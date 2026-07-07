using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.Repositories.Interfaces;

namespace Services.Email
{
    public enum SendGridHandleResult { Handled, BadSignature, Malformed }

    public interface ISendGridEventService
    {
        /// <summary>Verify + process a SendGrid Event Webhook delivery (a JSON array of events).</summary>
        Task<SendGridHandleResult> HandleAsync(string rawBody, string? signature, string? timestamp);
    }

    /// <summary>
    /// Turns SendGrid Event Webhook deliveries into suppression-list rows, mirroring the SES mapping:
    ///   Hard bounce                    -> scope 'all', platform-wide (the address is invalid for everyone)
    ///   Spam report                    -> scope 'marketing', scoped to the sending tenant if tagged
    ///   Unsubscribe/group unsubscribe  -> scope 'marketing', scoped to the sending tenant if tagged
    /// Delivery/open/click/deferred/processed events are ignored.
    ///
    /// The tenant arrives as the "tenant_id" custom arg (stamped on outbound sends via the X-SMTPAPI
    /// header); SendGrid copies custom args onto each event as top-level properties. Hard bounces
    /// ignore it on purpose, since an invalid address should be blocked globally.
    ///
    /// Trust anchor: SendGrid's Signed Event Webhook (ECDSA P-256 over timestamp + body, headers
    /// X-Twilio-Email-Event-Webhook-Signature / -Timestamp, public key from the SendGrid dashboard in
    /// Email:SendGrid:WebhookVerificationKey). Verification is on by default; only an explicit
    /// Email:SendGrid:VerifySignature=false disables it (local testing).
    /// </summary>
    public class SendGridEventService : ISendGridEventService
    {
        private readonly IEmailSuppressionRepository _suppression;
        private readonly IConfiguration _config;
        private readonly ILogger<SendGridEventService> _logger;

        public SendGridEventService(
            IEmailSuppressionRepository suppression,
            IConfiguration config,
            ILogger<SendGridEventService> logger)
        {
            _suppression = suppression;
            _config = config;
            _logger = logger;
        }

        public async Task<SendGridHandleResult> HandleAsync(string rawBody, string? signature, string? timestamp)
        {
            var verify = !string.Equals(_config["Email:SendGrid:VerifySignature"], "false", StringComparison.OrdinalIgnoreCase);
            if (verify)
            {
                if (!VerifySignature(rawBody, signature, timestamp))
                {
                    _logger.LogWarning("Rejected SendGrid event webhook: signature verification failed.");
                    return SendGridHandleResult.BadSignature;
                }
                // Replay guard: the timestamp is covered by the signature, so a captured delivery could
                // otherwise be replayed forever (e.g. to re-suppress an address after it resubscribed).
                if (!IsTimestampFresh(timestamp))
                {
                    _logger.LogWarning("Rejected SendGrid event webhook: timestamp outside the freshness window.");
                    return SendGridHandleResult.BadSignature;
                }
            }

            JsonElement events;
            try
            {
                events = JsonDocument.Parse(rawBody).RootElement;
            }
            catch
            {
                return SendGridHandleResult.Malformed;
            }
            if (events.ValueKind != JsonValueKind.Array) return SendGridHandleResult.Malformed;

            foreach (var ev in events.EnumerateArray())
            {
                var email = Str(ev, "email");
                if (string.IsNullOrWhiteSpace(email)) continue;
                var kind = Str(ev, "event");
                var tenantId = ExtractTenantId(ev);

                switch (kind)
                {
                    case "bounce":
                        // type 'blocked' is transient (full mailbox, greylisting); only hard bounces
                        // suppress, and globally: a dead address is dead for every tenant.
                        if (Str(ev, "type") == "blocked") break;
                        await SafeSuppress(null, email, "bounce", "all", "sendgrid_bounce", Detail(ev));
                        break;

                    case "dropped":
                        // SendGrid pre-emptively dropped the send. Only mirror the cases that mean the
                        // address itself is dead; policy drops (unsubscribed, spam-reported) are already
                        // covered by their own events.
                        var reason = Str(ev, "reason");
                        // Guard against our own config faults: a malformed X-SMTPAPI header makes SendGrid
                        // drop the whole run with reason "Invalid SMTPAPI header". A naive Contains("invalid")
                        // would then blocklist every recipient of that campaign platform-wide. Only suppress
                        // on reasons that name a dead address, and never on header/SMTPAPI drops.
                        var isHeaderDrop = reason.Contains("smtpapi", StringComparison.OrdinalIgnoreCase)
                            || reason.Contains("header", StringComparison.OrdinalIgnoreCase);
                        var isDeadAddress = reason.Equals("Invalid", StringComparison.OrdinalIgnoreCase)
                            || reason.Contains("bounced address", StringComparison.OrdinalIgnoreCase);
                        if (isDeadAddress && !isHeaderDrop)
                            await SafeSuppress(null, email, "bounce", "all", "sendgrid_dropped", reason);
                        break;

                    case "spamreport":
                        // Marketing-only so receipts still flow; scoped to the tenant if we know it,
                        // otherwise platform-wide marketing as the conservative default.
                        await SafeSuppress(tenantId, email, "complaint", "marketing", "sendgrid_complaint", Detail(ev));
                        break;

                    case "unsubscribe":
                    case "group_unsubscribe":
                        await SafeSuppress(tenantId, email, "unsubscribe", "marketing", "sendgrid_unsubscribe", kind);
                        break;

                    // delivered / open / click / processed / deferred / group_resubscribe: nothing to do.
                }
            }
            return SendGridHandleResult.Handled;
        }

