using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.Helpers;
using Services.Repositories.Data.TenantData;
using Services.Repositories.Interfaces;

namespace Services.Sms
{
    /// <summary>
    /// Creates a per-tenant Twilio Subaccount, buys a toll-free phone number
    /// under it, configures the inbound webhook URL on the number, encrypts
    /// the subaccount auth token, and writes the credentials onto the tenant
    /// row. One-shot: callers don't see the auth token or intermediate state
    /// (the only thing returned is the SID + number so the UI can confirm).
    /// </summary>
    public interface ITwilioSubaccountProvisioner
    {
        /// <summary>True when the global master Twilio credentials are present.</summary>
        bool IsMasterConfigured { get; }

        /// <summary>
        /// Search Twilio's inventory for toll-free US numbers, optionally
        /// filtering by area code (e.g. "833"). Returns up to <paramref name="max"/>
        /// candidates — tenant picks one and passes its phone_number back to
        /// <see cref="ProvisionTenant"/>.
        /// </summary>
        Task<IReadOnlyList<AvailableTwilioNumber>> SearchTollFreeNumbers(
            string? areaCode, int max = 10, CancellationToken ct = default);

        /// <summary>
        /// Create subaccount → buy number → encrypt token → persist credentials
        /// → flip sms_enabled on. Refuses to run if the tenant already has a
        /// provisioned number (caller must <see cref="ReleaseTenant"/> first).
        /// Throws <see cref="TwilioProvisioningException"/> on any failure with
        /// a tenant-safe error message.
        /// </summary>
        Task<TwilioProvisionResult> ProvisionTenant(
            Tenant tenant, string phoneNumber, CancellationToken ct = default);

        /// <summary>
        /// Permanently release the tenant's Twilio SMS provisioning. Closes
        /// the subaccount on Twilio's side (which cascades to releasing the
        /// phone number and deleting the Messaging Service) and clears the
        /// credentials from the tenant row. Destructive — the number goes
        /// back to Twilio's inventory and a future provision will get a new
        /// one. Idempotent: a tenant without any Twilio columns set is a no-op.
        /// </summary>
        Task ReleaseTenant(Tenant tenant, CancellationToken ct = default);
    }

    public record AvailableTwilioNumber(
        string PhoneNumber,     // E.164, e.g. +18885551234
        string FriendlyName,    // human-formatted, e.g. (888) 555-1234
        string Region,          // usually blank for toll-free
        string IsoCountry);     // "US"

    public record TwilioProvisionResult(string SubaccountSid, string PhoneNumber, string? MessagingServiceSid);

    public class TwilioProvisioningException : Exception
    {
        public TwilioProvisioningException(string message) : base(message) { }
    }

    public class TwilioSubaccountProvisioner : ITwilioSubaccountProvisioner
    {
        private static readonly HttpClient _http = new();

        private readonly ITenantRepository _tenants;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TwilioSubaccountProvisioner> _logger;
        private readonly string? _masterSid;
        private readonly string? _masterToken;
        private readonly string? _inboundWebhookUrl;

        // Search results are global to the master account (same answer for
        // every tenant given the same areaCode+max), so a short cache absorbs
        // the typical UI burst — admin typing "8", "83", "833" all return
        // from cache after the first call, and the inventory doesn't change
        // fast enough for 45s of staleness to matter. On purchase we verify
        // freshness via the buy API anyway.
        private static readonly TimeSpan SearchCacheTtl = TimeSpan.FromSeconds(45);

        public bool IsMasterConfigured => _masterSid is not null && _masterToken is not null;

        public TwilioSubaccountProvisioner(
            IConfiguration config,
            ITenantRepository tenants,
            IMemoryCache cache,
            ILogger<TwilioSubaccountProvisioner> logger)
        {
            _tenants = tenants;
            _cache = cache;
            _logger = logger;
            // Master credentials are the same Sms:Twilio:* keys TwilioSmsSender
            // uses for its global fallback — there's only one master account.
            _masterSid = NullIfEmpty(config["Sms:Twilio:AccountSid"]);
            _masterToken = NullIfEmpty(config["Sms:Twilio:AuthToken"]);
            // Optional: where Twilio POSTs inbound SMS for tenant numbers.
            // Until the inbound conversation feature ships, leaving this unset
            // is OK — provisioning still succeeds; the number just won't have
            // an inbound handler. Set Sms:Twilio:InboundSmsWebhookUrl when the
            // /api/Twilio/IncomingSms endpoint exists.
            _inboundWebhookUrl = NullIfEmpty(config["Sms:Twilio:InboundSmsWebhookUrl"]);
        }

