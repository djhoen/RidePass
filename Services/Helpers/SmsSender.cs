using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.Repositories.Data.MessagingData;
using Services.Repositories.Data.TenantData;
using Services.Repositories.Interfaces;

namespace Services.Helpers
{
    public interface ISmsSender
    {
        /// <summary>
        /// True when the global Sms:Twilio:* config keys are populated. This is
        /// the legacy / transition fallback used by code paths that don't yet
        /// have a tenant in hand; new code should call <see cref="IsConfiguredFor"/>.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// True if SMS can be sent for this tenant — either because the tenant
        /// has provisioned per-tenant Twilio credentials (and SmsEnabled is on)
        /// or because the global fallback is configured.
        /// </summary>
        bool IsConfiguredFor(Tenant tenant);

        /// <summary>
        /// Send using global credentials only. Returns false (no-op) when the
        /// global config isn't populated. Prefer the tenant-aware overload —
        /// this exists for callers that genuinely have no tenant context, and
        /// does NOT persist to tenant_message (no tenant to attribute it to).
        /// </summary>
        Task<bool> Send(string toPhone, string body);

        /// <summary>
        /// Send using the tenant's provisioned Twilio Subaccount when present,
        /// otherwise the global fallback. On successful send, persists an
        /// outbound row to tenant_message so the admin Inbox shows it as part
        /// of the conversation thread. Returns false when neither credential
        /// source is configured (silent no-op).
        ///
        /// sentByUserId attributes the outbound row in tenant_message — pass
        /// the admin's user id from the Inbox Reply path, leave null for
        /// system-initiated sends (waitlist promotion, scheduled rider
        /// messages, etc.) so the Inbox can distinguish "human reply" from
        /// "automated send".
        /// </summary>
        Task<bool> Send(Tenant tenant, string toPhone, string body, Guid? sentByUserId = null);
    }

    /// <summary>
    /// Twilio SMS via the Messages REST API. Resolves credentials per-tenant
    /// first (from the tenant row's twilio_subaccount_sid + decrypted token +
    /// twilio_from_number, gated by sms_enabled), falling back to the global
    /// Sms:Twilio:* config during the per-tenant rollout. Once every tenant is
    /// provisioned the global fallback can be deleted; until then it keeps
    /// existing notification paths working for tenants that haven't gone
    /// through Settings → SMS yet.
    ///
    /// Tenant-aware sends also write to tenant_message so the admin Inbox
    /// thread shows both halves of the conversation. The persist is best-effort
    /// — Twilio already accepted the message, so a DB failure here doesn't
    /// fail the caller; it just leaves the outbound message unrecorded in the
    /// thread (logged for investigation).
    /// </summary>
    public class TwilioSmsSender : ISmsSender
    {
        // One process-wide client is fine for low-volume SMS — connection pooling is automatic.
        private static readonly HttpClient _http = new();

        private readonly ITenantConversationRepository _conversations;
        private readonly ITenantSmsOptOutRepository _optOuts;
        private readonly ILogger<TwilioSmsSender> _logger;
        private readonly string? _globalSid;
        private readonly string? _globalToken;
        private readonly string? _globalFrom;
        // When set, every outbound message tells Twilio to POST delivery status
        // updates here. The webhook handler creates billing-ledger rows once
        // Twilio reports the final Price. Unset = no callback = no billing.
        private readonly string? _statusCallbackUrl;

        public bool IsConfigured => _globalSid is not null && _globalToken is not null && _globalFrom is not null;

        public TwilioSmsSender(
            IConfiguration config,
            ITenantConversationRepository conversations,
            ITenantSmsOptOutRepository optOuts,
            ILogger<TwilioSmsSender> logger)
        {
            _conversations = conversations;
            _optOuts = optOuts;
            _logger = logger;
            _globalSid = NullIfEmpty(config["Sms:Twilio:AccountSid"]);
            _globalToken = NullIfEmpty(config["Sms:Twilio:AuthToken"]);
            _globalFrom = NullIfEmpty(config["Sms:Twilio:FromNumber"]);
            _statusCallbackUrl = NullIfEmpty(config["Sms:Twilio:StatusCallbackUrl"]);
        }

        public bool IsConfiguredFor(Tenant tenant) => ResolveCredentials(tenant) is not null;

        public async Task<bool> Send(string toPhone, string body)
        {
            if (!IsConfigured) return false;
            // Global fallback path predates Messaging Services — there's no
            // platform-level MG SID. Send via raw From.
            var result = await SendInternal(_globalSid!, _globalToken!, _globalFrom!, messagingServiceSid: null, toPhone, body);
            return result is not null;
        }

