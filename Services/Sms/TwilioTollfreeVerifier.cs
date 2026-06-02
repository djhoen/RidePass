using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Services.Helpers;
using Services.Repositories.Data.SmsData;
using Services.Repositories.Data.TenantData;

namespace Services.Sms
{
    /// <summary>
    /// Submits Toll-Free Verification requests to Twilio and polls their
    /// status. Verification removes the carrier-imposed ~10 message/day cap
    /// on unverified toll-free numbers; without it, real tenant volume
    /// silently drops at the carrier level. Twilio's TFV review typically
    /// takes 5–30 days for the carrier-side approval; ours just submits and
    /// surfaces status.
    /// </summary>
    public interface ITwilioTollfreeVerifier
    {
        /// <summary>
        /// Submit the tenant's draft to Twilio. Returns the verification SID
        /// (HH...) and initial status (typically "PENDING_REVIEW"). Looks up
        /// the tenant's toll-free PhoneNumberSid on the fly so the schema
        /// doesn't have to carry it — it's only needed during submission.
        /// Throws <see cref="TwilioVerificationException"/> with a
        /// tenant-safe error message on any failure.
        /// </summary>
        Task<TollfreeSubmitResult> Submit(
            Tenant tenant, TenantTollfreeVerification verification, CancellationToken ct = default);

        /// <summary>
        /// Fetch the current status of an existing verification. Returns
        /// null when Twilio reports the verification SID as not found
        /// (typically means the tenant was released and resubmitted under a
        /// new subaccount — caller should clear local state).
        /// </summary>
        Task<TollfreeStatusResult?> RefreshStatus(
            Tenant tenant, string verificationSid, CancellationToken ct = default);
    }

    public record TollfreeSubmitResult(string VerificationSid, string Status);
    public record TollfreeStatusResult(string Status, string? RejectionReason);

    public class TwilioVerificationException : Exception
    {
        public TwilioVerificationException(string message) : base(message) { }
    }

    public class TwilioTollfreeVerifier : ITwilioTollfreeVerifier
    {
        private static readonly HttpClient _http = new();

        private readonly ILogger<TwilioTollfreeVerifier> _logger;

        public TwilioTollfreeVerifier(ILogger<TwilioTollfreeVerifier> logger)
        {
            _logger = logger;
        }