        public async Task<IReadOnlyList<AvailableTwilioNumber>> SearchTollFreeNumbers(
            string? areaCode, int max = 10, CancellationToken ct = default)
        {
            EnsureMasterConfigured();

            var cacheKey = $"twilio:tollfree:{areaCode ?? "any"}:{max}";
            if (_cache.TryGetValue<IReadOnlyList<AvailableTwilioNumber>>(cacheKey, out var hit) && hit is not null)
            {
                return hit;
            }

            var url = $"https://api.twilio.com/2010-04-01/Accounts/{_masterSid}/AvailablePhoneNumbers/US/TollFree.json"
                    + $"?SmsEnabled=true&Limit={Math.Clamp(max, 1, 30)}";
            if (!string.IsNullOrWhiteSpace(areaCode))
            {
                url += $"&AreaCode={Uri.EscapeDataString(areaCode)}";
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = BasicAuth(_masterSid!, _masterToken!);

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                throw new TwilioProvisioningException(
                    $"Twilio number search failed ({(int)resp.StatusCode}): {Truncate(detail, 400)}");
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<AvailableTwilioNumber>();
            if (doc.RootElement.TryGetProperty("available_phone_numbers", out var arr))
            {
                foreach (var item in arr.EnumerateArray())
                {
                    list.Add(new AvailableTwilioNumber(
                        item.GetProperty("phone_number").GetString() ?? "",
                        item.GetProperty("friendly_name").GetString() ?? "",
                        item.TryGetProperty("region", out var r) ? r.GetString() ?? "" : "",
                        item.TryGetProperty("iso_country", out var c) ? c.GetString() ?? "" : ""));
                }
            }
            _cache.Set(cacheKey, (IReadOnlyList<AvailableTwilioNumber>)list, SearchCacheTtl);
            return list;
        }

        public async Task<TwilioProvisionResult> ProvisionTenant(
            Tenant tenant, string phoneNumber, CancellationToken ct = default)
        {
            EnsureMasterConfigured();

            if (!string.IsNullOrWhiteSpace(tenant.TwilioSubaccountSid))
            {
                // Re-provisioning would orphan the existing subaccount + number
                // (and we'd keep paying for the old one). Force the caller to
                // explicitly release first — release flow isn't built yet in v1.
                throw new TwilioProvisioningException(
                    "Tenant already has SMS provisioned. Release the existing number before provisioning a new one.");
            }

            var friendlyName = $"RidePass · {tenant.DisplayName} · {tenant.Subdomain}";
            var (subaccountSid, subaccountToken) = await CreateSubaccount(friendlyName, ct);

            string phoneNumberSid;
            string messagingServiceSid;
            try
            {
                // Buy first, then create the Messaging Service, then attach.
                // Ordered this way so a sold-out number fails before we create
                // any MG (which would otherwise need its own rollback).
                phoneNumberSid = await BuyNumber(subaccountSid, subaccountToken, phoneNumber, ct);
                messagingServiceSid = await CreateMessagingService(subaccountSid, subaccountToken, friendlyName, ct);
                await AttachNumberToMessagingService(subaccountSid, subaccountToken, messagingServiceSid, phoneNumberSid, ct);
            }
            catch
            {
                // Don't leave an orphan subaccount on Twilio's side if any
                // step fails. Closing the subaccount also releases the
                // number and any MG inside it, so we don't need separate
                // rollbacks for the number/MG.
                await TryCloseSubaccount(subaccountSid, ct);
                throw;
            }

            var encryptedToken = EncryptionHelper.Encrypt(subaccountToken, null);
            await _tenants.SetTwilioCredentials(
                tenant.Id, subaccountSid, encryptedToken, phoneNumber, messagingServiceSid);

            _logger.LogInformation(
                "Provisioned Twilio SMS for tenant {TenantId} ({Subdomain}) — subaccount {Sid}, MG {MgSid}, number {Number}",
                tenant.Id, tenant.Subdomain, subaccountSid, messagingServiceSid, phoneNumber);

            return new TwilioProvisionResult(subaccountSid, phoneNumber, messagingServiceSid);
        }

        public async Task ReleaseTenant(Tenant tenant, CancellationToken ct = default)
        {
            EnsureMasterConfigured();

            if (string.IsNullOrWhiteSpace(tenant.TwilioSubaccountSid))
            {
                // Idempotent: tenant never provisioned (or already released).
                return;
            }

            var subaccountSid = tenant.TwilioSubaccountSid;

            // Close the subaccount on the master account's authority. Twilio
            // cascades: the phone number returns to inventory, the Messaging
            // Service is deleted, billing stops. Closed is terminal — there's
            // no "reopen", so a future re-provision creates a fresh subaccount.
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.twilio.com/2010-04-01/Accounts/{subaccountSid}.json")
            {
                Headers = { Authorization = BasicAuth(_masterSid!, _masterToken!) },
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("Status", "closed"),
                }),
            };

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                throw new TwilioProvisioningException(
                    $"Couldn't close Twilio subaccount ({(int)resp.StatusCode}): {Truncate(detail, 400)}");
            }

            // Clear credentials last so a Twilio failure doesn't leave the DB
            // saying "released" while Twilio still has the number live.
            await _tenants.ClearTwilioCredentials(tenant.Id);

