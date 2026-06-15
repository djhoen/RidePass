using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.Repositories.Interfaces;

namespace Services.Email
{
    /// <summary>
    /// Turns Amazon SES bounce/complaint events (delivered as SNS notifications) into
    /// suppression-list rows. Mapping:
    ///   Permanent bounce  -> scope 'all', platform-wide (the address is invalid for everyone)
    ///   Complaint         -> scope 'marketing', scoped to the sending tenant if tagged
    /// Transient bounces and delivery notifications are ignored.
    ///
    /// The tenant is read from the SES message tag "tenant_id" (stamped by our sender). Hard
    /// bounces ignore the tag on purpose, since an invalid address should be blocked globally.
    /// </summary>
    public class SesNotificationService : ISesNotificationService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly ConcurrentDictionary<string, X509Certificate2> _certCache = new();

        private readonly IEmailSuppressionRepository _suppression;
        private readonly IConfiguration _config;
        private readonly ILogger<SesNotificationService> _logger;

        public SesNotificationService(
            IEmailSuppressionRepository suppression,
            IConfiguration config,
            ILogger<SesNotificationService> logger)
        {
            _suppression = suppression;
            _config = config;
            _logger = logger;
        }

        public async Task<SesHandleResult> HandleAsync(string rawJson)
        {
            JsonElement env;
            try
            {
                env = JsonDocument.Parse(rawJson).RootElement;
            }
            catch
            {
                return SesHandleResult.Malformed;
            }

            var type = Str(env, "Type");
            if (string.IsNullOrEmpty(type)) return SesHandleResult.Malformed;

            // Signature verification on by default; only an explicit "false" disables it (local testing).
            var verifySignature = !string.Equals(_config["Email:Ses:VerifySignature"], "false", StringComparison.OrdinalIgnoreCase);
            if (verifySignature)
            {
                if (!await VerifySnsSignature(env))
                {
                    _logger.LogWarning("Rejected SNS message {MessageId}: signature verification failed.", Str(env, "MessageId"));
                    return SesHandleResult.BadSignature;
                }
            }

            switch (type)
            {
                case "SubscriptionConfirmation":
                    await ConfirmSubscription(env);
                    return SesHandleResult.Handled;

                case "Notification":
                    await HandleNotification(env);
                    return SesHandleResult.Handled;

                default:
                    // UnsubscribeConfirmation and anything else: nothing to do.
                    return SesHandleResult.Handled;
            }
        }

        // ── SES event mapping ───────────────────────────────────────────────────────

        private async Task HandleNotification(JsonElement env)
        {
            var messageRaw = Str(env, "Message");
            if (string.IsNullOrEmpty(messageRaw)) return;

            JsonElement msg;
            try
            {
                msg = JsonDocument.Parse(messageRaw).RootElement;
            }
            catch
            {
                _logger.LogWarning("SES notification Message was not valid JSON.");
                return;
            }

            var notificationType = Str(msg, "notificationType");
            var tenantId = ExtractTenantId(msg);

            if (notificationType == "Bounce" && msg.TryGetProperty("bounce", out var bounce))
            {
                // Only permanent bounces are suppressed; transient bounces may recover.
                if (Str(bounce, "bounceType") != "Permanent") return;
                var subType = Str(bounce, "bounceSubType");
                foreach (var addr in Recipients(bounce, "bouncedRecipients"))
                {
                    // Global: a dead address is dead for every tenant.
                    await _suppression.Suppress(null, addr, "bounce", "all", "ses_bounce", subType);
                }
            }
            else if (notificationType == "Complaint" && msg.TryGetProperty("complaint", out var complaint))
            {
                var feedback = Str(complaint, "complaintFeedbackType");
                foreach (var addr in Recipients(complaint, "complainedRecipients"))
                {
                    // Marketing-only so receipts still flow; scoped to the tenant if we know it,
                    // otherwise platform-wide marketing as the conservative default.
                    await _suppression.Suppress(tenantId, addr, "complaint", "marketing", "ses_complaint", feedback);
                }
            }
        }