        public async Task<bool> Send(Tenant tenant, string toPhone, string body, Guid? sentByUserId = null)
        {
            var creds = ResolveCredentials(tenant);
            if (creds is null) return false;
            var (sid, token, from, messagingServiceSid) = creds.Value;

            // Suppression check before we hit Twilio. The opt-out row is keyed
            // by E.164, same normalization SendInternal uses — normalize here
            // so the lookup actually finds the row even when the caller passes
            // a raw 10-digit number. A failed normalization (null) means we
            // can't reliably suppress; fall through to SendInternal which will
            // also reject the malformed number.
            var normalizedTo = NormalizeE164(toPhone);
            if (normalizedTo is not null && await _optOuts.IsOptedOut(tenant.Id, normalizedTo))
            {
                _logger.LogInformation(
                    "Suppressing SMS to {Phone} for tenant {TenantId}: opted out",
                    normalizedTo, tenant.Id);
                return false;
            }

            var result = await SendInternal(sid, token, from, messagingServiceSid, toPhone, body);
            if (result is null) return false;

            // Persist the outbound message to tenant_message so the Inbox
            // thread shows it. Best-effort — the SMS already left Twilio so a
            // DB failure here shouldn't fail the caller.
            try
            {
                if (normalizedTo is not null)
                {
                    var conversation = await _conversations.FindOrCreate(tenant.Id, normalizedTo, customerUserId: null);
                    await _conversations.AppendMessage(new TenantMessage
                    {
                        ConversationId = conversation.Id,
                        TenantId = tenant.Id,
                        Direction = "outbound",
                        Body = body,
                        TwilioMessageSid = result.Sid,
                        Status = result.Status,
                        NumSegments = result.NumSegments,
                        SentByUserId = sentByUserId,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Sent SMS to {Phone} for tenant {TenantId} but failed to persist tenant_message",
                    toPhone, tenant.Id);
            }

            return true;
        }

        private (string Sid, string Token, string From, string? MessagingServiceSid)? ResolveCredentials(Tenant tenant)
        {
            if (tenant.SmsEnabled
                && !string.IsNullOrWhiteSpace(tenant.TwilioSubaccountSid)
                && !string.IsNullOrWhiteSpace(tenant.TwilioAuthTokenEncrypted)
                && !string.IsNullOrWhiteSpace(tenant.TwilioFromNumber))
            {
                var token = EncryptionHelper.Decrypt(tenant.TwilioAuthTokenEncrypted);
                if (!string.IsNullOrEmpty(token))
                {
                    // MessagingServiceSid is preferred when present; SendInternal
                    // uses From only as the fallback. Pre-MG tenants have null
                    // here and keep working via the From path.
                    return (tenant.TwilioSubaccountSid!, token, tenant.TwilioFromNumber!,
                        string.IsNullOrWhiteSpace(tenant.TwilioMessagingServiceSid) ? null : tenant.TwilioMessagingServiceSid);
                }
                // Encrypted blob decrypts to nothing — almost always means the
                // encryption key rotated since the credentials were stored.
                // Log and fall through to global so the tenant isn't dead in
                // the water until they re-provision.
                _logger.LogWarning(
                    "Tenant {TenantId} Twilio auth token failed to decrypt; falling back to global config",
                    tenant.Id);
            }

            return IsConfigured ? (_globalSid!, _globalToken!, _globalFrom!, null) : null;
        }

        private record SendResult(string Sid, string Status, int? NumSegments);

        private async Task<SendResult?> SendInternal(string sid, string token, string from, string? messagingServiceSid, string toPhone, string body)
        {
            try
            {
                var to = NormalizeE164(toPhone);
                if (to is null) return null;

                using var req = new HttpRequestMessage(HttpMethod.Post,
                    $"https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages.json");
                var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sid}:{token}"));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

                // Twilio expects either MessagingServiceSid OR From, not both.
                // Routing through the MG (when present) lets Twilio pick the
                // best sender from the pool and apply sticky-sender for
                // two-way threads. The From path stays alive for tenants
                // provisioned before Script0089 introduced the MG column.
                var formBody = new List<KeyValuePair<string, string>>
                {
                    new("To", to),
                    new("Body", body),
                };
                if (!string.IsNullOrEmpty(messagingServiceSid))
                {
                    formBody.Add(new("MessagingServiceSid", messagingServiceSid));
                }
                else
                {
                    formBody.Add(new("From", from));
                }
                if (_statusCallbackUrl is not null)
                {
                    formBody.Add(new("StatusCallback", _statusCallbackUrl));
                    formBody.Add(new("StatusCallbackMethod", "POST"));
                }
                req.Content = new FormUrlEncodedContent(formBody);

                using var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    var detail = await resp.Content.ReadAsStringAsync();
                    _logger.LogWarning("Twilio rejected SMS to {Phone}: {Status} {Detail}", to, (int)resp.StatusCode, detail);
                    return null;
                }

                // Parse Twilio's JSON response to grab the SID and initial
                // status. Twilio returns num_segments as a string ("1", "2"),
                // so parse it explicitly. Missing fields → log + treat as
                // success-but-unrecorded (we accept Twilio's word that the
                // message left, even if we can't link it back later).
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var messageSid = doc.RootElement.TryGetProperty("sid", out var sidProp) ? sidProp.GetString() : null;
                var status = doc.RootElement.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
                int? numSegments = null;
                if (doc.RootElement.TryGetProperty("num_segments", out var nsProp)
                    && int.TryParse(nsProp.GetString(), out var nsInt) && nsInt > 0)
                {
                    numSegments = nsInt;
                }

                if (string.IsNullOrEmpty(messageSid) || string.IsNullOrEmpty(status))
                {
                    _logger.LogWarning("Twilio response missing sid/status for {Phone}", to);
                    return null;
                }

                return new SendResult(messageSid, status, numSegments);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send SMS to {Phone}", toPhone);
                return null;
            }
        }

        public static string? NormalizeE164(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var trimmed = raw.Trim();
            if (trimmed.StartsWith("+")) return "+" + new string(trimmed.Skip(1).Where(char.IsDigit).ToArray());
            var digits = new string(trimmed.Where(char.IsDigit).ToArray());
            if (digits.Length == 10) return "+1" + digits;            // US default
            if (digits.Length == 11 && digits.StartsWith("1")) return "+" + digits;
            if (digits.Length >= 10) return "+" + digits;
            return null;
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
