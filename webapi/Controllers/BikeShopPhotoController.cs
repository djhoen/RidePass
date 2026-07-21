using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Interfaces;
using Services.Storage;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.BikeShop;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // Condition photos for work orders and rentals: what the bike looked like when it arrived or
    // went out, and what it looked like when it came back. This is the cheap half of a repair
    // authorization (Lightspeed Retail allows 12 images per work order and captures no signature
    // at all), and for rentals it is the evidence behind any damage capture on the deposit.
    //
    // One controller for both owners because the upload, validation, cap, and delete are identical;
    // only which id you pass changes. ShopCounter throughout, same as the rest of the shop.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
    public class BikeShopPhotoController : ControllerBase
    {
        // Matches Lightspeed Retail's per-work-order allowance. Applied PER STAGE, so a rental can
        // hold 12 going out and 12 coming back.
        private const int MaxPhotosPerStage = 12;
        private const long MaxBytes = 5 * 1024 * 1024;

        private static readonly Dictionary<string, string> AllowedTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["image/png"] = ".png",
                ["image/jpeg"] = ".jpg",
                ["image/webp"] = ".webp",
            };

        private readonly IBikeShopRepository _shop;
        private readonly IImageStorage _imageStorage;
        private readonly ITenantContext _tenantContext;

        public BikeShopPhotoController(IBikeShopRepository shop, IImageStorage imageStorage,
            ITenantContext tenantContext)
        {
            _shop = shop;
            _imageStorage = imageStorage;
            _tenantContext = tenantContext;
        }

        private Guid TenantId => _tenantContext.TenantId;
        private Guid? UserId => Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;

        [HttpGet("WorkOrder/{workOrderId:guid}")]
        public async Task<IActionResult> ListForWorkOrder(Guid workOrderId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await _shop.ListConditionPhotosForWorkOrder(workOrderId, TenantId));
        }

        [HttpGet("Rental/{rentalId:guid}")]
        public async Task<IActionResult> ListForRental(Guid rentalId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await _shop.ListConditionPhotosForRental(rentalId, TenantId));
        }

        [HttpPost("WorkOrder/{workOrderId:guid}")]
        [RequestSizeLimit(MaxBytes)]
        public Task<IActionResult> UploadForWorkOrder(Guid workOrderId, IFormFile file,
            [FromQuery] string stage = "intake", [FromQuery] string? caption = null,
            CancellationToken ct = default) =>
            Upload(workOrderId, null, file, stage, caption, ct);

        [HttpPost("Rental/{rentalId:guid}")]
        [RequestSizeLimit(MaxBytes)]
        public Task<IActionResult> UploadForRental(Guid rentalId, IFormFile file,
            [FromQuery] string stage = "intake", [FromQuery] string? caption = null,
            CancellationToken ct = default) =>
            Upload(null, rentalId, file, stage, caption, ct);

        private async Task<IActionResult> Upload(Guid? workOrderId, Guid? rentalId, IFormFile file,
            string stage, string? caption, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.BikeShopEnabled)
                return new ApiResponses().BadRequestResult("The bike shop isn't turned on for this track.");

            stage = string.IsNullOrWhiteSpace(stage) ? "intake" : stage.Trim().ToLowerInvariant();
            if (stage is not ("intake" or "return" or "progress"))
                return new ApiResponses().BadRequestResult("Stage must be 'intake', 'return', or 'progress'.");
            // 'return' only means something for a rental; a work order never gets gear back.
            if (stage == "return" && rentalId is null)
                return new ApiResponses().BadRequestResult("Return photos only apply to a rental.");

            if (file is null || file.Length == 0)
                return new ApiResponses().BadRequestResult("Pick a photo to upload.");
            if (file.Length > MaxBytes)
                return new ApiResponses().BadRequestResult("That photo is over the 5 MB limit. Try a smaller one.");
            if (!AllowedTypes.TryGetValue(file.ContentType, out var ext))
                return new ApiResponses().BadRequestResult(
                    $"Photos must be PNG, JPEG, or WebP (got {file.ContentType}).");

            var already = await _shop.CountConditionPhotos(workOrderId, rentalId, stage, TenantId);
            if (already >= MaxPhotosPerStage)
                return new ApiResponses().BadRequestResult(
                    $"That's the limit of {MaxPhotosPerStage} {stage} photos. Delete one to add another.");

            // Store the file first, then the row. If the row insert finds no matching owner (wrong
            // tenant or deleted), the orphaned file is cleaned up rather than left behind.
            await using var stream = file.OpenReadStream();
            var url = await _imageStorage.SaveAsync(stream, TenantId, "shop-condition", ext, ct);

            var id = await _shop.AddConditionPhoto(new ShopConditionPhoto
            {
                TenantId = TenantId,
                WorkOrderId = workOrderId,
                RentalId = rentalId,
                Stage = stage,
                ImageUrl = url,
                Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
                UploadedByUserId = UserId,
                SortOrder = (already + 1) * 10,
            });
            if (id is null)
            {
                await SafeDeleteFile(url, ct);
                return new ApiResponses().NotFoundResult(
                    workOrderId.HasValue ? "Work order not found." : "Rental not found.");
            }

            return new ApiResponses().OkResult(new { id, imageUrl = url, stage });
        }

        // ── Signed agreements ─────────────────────────────────────────────────────────
        // Captured on the SHOP'S device with the customer present (repair authorization at
        // intake, rental agreement at pickup). Remote signing for rentals is a separate flow.

        /// <summary>The current agreement text to display before signing, plus whatever has
        /// already been signed for this record.</summary>
        [HttpGet("Agreement/{kind}")]
        public async Task<IActionResult> GetAgreement(string kind,
            [FromQuery] Guid? workOrderId = null, [FromQuery] Guid? rentalId = null)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!IsValidKind(kind)) return new ApiResponses().BadRequestResult("Unknown agreement type.");
            var agreement = await _shop.GetActiveAgreement(TenantId, kind);
            var signatures = (workOrderId.HasValue || rentalId.HasValue)
                ? await _shop.ListAgreementSignatures(workOrderId, rentalId, TenantId)
                : new List<ShopAgreementSignature>();
            return new ApiResponses().OkResult(new { agreement, signatures });
        }

        [HttpPost("Agreement/{kind}/Sign")]
        public async Task<IActionResult> SignAgreement(string kind, [FromBody] SignShopAgreementRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!IsValidKind(kind)) return new ApiResponses().BadRequestResult("Unknown agreement type.");
            if ((req.WorkOrderId.HasValue) == (req.RentalId.HasValue))
                return new ApiResponses().BadRequestResult("Sign against exactly one work order or rental.");
            if (string.IsNullOrWhiteSpace(req.SignerName))
                return new ApiResponses().BadRequestResult("Enter the name of the person signing.");
            if (string.IsNullOrWhiteSpace(req.SignatureDataUrl))
                return new ApiResponses().BadRequestResult("A signature is required.");

            var agreement = await _shop.GetActiveAgreement(TenantId, kind);
            if (agreement is null)
                return new ApiResponses().BadRequestResult(
                    "No agreement has been published yet. Add one in Bike Shop settings first.");

            var id = await _shop.AddAgreementSignature(new ShopAgreementSignature
            {
                TenantId = TenantId,
                AgreementId = agreement.Id,
                AgreementVersion = agreement.Version,
                WorkOrderId = req.WorkOrderId,
                RentalId = req.RentalId,
                SignerName = req.SignerName.Trim(),
                SignerEmail = string.IsNullOrWhiteSpace(req.SignerEmail) ? null : req.SignerEmail.Trim(),
                SignatureDataUrl = req.SignatureDataUrl,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                WitnessedByUserId = UserId,
            });
            if (id is null)
                return new ApiResponses().NotFoundResult(
                    req.WorkOrderId.HasValue ? "Work order not found." : "Rental not found.");

            return new ApiResponses().OkResult(new { id, agreementVersion = agreement.Version });
        }

        private static bool IsValidKind(string kind) =>
            kind is "rental_agreement" or "work_order_terms";

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var url = await _shop.DeleteConditionPhoto(id, TenantId);
            if (url is null) return new ApiResponses().NotFoundResult("Photo not found.");
            // The row is the record; a file left behind is untidy but harmless, so a storage
            // failure here must not fail the request the user already sees as done.
            await SafeDeleteFile(url, ct);
            return new ApiResponses().OkResult();
        }

        private async Task SafeDeleteFile(string url, CancellationToken ct)
        {
            try { await _imageStorage.DeleteAsync(url, ct); }
            catch { /* best effort: the DB row is the source of truth */ }
        }
    }
}
