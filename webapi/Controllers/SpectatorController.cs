using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Data.ExtrasData;
using Services.Repositories.Interfaces;
using webapi.Controllers.API.Data.Spectator;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Public spectator buy flow — guest checkout for Gate Fees and other
    /// spectator-audience extras at a race event. Captures one waiver signature
    /// per attending spectator (purchaser signs for themselves; parent signs
    /// again with each child's name on the row).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SpectatorController : ControllerBase
    {
        private readonly IEventRepository _events;
        private readonly IEventExtraRepository _extras;
        private readonly IWaiverRepository _waivers;
        private readonly IPaymentProvider _payments;
        private readonly ITenantContext _tenantContext;

        public SpectatorController(
            IEventRepository events,
            IEventExtraRepository extras,
            IWaiverRepository waivers,
            IPaymentProvider payments,
            ITenantContext tenantContext)
        {
            _events = events;
            _extras = extras;
            _waivers = waivers;
            _payments = payments;
            _tenantContext = tenantContext;
        }

        // Email-based lookup. Returns true when this email signed the waiver for
        // THEMSELVES (no child / spectator name on the row). The buyer still needs
        // to sign separately for each minor they're bringing.
        [HttpGet("Waiver/{waiverId:guid}/Check")]
        public async Task<IActionResult> CheckSignature(Guid waiverId, [FromQuery] string email)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (string.IsNullOrWhiteSpace(email)) return new ApiResponses().BadRequestResult("Email required.");
            var waiver = await _waivers.GetById(waiverId, _tenantContext.TenantId);
            if (waiver is null) return new ApiResponses().NotFoundResult("Waiver not found.");
            var sig = await _waivers.GetSignatureBySignerEmailForSelf(email.Trim(), waiverId);
            return new ApiResponses().OkResult(new CheckSignatureResponse { HasSigned = sig is not null });
        }

        [HttpPost("Buy")]
        public async Task<IActionResult> Buy([FromBody] SpectatorBuyRequest request, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenant = _tenantContext.Tenant;
            if (!tenant.ExtrasEnabled) return new ApiResponses().BadRequestResult("Spectator passes aren't sold at this track.");

            var ev = await _events.GetById(request.EventId, _tenantContext.TenantId);
            if (ev is null || ev.Status != "scheduled" || ev.EndsAt < DateTime.UtcNow)
            {
                return new ApiResponses().BadRequestResult("That event has already ended or is no longer available.");
            }

            // Resolve the spectator waiver: explicit pin > tenant default. Only
            // consulted when the event requires a spectator waiver.
            Services.Repositories.Data.PaymentData.TenantWaiver? waiver = null;
            if (ev.RequiresSpectatorWaiver)
            {
                if (ev.SpectatorWaiverId.HasValue)
                    waiver = await _waivers.GetById(ev.SpectatorWaiverId.Value, _tenantContext.TenantId);
                waiver ??= await _waivers.GetActive(_tenantContext.TenantId);
            }

            // Cart validation: dedupe by (productId, variantId), require gate-fee /
            // extras products, compute totals.
            var items = (request.Items ?? new()).Where(i => i.Quantity > 0).ToList();
            if (items.Count == 0) return new ApiResponses().BadRequestResult("Cart is empty.");

            var lines = new List<(EventExtraProduct Product, EventExtraVariant? Variant, int UnitAmount, int UnitServiceCharge, int UnitPriceFrozen, int Quantity)>();
            int totalCents = 0;
            // Spectator-attendee count tracks Gate Fee units only. Other add-ons
            // riding along on the same purchase (camping, parking, merch) don't
            // require a per-attendee waiver signature.
            int gateFeeUnits = 0;
            foreach (var item in items)
            {
                var product = await _extras.GetProduct(item.ProductId, _tenantContext.TenantId);
                if (product is null || !product.IsActive)
                {
                    return new ApiResponses().BadRequestResult("One of the selected items isn't available.");
                }
                if (product.ExpiresAt.HasValue && product.ExpiresAt.Value <= DateTime.UtcNow)
                {
                    return new ApiResponses().BadRequestResult($"\"{product.Name}\" is no longer being sold.");
                }
                var elig = await _extras.GetEligibility(ev.Id, product.Id);
                if (elig is null)
                {
                    return new ApiResponses().BadRequestResult($"\"{product.Name}\" isn't offered at this event.");
                }
                EventExtraVariant? variant = null;
                int unitPriceFrozen = product.PriceCents;
                var variants = await _extras.ListVariants(product.Id);
                var activeVariants = variants.Where(v => v.IsActive).ToList();
                if (activeVariants.Count > 0)
                {
                    if (!item.VariantId.HasValue)
                        return new ApiResponses().BadRequestResult($"Pick an option for \"{product.Name}\".");
                    variant = activeVariants.FirstOrDefault(v => v.Id == item.VariantId.Value);
                    if (variant is null)
                        return new ApiResponses().BadRequestResult($"That option isn't available for \"{product.Name}\".");
                    unitPriceFrozen = variant.PriceCents ?? product.PriceCents;
                }
                var (unitAmount, unitServiceCharge) = ComputeWithServiceCharge(
                    unitPriceFrozen, 1, tenant.ServiceChargeBps, product.RiderPaidServiceChargeBps);
                lines.Add((product, variant, unitAmount, unitServiceCharge, unitPriceFrozen, item.Quantity));
                totalCents += unitAmount * item.Quantity;
                if (IsGateFee(product))
                {
                    gateFeeUnits += item.Quantity;
                }
            }

            // Spectator buy is a Gate Fee purchase by definition — every order needs
            // at least one. Other items can ride along but can't be the entire cart.
            if (gateFeeUnits == 0)
            {
                return new ApiResponses().BadRequestResult("Add at least one Gate Fee to your spectator order.");
            }

            // Spectator info: when the event has a spectator waiver, every Gate Fee
            // unit must map to a spectator entry. Non-gate-fee items (camping etc.)
            // ride along without per-attendee signatures.
            var spectators = request.Spectators ?? new();
            if (waiver is not null && spectators.Count != gateFeeUnits)
            {
                return new ApiResponses().BadRequestResult(
                    "Each Gate Fee needs an attendee on the waiver. " +
                    $"You have {gateFeeUnits} Gate Fee pass(es) but provided {spectators.Count} attendee(s).");
            }

            // Validate each spectator entry + decide whether a signature must be
            // collected on this row (purchaser-for-self may already have signed;
            // children always require a fresh signature).
            var signatureCreates = new List<(SpectatorEntry Entry, bool NeedsSign)>();
            if (waiver is not null)
            {
                foreach (var s in spectators)
                {
                    if (string.IsNullOrWhiteSpace(s.FirstName) || string.IsNullOrWhiteSpace(s.LastName))
                        return new ApiResponses().BadRequestResult("Each spectator needs a name.");
                    if (s.Birthdate.Date >= DateTime.UtcNow.Date)
                        return new ApiResponses().BadRequestResult("Spectator birthdate must be in the past.");

                    var isMinor = WaiverPolicy.IsMinor(s.Birthdate);
                    bool isSelf = NameMatches(s, request.PurchaserName);
                    bool needsSign = true;

                    // The purchaser signing for themselves can be skipped if they already
                    // have a signature on file for this waiver (self-signed, no child).
                    if (!isMinor && isSelf)
                    {
                        var existing = await _waivers.GetSignatureBySignerEmailForSelf(request.PurchaserEmail, waiver.Id);
                        needsSign = existing is null;
                    }

                    if (needsSign)
                    {
                        if (!IsValidPngDataUrl(s.SignatureDataUrl))
                            return new ApiResponses().BadRequestResult($"Signature is required for {s.FirstName} {s.LastName}.");
                        if (isMinor)
                        {
                            if (string.IsNullOrWhiteSpace(s.ParentName))
                                return new ApiResponses().BadRequestResult($"Parent / guardian name is required for {s.FirstName} {s.LastName}.");
                            if ((s.ParentPhone?.Replace("-", "").Replace("(", "").Replace(")", "").Replace(" ", "").Length ?? 0) < 7)
                                return new ApiResponses().BadRequestResult($"A valid parent / guardian phone is required for {s.FirstName} {s.LastName}.");
                        }
                    }
                    signatureCreates.Add((s, needsSign));
                }
            }

            // Persist signatures first so we can stamp their ids on the purchase rows.
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var signatureIds = new List<Guid?>();
            if (waiver is not null)
            {
                foreach (var (entry, needsSign) in signatureCreates)
                {
                    if (!needsSign) { signatureIds.Add(null); continue; }
                    var isMinor = WaiverPolicy.IsMinor(entry.Birthdate);
                    var sigId = await _waivers.SignSpectator(
                        tenantId: tenant.Id,
                        waiverId: waiver.Id,
                        ipAddress: ip,
                        signatureDataUrl: entry.SignatureDataUrl!,
                        signerEmail: request.PurchaserEmail.Trim(),
                        signerName: request.PurchaserName.Trim(),
                        spectatorFirstName: entry.FirstName.Trim(),
                        spectatorLastName: entry.LastName.Trim(),
                        spectatorBirthdate: entry.Birthdate.Date,
                        signedByParent: isMinor,
                        parentName: isMinor ? entry.ParentName?.Trim() : null,
                        parentPhone: isMinor ? entry.ParentPhone?.Trim() : null);
                    signatureIds.Add(sigId);
                }
            }

            // Create one purchase row per unit so each spectator gets their own QR.
            // Signature ids are consumed in spectator-entry order, but ONLY for
            // Gate Fee rows — non-gate-fee add-ons riding along (camping etc.)
            // get a null waiver_signature_id since they don't represent attendees.
            var purchaseIds = new List<Guid>();
            int unitIdx = 0;
            foreach (var (product, variant, unitAmount, unitServiceCharge, unitPriceFrozen, quantity) in lines)
            {
                var isGateFee = IsGateFee(product);
                for (int i = 0; i < quantity; i++)
                {
                    Guid? sigId = (isGateFee && waiver is not null && unitIdx < signatureIds.Count)
                        ? signatureIds[unitIdx]
                        : null;
                    var ep = new EventExtraPurchase
                    {
                        TenantId = tenant.Id,
                        EventId = ev.Id,
                        ProductId = product.Id,
                        PurchaserUserId = null,                       // guest checkout
                        PurchaserEmail = request.PurchaserEmail.Trim(),
                        PurchaserName = request.PurchaserName.Trim(),
                        WaiverSignatureId = sigId,
                        Quantity = 1,
                        UnitPriceCentsFrozen = unitPriceFrozen,
                        AmountCents = unitAmount,
                        ServiceChargeCents = unitServiceCharge,
                        Status = "pending",
                        PaymentMethod = "stripe",
                        VariantId = variant?.Id,
                        SizeAtPurchase = variant?.Size,
                        ColorAtPurchase = variant?.Color,
                        GenderAtPurchase = variant?.Gender,
                    };
                    var created = await _extras.CreatePurchase(ep);
                    purchaseIds.Add(created.Id);
                    if (isGateFee) unitIdx++;
                }
            }

            // Single PaymentIntent for the whole spectator order.
            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = tenant.Id.ToString(),
                ["event_id"] = ev.Id.ToString(),
                ["sale_kind"] = "spectator",
                ["extra_purchase_ids"] = string.Join(",", purchaseIds),
                ["spectator_count"] = gateFeeUnits.ToString(),
            };

            PaymentIntentCreated intent;
            try
            {
                intent = await _payments.CreatePaymentIntentAsync(
                    amountCents: totalCents,
                    currency: "usd",
                    metadata: metadata,
                    receiptEmail: request.PurchaserEmail,
                    ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            foreach (var pid in purchaseIds)
            {
                await _extras.SetPaymentIntentId(pid, intent.IntentId);
            }

            return new ApiResponses().OkResult(new SpectatorBuyResponse
            {
                PurchaseIds = purchaseIds,
                ClientSecret = intent.ClientSecret,
                AmountCents = totalCents,
            });
        }

        // Gate Fee identification — matches the seeded slug OR a product literally
        // named "Gate Fee" so renamed / hand-edited catalogs still work.
        private static bool IsGateFee(EventExtraProduct p)
        {
            if (string.Equals(p.Kind, "gate_fee", StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(p.Name?.Trim(), "Gate Fee", StringComparison.OrdinalIgnoreCase);
        }

        // Heuristic: "is this spectator the purchaser themselves?" — used to skip
        // re-signing when an adult buys their own gate fee. Match on purchaser-name
        // case/whitespace-tolerant against firstname+lastname.
        private static bool NameMatches(SpectatorEntry s, string purchaserName)
        {
            var combined = $"{s.FirstName} {s.LastName}".Trim();
            return string.Equals(combined, purchaserName.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidPngDataUrl(string? dataUrl)
        {
            if (string.IsNullOrWhiteSpace(dataUrl)) return false;
            if (!dataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal)) return false;
            return dataUrl.Length is > 800 and < 1_400_000;
        }

        private static (int amountCents, int serviceChargeCents) ComputeWithServiceCharge(
            int unitPriceCents, int quantity, int tenantServiceChargeBps, int riderPaidBps)
        {
            var serviceChargePerUnit = (int)((long)unitPriceCents * tenantServiceChargeBps / 10_000L);
            var riderPortionPerUnit = (int)((long)serviceChargePerUnit * riderPaidBps / 10_000L);
            var amount = (unitPriceCents + riderPortionPerUnit) * quantity;
            var serviceCharge = serviceChargePerUnit * quantity;
            return (amount, serviceCharge);
        }
    }
}