        public async Task<TollfreeSubmitResult> Submit(
            Tenant tenant, TenantTollfreeVerification verification, CancellationToken ct = default)
        {
            var (sid, token) = ResolveCredentials(tenant);
            if (string.IsNullOrWhiteSpace(tenant.TwilioFromNumber))
            {
                throw new TwilioVerificationException("Tenant has no provisioned toll-free number to verify.");
            }

            // TFV needs the PhoneNumberSid (PN...) of the toll-free number,
            // not the E.164. We don't store the SID, so look it up from the
            // tenant's subaccount by querying the IncomingPhoneNumbers
            // resource filtered by phone number.
            var phoneNumberSid = await LookupPhoneNumberSid(sid, token, tenant.TwilioFromNumber!, ct);
            if (phoneNumberSid is null)
            {
                throw new TwilioVerificationException(
                    $"Couldn't locate {tenant.TwilioFromNumber} in this tenant's Twilio subaccount.");
            }

            // Submit. Twilio TFV form accepts repeated keys for the array
            // fields (UseCaseCategories, ProductionMessageSample,
            // OptInImageUrls); we encode each element as its own kvp.
            var body = new List<KeyValuePair<string, string>>
            {
                new("TollfreePhoneNumberSid", phoneNumberSid),
                new("BusinessName", verification.BusinessName ?? string.Empty),
                new("BusinessWebsite", verification.BusinessWebsite ?? string.Empty),
                new("BusinessStreetAddress", verification.BusinessStreetAddress ?? string.Empty),
                new("BusinessCity", verification.BusinessCity ?? string.Empty),
                new("BusinessStateProvinceRegion", verification.BusinessStateProvinceRegion ?? string.Empty),
                new("BusinessPostalCode", verification.BusinessPostalCode ?? string.Empty),
                new("BusinessCountry", verification.BusinessCountry ?? string.Empty),
                new("BusinessContactFirstName", verification.BusinessContactFirstName ?? string.Empty),
                new("BusinessContactLastName", verification.BusinessContactLastName ?? string.Empty),
                new("BusinessContactEmail", verification.BusinessContactEmail ?? string.Empty),
                new("BusinessContactPhone", verification.BusinessContactPhone ?? string.Empty),
                new("NotificationEmail", verification.NotificationEmail ?? string.Empty),
                new("UseCaseSummary", verification.UseCaseSummary ?? string.Empty),
                new("OptInType", verification.OptInType ?? string.Empty),
                new("MessageVolume", verification.MessageVolume ?? string.Empty),
            };
            if (!string.IsNullOrWhiteSpace(verification.AdditionalInformation))
            {
                body.Add(new("AdditionalInformation", verification.AdditionalInformation!));
            }
            foreach (var cat in verification.UseCaseCategories ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(cat))
                {
                    body.Add(new("UseCaseCategories", cat));
                }
            }
            foreach (var sample in verification.ProductionMessageSamples ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(sample))
                {
                    body.Add(new("ProductionMessageSample", sample));
                }
            }
            foreach (var url in verification.OptInImageUrls ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(url))
                {
                    body.Add(new("OptInImageUrls", url));
                }
            }

            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://messaging.twilio.com/v1/Tollfree/Verifications")
            {
                Headers = { Authorization = BasicAuth(sid, token) },
                Content = new FormUrlEncodedContent(body),
            };

            using var resp = await _http.SendAsync(req, ct);
            var responseBody = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                throw new TwilioVerificationException(
                    $"Twilio rejected the verification submission ({(int)resp.StatusCode}): {Truncate(responseBody, 400)}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var verificationSid = doc.RootElement.TryGetProperty("sid", out var sidProp) ? sidProp.GetString() : null;
            var status = doc.RootElement.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;

            if (string.IsNullOrEmpty(verificationSid) || string.IsNullOrEmpty(status))
            {
                throw new TwilioVerificationException("Twilio returned a verification without sid/status.");
            }

            return new TollfreeSubmitResult(verificationSid, status);
        }

        public async Task<TollfreeStatusResult?> RefreshStatus(
            Tenant tenant, string verificationSid, CancellationToken ct = default)
        {
            var (sid, token) = ResolveCredentials(tenant);

            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://messaging.twilio.com/v1/Tollfree/Verifications/{verificationSid}")
            {
                Headers = { Authorization = BasicAuth(sid, token) },
            };

            using var resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning(
                    "Tollfree verification {VerificationSid} not found for tenant {TenantId}",
                    verificationSid, tenant.Id);
                return null;
            }

            var responseBody = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                throw new TwilioVerificationException(
                    $"Twilio status check failed ({(int)resp.StatusCode}): {Truncate(responseBody, 400)}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var status = doc.RootElement.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
            string? rejectionReason = null;
            // Twilio puts the rejection reason in different fields depending
            // on the rejection source; prefer whichever is populated.
            if (doc.RootElement.TryGetProperty("rejection_reason", out var rrProp))
            {
                rejectionReason = rrProp.GetString();
            }

            if (string.IsNullOrEmpty(status))
            {
                throw new TwilioVerificationException("Twilio returned a verification without a status.");
            }

            return new TollfreeStatusResult(status, string.IsNullOrEmpty(rejectionReason) ? null : rejectionReason);
        }

        private async Task<string?> LookupPhoneNumberSid(
            string subaccountSid, string subaccountToken, string phoneNumber, CancellationToken ct)
        {
            var url = $"https://api.twilio.com/2010-04-01/Accounts/{subaccountSid}/IncomingPhoneNumbers.json"
                    + $"?PhoneNumber={Uri.EscapeDataString(phoneNumber)}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Headers = { Authorization = BasicAuth(subaccountSid, subaccountToken) },
            };

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                throw new TwilioVerificationException(
                    $"Couldn't look up phone number SID ({(int)resp.StatusCode}): {Truncate(detail, 400)}");
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("incoming_phone_numbers", out var arr)) return null;
            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("sid", out var sidProp))
                {
                    var sid = sidProp.GetString();
                    if (!string.IsNullOrEmpty(sid)) return sid;
                }
            }
            return null;
        }

        private static (string Sid, string Token) ResolveCredentials(Tenant tenant)
        {
            if (string.IsNullOrWhiteSpace(tenant.TwilioSubaccountSid)
                || string.IsNullOrWhiteSpace(tenant.TwilioAuthTokenEncrypted))
            {
                throw new TwilioVerificationException(
                    "Tenant has no provisioned Twilio subaccount. Provision a number before submitting verification.");
            }
            var token = EncryptionHelper.Decrypt(tenant.TwilioAuthTokenEncrypted);
            if (string.IsNullOrEmpty(token))
            {
                throw new TwilioVerificationException(
                    "Tenant Twilio auth token failed to decrypt — encryption key may have rotated.");
            }
            return (tenant.TwilioSubaccountSid!, token);
        }

        private static AuthenticationHeaderValue BasicAuth(string user, string pass)
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
            return new AuthenticationHeaderValue("Basic", basic);
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
