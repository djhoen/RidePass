using Microsoft.Extensions.Logging;
using Services.Repositories.Data.PlatformData;
using Services.Repositories.Interfaces;

namespace Services.Delivery
{
    public enum DeliveryChannel { Email, Sms }

    /// <summary>Current super-admin outbound delivery policy. An empty allowlist means "everyone".</summary>
    public sealed record OutboundDeliverySettings(
        bool EmailEnabled,
        IReadOnlyList<string> EmailAllowlist,
        bool SmsEnabled,
        IReadOnlyList<string> SmsAllowlist);

    /// <summary>
    /// Platform-wide kill switch + allowlist for EVERY outbound email and SMS, checked at the
    /// last hop (SmtpEmailer / TwilioSmsSender) so no caller can route around it. Exists for two
    /// reasons: staging carries a scrubbed clone of production (thousands of fake
    /// @highland.test riders that bounce and poison sender reputation the moment a real relay
    /// key lands), and incident response ("stop all mail NOW" without a deploy).
    /// Settings live in platform_setting and are read by both the web API and the TaskRunner,
    /// each with its own short cache, so a toggle takes effect within CacheTtl everywhere.
    /// </summary>
    public interface IOutboundDeliveryGate
    {
        /// <summary>Null when delivery may proceed; otherwise a human-readable reason it was blocked.</summary>
        Task<string?> BlockReason(DeliveryChannel channel, string recipient);
        Task<OutboundDeliverySettings> GetSettings();
        /// <summary>Drop this process's cached copy so the next check re-reads the database.</summary>
        void Invalidate();
    }

    public class OutboundDeliveryGate : IOutboundDeliveryGate
    {
        public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(15);

        private readonly IPlatformSettingRepository _settings;
        private readonly ILogger<OutboundDeliveryGate> _logger;
        private readonly object _lock = new();
        private OutboundDeliverySettings? _cached;
        private DateTime _cachedAtUtc;

        public OutboundDeliveryGate(IPlatformSettingRepository settings, ILogger<OutboundDeliveryGate> logger)
        {
            _settings = settings;
            _logger = logger;
        }

        public async Task<string?> BlockReason(DeliveryChannel channel, string recipient)
        {
            var s = await GetSettings();
            return channel switch
            {
                DeliveryChannel.Email => !s.EmailEnabled
                    ? "Outbound email is switched off by a super admin"
                    : EmailAllowed(s.EmailAllowlist, recipient)
                        ? null
                        : "Recipient is not on the super-admin email allowlist",
                DeliveryChannel.Sms => !s.SmsEnabled
                    ? "Outbound SMS is switched off by a super admin"
                    : SmsAllowed(s.SmsAllowlist, recipient)
                        ? null
                        : "Recipient is not on the super-admin SMS allowlist",
                _ => null,
            };
        }

        public async Task<OutboundDeliverySettings> GetSettings()
        {
            lock (_lock)
            {
                if (_cached is not null && DateTime.UtcNow - _cachedAtUtc < CacheTtl) return _cached;
            }

            OutboundDeliverySettings fresh;
            try
            {
                fresh = new OutboundDeliverySettings(
                    ParseBool(await _settings.Get(PlatformSettingKeys.OutboundEmailEnabled), defaultValue: true),
                    ParseList(await _settings.Get(PlatformSettingKeys.OutboundEmailAllowlist)),
                    ParseBool(await _settings.Get(PlatformSettingKeys.OutboundSmsEnabled), defaultValue: true),
                    ParseList(await _settings.Get(PlatformSettingKeys.OutboundSmsAllowlist)));
            }
            catch (Exception ex)
            {
                // Keep the last known policy rather than guessing. With nothing cached, fail
                // CLOSED: a transient DB blip must never turn into a flood of unintended mail.
                _logger.LogWarning(ex, "Could not read outbound delivery settings; {Fallback}",
                    _cached is null ? "blocking all outbound delivery until the next successful read" : "keeping the last known policy");
                lock (_lock)
                {
                    return _cached ?? new OutboundDeliverySettings(false, Array.Empty<string>(), false, Array.Empty<string>());
                }
            }

            lock (_lock)
            {
                _cached = fresh;
                _cachedAtUtc = DateTime.UtcNow;
                return fresh;
            }
        }

        public void Invalidate()
        {
            lock (_lock) { _cached = null; }
        }

        // --- Matching ------------------------------------------------------------------

        /// <summary>Allowlist entries are full addresses ("dan@x.com") or domains ("x.com" / "@x.com").</summary>
        public static bool EmailAllowed(IReadOnlyList<string> allowlist, string recipient)
        {
            if (allowlist.Count == 0) return true;
            var email = recipient.Trim().ToLowerInvariant();
            var at = email.LastIndexOf('@');
            var domain = at >= 0 ? email[(at + 1)..] : string.Empty;
            foreach (var raw in allowlist)
            {
                var entry = raw.Trim().ToLowerInvariant().TrimStart('@');
                if (entry.Length == 0) continue;
                if (entry.Contains('@')) { if (entry == email) return true; }
                else if (entry == domain) return true;
            }
            return false;
        }

        /// <summary>Allowlist entries are phone numbers in any formatting; compared digits-only, and a
        /// 10-digit entry matches its +1 E.164 form.</summary>
        public static bool SmsAllowed(IReadOnlyList<string> allowlist, string recipient)
        {
            if (allowlist.Count == 0) return true;
            var to = Digits(recipient);
            if (to.Length == 0) return false;
            foreach (var raw in allowlist)
            {
                var entry = Digits(raw);
                if (entry.Length < 7) continue;
                if (entry == to) return true;
                if (entry.Length >= 10 && to.EndsWith(entry, StringComparison.Ordinal)) return true;
                if (to.Length >= 10 && entry.EndsWith(to, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static IReadOnlyList<string> ParseList(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
            return raw.Split(new[] { '\n', '\r', ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool ParseBool(string? raw, bool defaultValue)
            => bool.TryParse(raw?.Trim(), out var b) ? b : defaultValue;

        private static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());
    }
}
