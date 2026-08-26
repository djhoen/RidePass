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

    /// <summary>
    /// A Class in the tenant's company, for the profit-center mapping UI. FullyQualifiedName is
    /// what a human recognises when classes are nested ("Retail:Bike Shop"); Name alone is the leaf.
    /// </summary>
    public record QboClass(string Id, string Name, string FullyQualifiedName);

    /// <summary>
    /// The company's class-tracking preference. Posting a ClassRef into a company that has class
    /// tracking switched off is rejected by QBO, so the settings screen reads this first and says
    /// so, rather than letting the tenant map classes and discover it at 2am when the sync fails.
    /// </summary>
    public record QboClassPreferences(bool TrackingEnabled, bool TrackingPerLine);

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
        /// <summary>The company's active classes, for mapping profit centers onto them.</summary>
        Task<List<QboClass>> ListClassesAsync(Guid tenantId, CancellationToken ct = default);
        /// <summary>Whether this company tracks classes at all, and whether it does so per line.</summary>
        Task<QboClassPreferences> GetClassPreferencesAsync(Guid tenantId, CancellationToken ct = default);
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

        public async Task<List<QboClass>> ListClassesAsync(Guid tenantId, CancellationToken ct = default)
        {
            const string query = "select Id, Name, FullyQualifiedName from Class where Active = true maxresults 1000";
            var json = await SendAsync(tenantId, HttpMethod.Get, $"query?query={Uri.EscapeDataString(query)}", null, ct);

            var classes = new List<QboClass>();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("QueryResponse", out var qr) ||
                !qr.TryGetProperty("Class", out var arr))
            {
                // A company with class tracking on but no classes created yet. Empty, not an error:
                // the settings screen says "you have no classes" and points at QuickBooks.
                return classes;
            }

            foreach (var c in arr.EnumerateArray())
            {
                var id = Str(c, "Id");
                var name = Str(c, "Name");
                if (id is null || name is null) continue;
                classes.Add(new QboClass(id, name, Str(c, "FullyQualifiedName") ?? name));
            }
            // Sorted by the qualified name so nested classes sit under their parent in the dropdown.
            return classes.OrderBy(c => c.FullyQualifiedName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<QboClassPreferences> GetClassPreferencesAsync(Guid tenantId, CancellationToken ct = default)
        {
            const string query = "select * from Preferences";
            var json = await SendAsync(tenantId, HttpMethod.Get, $"query?query={Uri.EscapeDataString(query)}", null, ct);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("QueryResponse", out var qr) ||
                !qr.TryGetProperty("Preferences", out var arr) ||
                arr.GetArrayLength() == 0 ||
                !arr[0].TryGetProperty("AccountingInfoPrefs", out var prefs))
            {
                // Can't tell. Report tracking as ON rather than blocking the screen on a shape we
                // didn't expect: the worst case is QBO rejecting the post with its own clear error,
                // whereas a false "tracking is off" would hide a feature the tenant really has.
                return new QboClassPreferences(true, true);
            }

            // QBO exposes the two modes separately: per whole transaction, or per line. A journal
            // entry needs the per-LINE mode to carry a different class on each revenue line.
            var perLine = Bool(prefs, "ClassTrackingPerTxnLine");
            var perTxn  = Bool(prefs, "ClassTrackingPerTxn");
            return new QboClassPreferences(perLine || perTxn, perLine);
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

                // A dictionary rather than an anonymous type because ClassRef has to be ABSENT,
                // not null, when the line carries no class: QBO rejects a null ref object, and an
                // anonymous type can't drop a property conditionally.
                var detail = new Dictionary<string, object>
                {
                    ["PostingType"] = line.IsDebit ? "Debit" : "Credit",
                    ["AccountRef"] = new { value = accountId },
                };
                // This is what puts the line in a profit center inside QuickBooks. Only revenue
                // lines ever carry one, and only once the tenant has mapped their centers to classes.
                if (line.ClassId is not null)
                {
                    detail["ClassRef"] = new { value = line.ClassId };
                }

                lines.Add(new
                {
                    DetailType = "JournalEntryLineDetail",
                    Amount = Math.Round(line.AmountCents / 100m, 2),
                    Description = QboAccountKeys.Label(line.AccountKey),
                    JournalEntryLineDetail = detail,
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

        /// <summary>
        /// QBO is inconsistent about booleans in Preferences: some come back as JSON true/false,
        /// some as the strings "true"/"false". Absent reads as false.
        /// </summary>
        private static bool Bool(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var v)) return false;
            return v.ValueKind switch
            {
                JsonValueKind.True   => true,
                JsonValueKind.False  => false,
                JsonValueKind.String => bool.TryParse(v.GetString(), out var b) && b,
                _                    => false,
            };
        }
    }
}