        // SES message tags arrive as { "tenant_id": ["<guid>"] }. Returns null when absent/invalid
        // so the suppression lands platform-wide.
        private static Guid? ExtractTenantId(JsonElement msg)
        {
            if (!msg.TryGetProperty("mail", out var mail)) return null;
            if (!mail.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object) return null;
            if (!tags.TryGetProperty("tenant_id", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
            foreach (var v in arr.EnumerateArray())
            {
                if (v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g)) return g;
            }
            return null;
        }

        private static IEnumerable<string> Recipients(JsonElement parent, string prop)
        {
            if (!parent.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) yield break;
            foreach (var r in arr.EnumerateArray())
            {
                var email = Str(r, "emailAddress");
                if (!string.IsNullOrWhiteSpace(email)) yield return email;
            }
        }

        // ── SNS plumbing ──────────────────────────────────────────────────────────────

        private async Task ConfirmSubscription(JsonElement env)
        {
            var url = Str(env, "SubscribeURL");
            if (string.IsNullOrEmpty(url) || !IsAwsUrl(url))
            {
                _logger.LogWarning("SNS SubscriptionConfirmation had a missing or non-AWS SubscribeURL.");
                return;
            }
            try
            {
                await _http.GetAsync(url);
                _logger.LogInformation("Confirmed SNS subscription for topic {TopicArn}.", Str(env, "TopicArn"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to confirm SNS subscription.");
            }
        }

        private async Task<bool> VerifySnsSignature(JsonElement env)
        {
            try
            {
                var certUrl = Str(env, "SigningCertURL");
                if (string.IsNullOrEmpty(certUrl) || !IsAwsUrl(certUrl)) return false;

                var signature = Str(env, "Signature");
                if (string.IsNullOrEmpty(signature)) return false;

                var canonical = BuildCanonicalString(env);
                if (canonical is null) return false;

                var cert = await GetCert(certUrl);
                using var rsa = cert.GetRSAPublicKey();
                if (rsa is null) return false;

                var hashAlg = Str(env, "SignatureVersion") == "2" ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA1;
                return rsa.VerifyData(
                    Encoding.UTF8.GetBytes(canonical),
                    Convert.FromBase64String(signature),
                    hashAlg,
                    RSASignaturePadding.Pkcs1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying SNS signature.");
                return false;
            }
        }

        // Canonical string is a fixed field order per message type, each "key\nvalue\n".
        private static string? BuildCanonicalString(JsonElement env)
        {
            var type = Str(env, "Type");
            var sb = new StringBuilder();

            void Add(string key)
            {
                sb.Append(key).Append('\n').Append(Str(env, key)).Append('\n');
            }

            if (type == "Notification")
            {
                Add("Message");
                Add("MessageId");
                if (env.TryGetProperty("Subject", out var subj) && subj.ValueKind == JsonValueKind.String) Add("Subject");
                Add("Timestamp");
                Add("TopicArn");
                Add("Type");
            }
            else if (type == "SubscriptionConfirmation" || type == "UnsubscribeConfirmation")
            {
                Add("Message");
                Add("MessageId");
                Add("SubscribeURL");
                Add("Timestamp");
                Add("Token");
                Add("TopicArn");
                Add("Type");
            }
            else
            {
                return null;
            }
            return sb.ToString();
        }

        private static async Task<X509Certificate2> GetCert(string url)
        {
            if (_certCache.TryGetValue(url, out var cached)) return cached;
            var pem = await _http.GetStringAsync(url);
            var cert = X509Certificate2.CreateFromPem(pem);
            _certCache.TryAdd(url, cert);
            return cert;
        }

        // Guard against SSRF: only https hosts under amazonaws.com.
        private static bool IsAwsUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && (uri.Host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.Equals("amazonaws.com", StringComparison.OrdinalIgnoreCase));
        }

        private static string Str(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }
}
