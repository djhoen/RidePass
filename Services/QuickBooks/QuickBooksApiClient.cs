using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Services.Accounting;
using Services.Repositories.Interfaces;

namespace Services.QuickBooks
{
    /// <summary>An account in the tenant's chart of accounts, for the mapping UI.</summary>
    public record QboAccount(string Id, string Name, string AccountType, string? AccountSubType, string? Classification);

    /// <summary>A posted journal entry.</summary>
    public record QboPostedEntry(string Id, string? DocNumber);

    /// <summary>Thrown when QBO rejects a call in a way a retry won't fix. Message is user-facing.</summary>
    public class QuickBooksApiException : Exception
    {
        public bool IsRetryable { get; }
        public QuickBooksApiException(string message, bool isRetryable = false) : base(message) => IsRetryable = isRetryable;
    }

    public interface IQuickBooksApiClient
    {
        Task<List<QboAccount>> ListAccountsAsync(Guid tenantId, CancellationToken ct = default);
        Task<QboPostedEntry> CreateJournalEntryAsync(Guid tenantId, JournalDraft draft, IReadOnlyDictionary<string, string> accountIdsByKey, string docNumber, CancellationToken ct = default);
        /// <summary>Round-trip proof the link still works, for the settings screen's Test button.</summary>
        Task<string?> GetCompanyNameAsync(Guid tenantId, CancellationToken ct = default);
    }

    /// <summary>
    /// Thin QBO REST client. Deliberately hand-rolled over HttpClient rather than pulling in
    /// Intuit's SDK: we use three endpoints, and the SDK drags in a large dependency surface for
    /// query-builder machinery we'd never touch.
    /// </summary>
    public class QuickBooksApiClient : IQuickBooksApiClient
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

        private readonly QuickBooksOptions _options;
        private readonly IQuickBooksTokenService _tokens;
        private readonly IQuickBooksRepository _repo;
        private readonly ILogger<QuickBooksApiClient> _logger;

        public QuickBooksApiClient(
            QuickBooksOptions options,
            IQuickBooksTokenService tokens,
            IQuickBooksRepository repo,
            ILogger<QuickBooksApiClient> logger)
        {
            _options = options;
            _tokens = tokens;
            _repo = repo;
            _logger = logger;
        }

        public async Task<List<QboAccount>> ListAccountsAsync(Guid tenantId, CancellationToken ct = default)
        {
            // maxresults 1000 covers any realistic chart of accounts; QBO caps a page at 1000 anyway.
            const string query = "select Id, Name, AccountType, AccountSubType, Classification from Account where Active = true maxresults 1000";
            var json = await SendAsync(tenantId, HttpMethod.Get, $"query?query={Uri.EscapeDataString(query)}", null, ct);

            var accounts = new List<QboAccount>();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("QueryResponse", out var qr) ||
                !qr.TryGetProperty("Account", out var arr))
            {
                return accounts;   // A company with no active accounts. Empty, not an error.
            }