            _logger.LogInformation(
                "Released Twilio SMS for tenant {TenantId} ({Subdomain}) — closed subaccount {Sid}",
                tenant.Id, tenant.Subdomain, subaccountSid);
        }

        private async Task<(string Sid, string Token)> CreateSubaccount(string friendlyName, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://api.twilio.com/2010-04-01/Accounts.json")
            {
                Headers = { Authorization = BasicAuth(_masterSid!, _masterToken!) },
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("FriendlyName", friendlyName),
                }),
            };

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                throw new TwilioProvisioningException(
                    $"Couldn't create Twilio subaccount ({(int)resp.StatusCode}): {Truncate(detail, 400)}");
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var sid = doc.RootElement.GetProperty("sid").GetString();
            var token = doc.RootElement.GetProperty("auth_token").GetString();
            if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(token))
            {
                throw new TwilioProvisioningException("Twilio returned a subaccount without sid/auth_token.");
            }
            return (sid, token);
        }

        private async Task<string> BuyNumber(string subaccountSid, string subaccountToken, string phoneNumber, CancellationToken ct)
        {
            // Note: SmsUrl is intentionally NOT set on the IncomingPhoneNumber
            // — the inbound webhook is configured on the owning Messaging
            // Service instead (see CreateMessagingService). When a number is
            // attached to an MG, Twilio routes inbound through the MG's
            // InboundRequestUrl regardless of the per-number setting.
            var body = new List<KeyValuePair<string, string>>
            {
                new("PhoneNumber", phoneNumber),
            };

            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.twilio.com/2010-04-01/Accounts/{subaccountSid}/IncomingPhoneNumbers.json")
            {
                Headers = { Authorization = BasicAuth(subaccountSid, subaccountToken) },
                Content = new FormUrlEncodedContent(body),
            };

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                throw new TwilioProvisioningException(
                    $"Couldn't buy {phoneNumber} ({(int)resp.StatusCode}): {Truncate(detail, 400)}");
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var sid = doc.RootElement.TryGetProperty("sid", out var sidProp) ? sidProp.GetString() : null;
            if (string.IsNullOrEmpty(sid))
            {
                throw new TwilioProvisioningException(
                    $"Twilio returned a purchased number without a sid for {phoneNumber}.");
            }
            return sid;
        }

        private async Task<string> CreateMessagingService(
            string subaccountSid, string subaccountToken, string friendlyName, CancellationToken ct)
        {
            // The Messaging Service is created inside the subaccount, so its
            // authentication uses the subaccount's credentials. InboundRequestUrl
            // here is what makes the existing /api/Twilio/IncomingSms webhook
            // continue to receive inbound for this tenant.
            var body = new List<KeyValuePair<string, string>>
            {
                new("FriendlyName", friendlyName),
            };
            if (!string.IsNullOrWhiteSpace(_inboundWebhookUrl))
            {
                body.Add(new("InboundRequestUrl", _inboundWebhookUrl));
                body.Add(new("InboundMethod", "POST"));
            }

            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://messaging.twilio.com/v1/Services")
            {
                Headers = { Authorization = BasicAuth(subaccountSid, subaccountToken) },
                Content = new FormUrlEncodedContent(body),
            };

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                throw new TwilioProvisioningException(
                    $"Couldn't create Twilio Messaging Service ({(int)resp.StatusCode}): {Truncate(detail, 400)}");
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var sid = doc.RootElement.TryGetProperty("sid", out var sidProp) ? sidProp.GetString() : null;
            if (string.IsNullOrEmpty(sid))
            {
                throw new TwilioProvisioningException("Twilio returned a Messaging Service without a sid.");
            }
            return sid;
        }

        private async Task AttachNumberToMessagingService(
            string subaccountSid, string subaccountToken,
            string messagingServiceSid, string phoneNumberSid, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://messaging.twilio.com/v1/Services/{messagingServiceSid}/PhoneNumbers")
            {
                Headers = { Authorization = BasicAuth(subaccountSid, subaccountToken) },
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("PhoneNumberSid", phoneNumberSid),
                }),
            };

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                throw new TwilioProvisioningException(
                    $"Couldn't attach number to Messaging Service ({(int)resp.StatusCode}): {Truncate(detail, 400)}");
            }
        }

        private async Task TryCloseSubaccount(string subaccountSid, CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post,
                    $"https://api.twilio.com/2010-04-01/Accounts/{subaccountSid}.json")
                {
                    Headers = { Authorization = BasicAuth(_masterSid!, _masterToken!) },
                    Content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("Status", "closed"),
                    }),
                };
                using var resp = await _http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Could not close orphan subaccount {Sid}: {Status}",
                        subaccountSid, (int)resp.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception while closing orphan subaccount {Sid}", subaccountSid);
            }
        }

        private void EnsureMasterConfigured()
        {
            if (!IsMasterConfigured)
            {
                throw new TwilioProvisioningException(
                    "Master Twilio credentials aren't configured. Set Sms:Twilio:AccountSid and Sms:Twilio:AuthToken.");
            }
        }

        private static AuthenticationHeaderValue BasicAuth(string user, string pass)
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
            return new AuthenticationHeaderValue("Basic", basic);
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