        // ECDSA P-256 over UTF8(timestamp + rawBody); signature is base64 DER, public key is the base64
        // SPKI shown in the SendGrid dashboard when the Signed Event Webhook is enabled.
        private bool VerifySignature(string rawBody, string? signature, string? timestamp)
        {
            var publicKey = _config["Email:SendGrid:WebhookVerificationKey"];
            if (string.IsNullOrWhiteSpace(publicKey))
            {
                _logger.LogWarning("SendGrid webhook received but Email:SendGrid:WebhookVerificationKey is not set.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(timestamp)) return false;
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
                return ecdsa.VerifyData(
                    Encoding.UTF8.GetBytes(timestamp + rawBody),
                    Convert.FromBase64String(signature),
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying SendGrid webhook signature.");
                return false;
            }
        }

        // A single bad event must not fail the whole batch: SendGrid retries a non-2xx delivery with
        // the identical payload, so one un-writable row (e.g. an FK violation because the tagged tenant
        // was deleted) would otherwise loop forever and re-apply every other row on each retry. Swallow
        // and log the per-event failure; the batch still returns Handled.
        private async Task SafeSuppress(Guid? tenantId, string email, string reason, string scope, string source, string? detail)
        {
            try
            {
                await _suppression.Suppress(tenantId, email, reason, scope, source, detail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write SendGrid suppression for {Email} ({Reason}); skipping event.", email, reason);
            }
        }

        // The signed timestamp is unix seconds. Reject deliveries outside a 10-minute window so a
        // captured, still-validly-signed batch cannot be replayed later. Allows a small future skew.
        private bool IsTimestampFresh(string? timestamp)
        {
            if (!long.TryParse(timestamp, out var ts)) return false;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return Math.Abs(now - ts) <= 600;
        }

        // Custom args (stamped via X-SMTPAPI unique_args on outbound sends) arrive as top-level
        // properties on each event. Returns null when absent so the suppression lands platform-wide.
        private static Guid? ExtractTenantId(JsonElement ev) =>
            ev.TryGetProperty("tenant_id", out var v) && v.ValueKind == JsonValueKind.String
                && Guid.TryParse(v.GetString(), out var g) ? g : null;

        private static string? Detail(JsonElement ev)
        {
            var reason = Str(ev, "reason");
            var status = Str(ev, "status");
            var detail = string.Join(" ", new[] { status, reason }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return string.IsNullOrWhiteSpace(detail) ? null : detail[..Math.Min(detail.Length, 500)];
        }

        private static string Str(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }
}