            foreach (var a in arr.EnumerateArray())
            {
                var id = Str(a, "Id");
                var name = Str(a, "Name");
                if (id is null || name is null) continue;
                accounts.Add(new QboAccount(id, name, Str(a, "AccountType") ?? "", Str(a, "AccountSubType"), Str(a, "Classification")));
            }
            return accounts.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<string?> GetCompanyNameAsync(Guid tenantId, CancellationToken ct = default)
        {
            var conn = await _repo.GetConnection(tenantId);
            if (conn is null) return null;
            var json = await SendAsync(tenantId, HttpMethod.Get, $"companyinfo/{conn.RealmId}", null, ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("CompanyInfo", out var ci) ? Str(ci, "CompanyName") : null;
        }

        public async Task<QboPostedEntry> CreateJournalEntryAsync(
            Guid tenantId,
            JournalDraft draft,
            IReadOnlyDictionary<string, string> accountIdsByKey,
            string docNumber,
            CancellationToken ct = default)
        {
            var lines = new List<object>();
            foreach (var line in draft.Lines)
            {
                if (!accountIdsByKey.TryGetValue(line.AccountKey, out var accountId))
                {
                    // Fail the day rather than guess an account. A journal entry silently booked to
                    // the wrong account is far more expensive to unpick than a day that didn't post.
                    throw new QuickBooksApiException(
                        $"No QuickBooks account is mapped for \"{QboAccountKeys.Label(line.AccountKey)}\". " +
                        $"Set it under Settings → QuickBooks, then re-sync {draft.BusinessDate:yyyy-MM-dd}.");
                }

                lines.Add(new
                {
                    DetailType = "JournalEntryLineDetail",
                    Amount = Math.Round(line.AmountCents / 100m, 2),
                    Description = QboAccountKeys.Label(line.AccountKey),
                    JournalEntryLineDetail = new
                    {
                        PostingType = line.IsDebit ? "Debit" : "Credit",
                        AccountRef = new { value = accountId },
                    },
                });
            }

            var payload = new
            {
                // QBO derives the period from TxnDate, so this is what puts the money on the right
                // day. It's the tenant-LOCAL business date, see the Script0175 header.
                TxnDate = draft.BusinessDate.ToString("yyyy-MM-dd"),
                DocNumber = docNumber,
                PrivateNote = $"RidePass daily summary for {draft.BusinessDate:yyyy-MM-dd} ({draft.EntryCount} transactions).",
                Line = lines,
            };

            var json = await SendAsync(tenantId, HttpMethod.Post, "journalentry", JsonSerializer.Serialize(payload), ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("JournalEntry", out var je))
            {
                throw new QuickBooksApiException("QuickBooks accepted the request but returned no journal entry.");
            }
            return new QboPostedEntry(Str(je, "Id") ?? "", Str(je, "DocNumber"));
        }

        private async Task<string> SendAsync(Guid tenantId, HttpMethod method, string path, string? body, CancellationToken ct)
        {
            var conn = await _repo.GetConnection(tenantId);
            if (conn is null)
            {
                throw new QuickBooksApiException("QuickBooks is not connected for this track.");
            }

            var accessToken = await _tokens.GetAccessTokenAsync(tenantId, ct);
            if (accessToken is null)
            {
                // The token service has already written the specific reason onto the connection.
                throw new QuickBooksApiException(
                    conn.LastSyncError ?? "The QuickBooks connection is no longer valid. Reconnect QuickBooks under Settings → QuickBooks.");
            }

            var url = $"{_options.ApiBaseUrl}/{conn.RealmId}/{path}";
            if (path.Contains("query", StringComparison.Ordinal) is false && method == HttpMethod.Post)
            {
                url += "?minorversion=70";
            }

            using var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null)
            {
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new QuickBooksApiException($"Could not reach QuickBooks: {ex.Message}", isRetryable: true);
            }

            using (resp)
            {
                var text = await resp.Content.ReadAsStringAsync(ct);
                if (resp.IsSuccessStatusCode) return text;

                // 429 and 5xx are worth another pass tonight; everything else needs a human.
                var retryable = resp.StatusCode == HttpStatusCode.TooManyRequests || (int)resp.StatusCode >= 500;

                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    await _repo.SetStatus(tenantId, "expired",
                        "QuickBooks rejected the connection. Reconnect QuickBooks under Settings → QuickBooks.");
                    throw new QuickBooksApiException("QuickBooks rejected the connection. Reconnect QuickBooks under Settings → QuickBooks.");
                }

                _logger.LogWarning("QBO {Method} {Path} for tenant {TenantId} failed: {Status} {Body}",
                    method, path, tenantId, (int)resp.StatusCode, text);

                throw new QuickBooksApiException(
                    $"QuickBooks returned {(int)resp.StatusCode}: {ExtractIntuitError(text)}", retryable);
            }
        }

        /// <summary>
        /// Intuit buries the useful bit in Fault.Error[0].Detail. Surfacing the raw JSON blob to a
        /// track owner is useless, so dig out the message and fall back to a truncated body.
        /// </summary>
        private static string ExtractIntuitError(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("Fault", out var fault) &&
                    fault.TryGetProperty("Error", out var errors) &&
                    errors.GetArrayLength() > 0)
                {
                    var first = errors[0];
                    var msg = Str(first, "Message");
                    var detail = Str(first, "Detail");
                    return string.Join(", ", new[] { msg, detail }.Where(s => !string.IsNullOrWhiteSpace(s)));
                }
            }
            catch (JsonException)
            {
                // Not JSON (an HTML error page from a proxy, say). Fall through to the raw text.
            }
            return body.Length > 300 ? body[..300] : body;
        }

        private static string? Str(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) ? v.ToString() : null;
    }
}
