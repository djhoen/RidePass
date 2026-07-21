using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Interfaces;
using webapi.Controllers.API.Data.BikeShop;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // PUBLIC signing page for a rental, reached from an emailed link. Mirrors ShopDepositController:
    // the token is the whole credential, no login, and it is scoped to one rental.
    //
    // Deliberately narrow: it can read just enough for the renter to know what they are signing,
    // and it can add signatures. It cannot move money, change the rental, reveal another rental,
    // or reveal anything about the customer beyond what they already know about themselves.
    [ApiController]
    [Route("api/[controller]")]
    public class ShopRentalSigningController : ControllerBase
    {
        private readonly IBikeShopRepository _shop;
        private readonly IWaiverRepository _waivers;
        private readonly ITenantContext _tenantContext;

        public ShopRentalSigningController(IBikeShopRepository shop, IWaiverRepository waivers,
            ITenantContext tenantContext)
        {
            _shop = shop;
            _waivers = waivers;
            _tenantContext = tenantContext;
        }

        [HttpGet("{token:guid}")]
        public async Task<IActionResult> Get(Guid token)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.BikeShopEnabled) return new ApiResponses().NotFoundResult("Not found.");

            var rental = await _shop.GetRentalBySignatureToken(token, _tenantContext.TenantId);
            if (rental is null) return new ApiResponses().NotFoundResult("This signing link isn't valid.");

            var agreement = await _shop.GetActiveAgreement(_tenantContext.TenantId, "rental_agreement");
            var waiver = await _waivers.GetActive(_tenantContext.TenantId);

            var agreementSigned = agreement is not null
                && await _shop.HasCurrentAgreementSignature(rental.Id, _tenantContext.TenantId, "rental_agreement");
            var waiverSigned = await IsWaiverSigned(rental, waiver?.Id);

            return new ApiResponses().OkResult(new
            {
                renterName = rental.RenterName,
                startsAt = DateTime.SpecifyKind(rental.StartsAt, DateTimeKind.Utc),
                endsAt = DateTime.SpecifyKind(rental.EndsAt, DateTimeKind.Utc),
                depositCents = rental.DepositCents,
                // Already handed over or finished: signing now would be meaningless.
                closed = rental.Status is "out" or "returned" or "damaged" or "cancelled" or "failed",
                status = rental.Status,
                items = rental.Lines.Select(l => new
                {
                    name = l.NameSnapshot, variantLabel = l.VariantLabel, quantity = l.Quantity,
                }),
                agreement = agreement is null ? null : new { agreement.Title, agreement.Body, agreement.Version },
                agreementSigned,
                waiver = waiver is null ? null : new { waiver.Title, waiver.Body },
                waiverRequired = waiver is not null,
                waiverSigned,
            });
        }

        [HttpPost("{token:guid}/SignAgreement")]
        public async Task<IActionResult> SignAgreement(Guid token, [FromBody] PublicSignRentalRequest req)
        {
            var rental = await ResolveOpenRental(token);
            if (rental is null) return new ApiResponses().NotFoundResult("This signing link isn't valid.");
            if (rental.Status is "out" or "returned" or "damaged" or "cancelled" or "failed")
                return new ApiResponses().BadRequestResult("This rental is already closed.");

            if (string.IsNullOrWhiteSpace(req.SignerName))
                return new ApiResponses().BadRequestResult("Enter your name.");
            if (string.IsNullOrWhiteSpace(req.SignatureDataUrl))
                return new ApiResponses().BadRequestResult("A signature is required.");

            var agreement = await _shop.GetActiveAgreement(_tenantContext.TenantId, "rental_agreement");
            if (agreement is null)
                return new ApiResponses().BadRequestResult("There's no rental agreement to sign right now.");

            var id = await _shop.AddAgreementSignature(new ShopAgreementSignature
            {
                TenantId = _tenantContext.TenantId,
                AgreementId = agreement.Id,
                AgreementVersion = agreement.Version,
                RentalId = rental.Id,
                SignerName = req.SignerName.Trim(),
                SignerEmail = string.IsNullOrWhiteSpace(req.SignerEmail) ? rental.RenterEmail : req.SignerEmail.Trim(),
                SignatureDataUrl = req.SignatureDataUrl,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                // No witness: the renter signed this themselves, away from the counter.
                WitnessedByUserId = null,
            });
            return id is null
                ? new ApiResponses().NotFoundResult("This signing link isn't valid.")
                : new ApiResponses().OkResult(new { id });
        }

        [HttpPost("{token:guid}/SignWaiver")]
        public async Task<IActionResult> SignWaiver(Guid token, [FromBody] PublicSignRentalWaiverRequest req)
        {
            var rental = await ResolveOpenRental(token);
            if (rental is null) return new ApiResponses().NotFoundResult("This signing link isn't valid.");
            if (rental.Status is "out" or "returned" or "damaged" or "cancelled" or "failed")
                return new ApiResponses().BadRequestResult("This rental is already closed.");

            var waiver = await _waivers.GetActive(_tenantContext.TenantId);
            if (waiver is null) return new ApiResponses().BadRequestResult("This track has no active waiver.");

            if (string.IsNullOrWhiteSpace(req.FirstName) || string.IsNullOrWhiteSpace(req.LastName))
                return new ApiResponses().BadRequestResult("Enter the rider's first and last name.");
            if (string.IsNullOrWhiteSpace(req.SignatureDataUrl))
                return new ApiResponses().BadRequestResult("A signature is required.");
            if (req.SignedByParent && string.IsNullOrWhiteSpace(req.ParentName))
                return new ApiResponses().BadRequestResult("Enter the parent or guardian's name.");

            var signatureId = await _waivers.SignRegistrant(
                _tenantContext.TenantId, waiver.Id,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                req.SignatureDataUrl,
                signerEmail: string.IsNullOrWhiteSpace(req.Email) ? rental.RenterEmail : req.Email.Trim(),
                signerName: req.SignedByParent ? req.ParentName!.Trim() : $"{req.FirstName.Trim()} {req.LastName.Trim()}",
                attendeeFirstName: req.FirstName.Trim(),
                attendeeLastName: req.LastName.Trim(),
                attendeeBirthdate: req.Birthdate,
                signedByParent: req.SignedByParent,
                parentName: req.ParentName?.Trim(),
                parentPhone: req.ParentPhone?.Trim());

            // Append, don't replace: a rental can be for several riders, and each one signing
            // through the emailed link must add to the set rather than overwrite the last person.
            await _shop.AddRentalWaiverSignature(rental.Id, _tenantContext.TenantId, signatureId);
            var signed = await _shop.CountRentalWaiverSignatures(rental.Id, _tenantContext.TenantId);
            var required = Math.Max(1, rental.RidersRequired);
            return new ApiResponses().OkResult(new
            {
                signatureId,
                ridersSigned = signed,
                ridersRequired = required,
                ridersOutstanding = Math.Max(0, required - signed),
            });
        }

        private async Task<ShopRentalWithLines?> ResolveOpenRental(Guid token)
        {
            if (!_tenantContext.IsResolved || !_tenantContext.Tenant.BikeShopEnabled) return null;
            return await _shop.GetRentalBySignatureToken(token, _tenantContext.TenantId);
        }

        // Same three-way resolution the counter's readiness check uses: the signature stored on
        // the rental, then the renter's account, then their email for a walk-in.
        private async Task<bool> IsWaiverSigned(ShopRentalWithLines rental, Guid? waiverId)
        {
            if (waiverId is not Guid wid) return false;
            if (rental.WaiverSignatureId is Guid sigId)
                return await _waivers.GetSignatureById(sigId, _tenantContext.TenantId) is not null;
            if (rental.RenterUserId is Guid renterId)
                return await _waivers.GetSignature(renterId, wid) is not null;
            if (!string.IsNullOrWhiteSpace(rental.RenterEmail))
                return await _waivers.GetSignatureBySignerEmailForSelf(rental.RenterEmail.Trim(), wid) is not null;
            return false;
        }
    }
}
