using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Audit;
using Services.Helpers;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.SmsData;
using Services.Repositories.Interfaces;
using Services.Sms;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.SmsSettings;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Manages the Toll-Free Verification submission for the current tenant.
    /// Save-draft (PUT) is independent of Submit (POST /Submit) so an admin
    /// can fill in the form over multiple sittings without accidentally
    /// triggering a submission to Twilio. Submit/RefreshStatus call out to
    /// Twilio's TFV API; everything else is local DB.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
    public class TollfreeVerificationController : ControllerBase
    {
        private readonly ITenantContext _tenantContext;
        private readonly ITenantRepository _tenants;
        private readonly ITenantTollfreeVerificationRepository _verifications;
        private readonly ITwilioTollfreeVerifier _verifier;
        private readonly IAuditLogger _audit;
        private readonly ILogger<TollfreeVerificationController> _logger;

        public TollfreeVerificationController(
            ITenantContext tenantContext,
            ITenantRepository tenants,
            ITenantTollfreeVerificationRepository verifications,
            ITwilioTollfreeVerifier verifier,
            IAuditLogger audit,
            ILogger<TollfreeVerificationController> logger)
        {
            _tenantContext = tenantContext;
            _tenants = tenants;
            _verifications = verifications;
            _verifier = verifier;
            _audit = audit;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var row = await _verifications.Get(_tenantContext.TenantId);
            if (row is null)
            {
                // No row yet — return an empty DTO so the UI can render the
                // form in its pre-fill-from-tenant state without a special
                // "404 vs empty" branch.
                return new ApiResponses().OkResult(new TollfreeVerificationDto());
            }

            return new ApiResponses().OkResult(ToDto(row));
        }

        [HttpPut]
        public async Task<IActionResult> Save([FromBody] SaveTollfreeVerificationRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req is null) return new ApiResponses().BadRequestResult("Body required.");

            var v = new TenantTollfreeVerification
            {
                TenantId = _tenantContext.TenantId,
                BusinessName = req.BusinessName,
                BusinessWebsite = req.BusinessWebsite,
                BusinessStreetAddress = req.BusinessStreetAddress,
                BusinessCity = req.BusinessCity,
                BusinessStateProvinceRegion = req.BusinessStateProvinceRegion,
                BusinessPostalCode = req.BusinessPostalCode,
                BusinessCountry = req.BusinessCountry,
                BusinessContactFirstName = req.BusinessContactFirstName,
                BusinessContactLastName = req.BusinessContactLastName,
                BusinessContactEmail = req.BusinessContactEmail,
                BusinessContactPhone = req.BusinessContactPhone,
                NotificationEmail = req.NotificationEmail,
                UseCaseCategories = req.UseCaseCategories,
                UseCaseSummary = req.UseCaseSummary,
                ProductionMessageSamples = req.ProductionMessageSamples,
                OptInType = req.OptInType,
                OptInImageUrls = req.OptInImageUrls,
                MessageVolume = req.MessageVolume,
                AdditionalInformation = req.AdditionalInformation,
            };
            await _verifications.Upsert(v);

            var refreshed = await _verifications.Get(_tenantContext.TenantId);
            return new ApiResponses().OkResult(ToDto(refreshed!));
        }

        [HttpPost("Submit")]
        public async Task<IActionResult> Submit(CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var row = await _verifications.Get(_tenantContext.TenantId);
            if (row is null)
            {
                return new ApiResponses().BadRequestResult("Fill out the verification form before submitting.");
            }

            var missing = RequiredFieldsMissing(row);
            if (missing.Count > 0)
            {
                return new ApiResponses().BadRequestResult(
                    "Missing required fields: " + string.Join(", ", missing));
            }

            // Re-fetch tenant fresh — _tenantContext.Tenant is the snapshot
            // from request start, and another tab may have released the
            // number between then and now.
            var tenant = await _tenants.GetById(_tenantContext.TenantId);
            if (tenant is null)
            {
                return new ApiResponses().BadRequestResult("Tenant not found.");
            }

            try
            {
                var result = await _verifier.Submit(tenant, row, ct);
                await _verifications.SetSubmitted(_tenantContext.TenantId, result.VerificationSid, result.Status);

                await _audit.Log("sms.tollfree_verification.submit",
                    $"Submitted toll-free verification (status {result.Status})",
                    targetKind: "tenant", targetId: _tenantContext.TenantId,
                    metadata: new { verificationSid = result.VerificationSid });

                var refreshed = await _verifications.Get(_tenantContext.TenantId);
                return new ApiResponses().OkResult(ToDto(refreshed!));
            }
            catch (TwilioVerificationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [HttpPost("RefreshStatus")]
        public async Task<IActionResult> RefreshStatus(CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var row = await _verifications.Get(_tenantContext.TenantId);
            if (row is null || string.IsNullOrWhiteSpace(row.TwilioVerificationSid))
            {
                return new ApiResponses().BadRequestResult("No verification has been submitted yet.");
            }

            var tenant = await _tenants.GetById(_tenantContext.TenantId);
            if (tenant is null)
            {
                return new ApiResponses().BadRequestResult("Tenant not found.");
            }

            try
            {
                var result = await _verifier.RefreshStatus(tenant, row.TwilioVerificationSid!, ct);
                if (result is null)
                {
                    return new ApiResponses().BadRequestResult(
                        "Twilio no longer knows about this verification — it was likely cleared when the number was released.");
                }
                await _verifications.SetStatus(_tenantContext.TenantId, result.Status, result.RejectionReason);

                var refreshed = await _verifications.Get(_tenantContext.TenantId);
                return new ApiResponses().OkResult(ToDto(refreshed!));
            }
            catch (TwilioVerificationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        private static TollfreeVerificationDto ToDto(TenantTollfreeVerification v) => new()
        {
            BusinessName = v.BusinessName,
            BusinessWebsite = v.BusinessWebsite,
            BusinessStreetAddress = v.BusinessStreetAddress,
            BusinessCity = v.BusinessCity,
            BusinessStateProvinceRegion = v.BusinessStateProvinceRegion,
            BusinessPostalCode = v.BusinessPostalCode,
            BusinessCountry = v.BusinessCountry,
            BusinessContactFirstName = v.BusinessContactFirstName,
            BusinessContactLastName = v.BusinessContactLastName,
            BusinessContactEmail = v.BusinessContactEmail,
            BusinessContactPhone = v.BusinessContactPhone,
            NotificationEmail = v.NotificationEmail,
            UseCaseCategories = v.UseCaseCategories,
            UseCaseSummary = v.UseCaseSummary,
            ProductionMessageSamples = v.ProductionMessageSamples,
            OptInType = v.OptInType,
            OptInImageUrls = v.OptInImageUrls,
            MessageVolume = v.MessageVolume,
            AdditionalInformation = v.AdditionalInformation,
            Status = v.Status,
            RejectionReason = v.RejectionReason,
            LastSubmittedAtUtc = v.LastSubmittedAtUtc,
            LastStatusCheckedAtUtc = v.LastStatusCheckedAtUtc,
        };

        private static List<string> RequiredFieldsMissing(TenantTollfreeVerification v)
        {
            // Mirrors Twilio's required-field set. We check here instead of
            // relying on Twilio's 400 response so the admin gets one
            // consolidated error message instead of one field at a time.
            var missing = new List<string>();
            void need(string? value, string label) { if (string.IsNullOrWhiteSpace(value)) missing.Add(label); }
            void needArray(string[]? arr, string label) { if (arr is null || arr.All(string.IsNullOrWhiteSpace)) missing.Add(label); }

            need(v.BusinessName, "Business name");
            need(v.BusinessWebsite, "Business website");
            need(v.BusinessStreetAddress, "Business address");
            need(v.BusinessCity, "Business city");
            need(v.BusinessStateProvinceRegion, "Business state/region");
            need(v.BusinessPostalCode, "Business postal code");
            need(v.BusinessCountry, "Business country");
            need(v.BusinessContactFirstName, "Contact first name");
            need(v.BusinessContactLastName, "Contact last name");
            need(v.BusinessContactEmail, "Contact email");
            need(v.BusinessContactPhone, "Contact phone");
            need(v.NotificationEmail, "Notification email");
            needArray(v.UseCaseCategories, "Use case categories");
            need(v.UseCaseSummary, "Use case summary");
            needArray(v.ProductionMessageSamples, "Sample messages");
            need(v.OptInType, "Opt-in type");
            needArray(v.OptInImageUrls, "Opt-in image URL");
            need(v.MessageVolume, "Message volume");

            return missing;
        }
    }
}
