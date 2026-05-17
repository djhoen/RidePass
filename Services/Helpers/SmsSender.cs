using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Services.Helpers
{
    public interface ISmsSender
    {
        bool IsConfigured { get; }
        Task<bool> Send(string toPhone, string body);
    }

    /// <summary>
    /// Twilio SMS via the Messages REST API. Config-gated: when Sms:Twilio:AccountSid /
    /// AuthToken / FromNumber are missing, IsConfigured is false and Send is a silent
    /// no-op so notification fan-out doesn't have to special-case unconfigured deploys.
    /// Phone numbers are normalized to E.164 (US default) before sending.
    /// </summary>
    public class TwilioSmsSender : ISmsSender
    {
        // One process-wide client is fine for low-volume SMS — connection pooling is automatic.
        private static readonly HttpClient _http = new();

        private readonly IConfiguration _config;
        private readonly ILogger<TwilioSmsSender> _logger;

        public bool IsConfigured { get; }

        public TwilioSmsSender(IConfiguration config, ILogger<TwilioSmsSender> logger)
        {
            _config = config;
            _logger = logger;
            IsConfigured = !string.IsNullOrWhiteSpace(config["Sms:Twilio:AccountSid"])
                        && !string.IsNullOrWhiteSpace(config["Sms:Twilio:AuthToken"])
                        && !string.IsNullOrWhiteSpace(config["Sms:Twilio:FromNumber"]);
        }

        public async Task<bool> Send(string toPhone, string body)
        {
            if (!IsConfigured) return false;
            try
            {
                var sid = _config["Sms:Twilio:AccountSid"]!;
                var token = _config["Sms:Twilio:AuthToken"]!;
                var from = _config["Sms:Twilio:FromNumber"]!;
                var to = NormalizeE164(toPhone);
                if (to is null) return false;

                using var req = new HttpRequestMessage(HttpMethod.Post,
                    $"https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages.json");
                var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sid}:{token}"));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
                req.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("From", from),
                    new KeyValuePair<string, string>("To", to),
                    new KeyValuePair<string, string>("Body", body),
                });

                using var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    var detail = await resp.Content.ReadAsStringAsync();
                    _logger.LogWarning("Twilio rejected SMS to {Phone}: {Status} {Detail}", to, (int)resp.StatusCode, detail);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send SMS to {Phone}", toPhone);
                return false;
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
    }
}
