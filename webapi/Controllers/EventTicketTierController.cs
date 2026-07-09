using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.EventTicketTier;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/Event/{eventId:guid}/Tiers")]
    public class EventTicketTierController : ControllerBase
    {
        private readonly IEventTicketTierRepository _tiers;
        private readonly IEventRepository _events;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantRepository _tenants;

        public EventTicketTierController(
            IEventTicketTierRepository tiers,
            IEventRepository events,
            ITenantContext tenantContext,
            ITenantRepository tenants)
        {
            _tiers = tiers;
            _events = events;
            _tenantContext = tenantContext;
            _tenants = tenants;
        }

        [HttpGet]
        public async Task<IActionResult> GetForEvent(Guid eventId)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            var rows = await _tiers.GetForEvent(eventId, _tenantContext.TenantId, activeOnly: true);

            // Standalone tiers pass through unchanged. Each price ladder collapses to its
            // ACTIVE step, augmented with capacity-remaining + next-change for buy-page copy,
            // so the buyer only ever sees (and can only add) the current price.
            var result = new List<EventTicketTierResponse>();
            foreach (var r in rows.Where(t => t.LadderGroup is null))
            {
                result.Add(ToResponse(r, sold: null));
            }

            var ladderGroups = rows.Where(t => t.LadderGroup is not null)
                                   .GroupBy(t => t.LadderGroup!)
                                   .ToList();
            if (ladderGroups.Count > 0)
            {
                var ev = await _events.GetById(eventId, _tenantContext.TenantId);
                var now = DateTime.UtcNow;
                foreach (var grp in ladderGroups)
                {
                    var steps = grp.ToList();
                    var groupSold = await _tiers.GroupSoldCount(eventId, grp.Key, _tenantContext.TenantId);
                    var state = ev is null
                        ? null
                        : Services.Pricing.PriceStepResolver.Resolve(steps, groupSold, ev.StartsAt, now);
                    if (state is null)
                    {
                        // Misconfigured (no fired step) or event missing: surface the cheapest
                        // step so the ladder isn't invisible.
                        var fallback = ToResponse(steps.OrderBy(s => s.PriceCents).First(), sold: null);
                        result.Add(fallback);
                        continue;
                    }

                    var resp = ToResponse(state.Active, sold: null);
                    resp.RemainingToCapacity = ev!.Capacity.HasValue
                        ? Math.Max(0, ev.Capacity.Value - groupSold)
                        : null;
                    if (state.Next is not null)
                    {
                        resp.NextPriceCents = state.Next.PriceCents;
                        if (state.Next.MinSold.HasValue)
                        {
                            resp.NextChangeKind = "sold";
                            resp.NextChangeSoldThreshold = state.Next.MinSold;
                        }
                        else if (state.Next.EffectiveDaysBefore.HasValue)
                        {
                            resp.NextChangeKind = "date";
                            resp.NextChangeAtUtc = DateTime.SpecifyKind(
                                ev.StartsAt.AddDays(-state.Next.EffectiveDaysBefore.Value), DateTimeKind.Utc);
                        }
                        else if (state.Next.EffectiveAtUtc.HasValue)
                        {
                            resp.NextChangeKind = "date";
                            resp.NextChangeAtUtc = DateTime.SpecifyKind(state.Next.EffectiveAtUtc.Value, DateTimeKind.Utc);
                        }
                    }
                    result.Add(resp);
                }
            }

            return new ApiResponses().OkResult(result.OrderBy(r => r.SortOrder).ThenBy(r => r.Name));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Admin")]
        public async Task<IActionResult> GetAllForAdmin(Guid eventId)
        {
            var rows = await _tiers.GetForEvent(eventId, _tenantContext.TenantId, activeOnly: false);
            var responses = new List<EventTicketTierResponse>();
            foreach (var r in rows)
            {
                var sold = await _tiers.SoldCount(r.Id);
                responses.Add(ToResponse(r, sold));
            }
            return new ApiResponses().OkResult(responses);
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost]
        public async Task<IActionResult> Create(Guid eventId, [FromBody] UpsertEventTicketTierRequest request)
        {
            var ev = await _events.GetById(eventId, _tenantContext.TenantId);
            if (ev is null)
            {
                return new ApiResponses().NotFoundResult("Event not found.");
            }

            if (!ValidateBundledCoupon(request, out var bundleErr))
                return new ApiResponses().BadRequestResult(bundleErr);

            var toggleErr = await CheckFeatureToggles(request, existing: null);
            if (toggleErr is not null)
                return new ApiResponses().BadRequestResult(toggleErr);

            NormalizeAudience(request);

            var tier = new EventTicketTier
            {
                TenantId = _tenantContext.TenantId,
                EventId = eventId,
                Kind = request.Kind,
                Audience = request.Audience,
                Required = request.Required,
                Name = request.Name,
                PriceCents = request.PriceCents,
                Inventory = request.Inventory,
                SortOrder = request.SortOrder,
                IsActive = request.IsActive,
                RiderPaidServiceChargeBps = request.RiderPaidServiceChargeBps,
                LadderGroup = string.IsNullOrWhiteSpace(request.LadderGroup) ? null : request.LadderGroup.Trim(),
                MinSold = request.MinSold,
                EffectiveDaysBefore = request.EffectiveDaysBefore,
                EffectiveAtUtc = request.EffectiveAtUtc,
                BundledCouponCount = request.BundledCouponCount,
                BundledCouponDiscountKind = request.BundledCouponDiscountKind,
                BundledCouponDiscountValue = request.BundledCouponDiscountValue,
                BundledCouponScope = request.BundledCouponScope,
                BundledCouponExpiresInDays = request.BundledCouponExpiresInDays,
            };
            tier.Id = await _tiers.Create(tier);
            return new ApiResponses().OkResult(ToResponse(tier, sold: 0));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid eventId, Guid id, [FromBody] UpsertEventTicketTierRequest request)
        {
            var existing = await _tiers.GetById(id, _tenantContext.TenantId);
            if (existing is null || existing.EventId != eventId)
            {
                return new ApiResponses().NotFoundResult("Tier not found.");
            }

            if (!ValidateBundledCoupon(request, out var bundleErr))
                return new ApiResponses().BadRequestResult(bundleErr);

            var toggleErr = await CheckFeatureToggles(request, existing);
            if (toggleErr is not null)
                return new ApiResponses().BadRequestResult(toggleErr);

            NormalizeAudience(request);

            existing.Kind = request.Kind;
            existing.Audience = request.Audience;
            existing.Required = request.Required;
            existing.Name = request.Name;
            existing.PriceCents = request.PriceCents;
            existing.Inventory = request.Inventory;
            existing.SortOrder = request.SortOrder;
            existing.IsActive = request.IsActive;
            existing.RiderPaidServiceChargeBps = request.RiderPaidServiceChargeBps;
            existing.LadderGroup = string.IsNullOrWhiteSpace(request.LadderGroup) ? null : request.LadderGroup.Trim();
            existing.MinSold = request.MinSold;
            existing.EffectiveDaysBefore = request.EffectiveDaysBefore;
            existing.EffectiveAtUtc = request.EffectiveAtUtc;
            existing.BundledCouponCount = request.BundledCouponCount;
            existing.BundledCouponDiscountKind = request.BundledCouponDiscountKind;
            existing.BundledCouponDiscountValue = request.BundledCouponDiscountValue;
            existing.BundledCouponScope = request.BundledCouponScope;
            existing.BundledCouponExpiresInDays = request.BundledCouponExpiresInDays;

            await _tiers.Update(existing);
            var sold = await _tiers.SoldCount(id);
            return new ApiResponses().OkResult(ToResponse(existing, sold));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Reorder")]
        public async Task<IActionResult> Reorder(Guid eventId, [FromBody] ReorderEventTicketTiersRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var ev = await _events.GetById(eventId, _tenantContext.TenantId);
            if (ev is null) return new ApiResponses().NotFoundResult("Event not found.");
            if (req.Items.Count == 0) return new ApiResponses().OkResult();
            var ids = req.Items.Select(i => i.Id).ToList();
            var orders = req.Items.Select(i => i.SortOrder).ToList();
            await _tiers.UpdateSortOrders(_tenantContext.TenantId, eventId, ids, orders);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid eventId, Guid id)
        {
            var existing = await _tiers.GetById(id, _tenantContext.TenantId);
            if (existing is null || existing.EventId != eventId)
            {
                return new ApiResponses().NotFoundResult("Tier not found.");
            }

            var sold = await _tiers.SoldCount(id);
            if (sold > 0)
            {
                return new ApiResponses().BadRequestResult("This tier has purchases and cannot be deleted. Set inactive instead.");
            }

            await _tiers.Delete(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        private static EventTicketTierResponse ToResponse(EventTicketTier t, int? sold) => new()
        {
            Id = t.Id,
            EventId = t.EventId,
            Kind = t.Kind,
            Audience = t.Audience,
            Required = t.Required,
            Name = t.Name,
            PriceCents = t.PriceCents,
            Inventory = t.Inventory,
            Sold = sold,
            SortOrder = t.SortOrder,
            IsActive = t.IsActive,
            RiderPaidServiceChargeBps = t.RiderPaidServiceChargeBps,
            LadderGroup = t.LadderGroup,
            MinSold = t.MinSold,
            EffectiveDaysBefore = t.EffectiveDaysBefore,
            EffectiveAtUtc = t.EffectiveAtUtc,
            BundledCouponCount = t.BundledCouponCount,
            BundledCouponDiscountKind = t.BundledCouponDiscountKind,
            BundledCouponDiscountValue = t.BundledCouponDiscountValue,
            BundledCouponScope = t.BundledCouponScope,
            BundledCouponExpiresInDays = t.BundledCouponExpiresInDays,
        };

        // Race classes are always rider-audience and never themselves "required" (the
        // gate fee carries the required-purchase rule, not the class). Force those so a
        // stray client payload can't store a nonsensical combination.
        private static void NormalizeAudience(UpsertEventTicketTierRequest r)
        {
            if (r.Kind == "race_entry")
            {
                r.Audience = "rider";
                r.Required = false;
            }
        }

        private static bool ConfiguresPriceSteps(UpsertEventTicketTierRequest r) =>
            !string.IsNullOrWhiteSpace(r.LadderGroup) || r.MinSold.HasValue
            || r.EffectiveDaysBefore.HasValue || r.EffectiveAtUtc.HasValue;

        // Dynamic pricing and bundled coupons are per-tenant feature toggles (super-admin
        // controlled, default off). Creating config requires the toggle; updates are only
        // blocked when they ADD config the tier didn't already have, so a tenant whose
        // toggle was later turned off can still edit or clear pre-existing config.
        private async Task<string?> CheckFeatureToggles(UpsertEventTicketTierRequest request, EventTicketTier? existing)
        {
            var tenant = await _tenants.GetById(_tenantContext.TenantId);

            if (ConfiguresPriceSteps(request)
                && tenant?.DynamicPricingEnabled != true
                && (existing is null || existing.LadderGroup is null))
            {
                return "Dynamic pricing is not enabled for this track. Contact RidePass support to enable stepped price ladders.";
            }

            if (request.BundledCouponCount is > 0
                && tenant?.BundledCouponsEnabled != true
                && (existing is null || existing.BundledCouponCount is null or <= 0))
            {
                return "Bundled coupons are not enabled for this track. Contact RidePass support to enable them.";
            }

            return null;
        }

        // Bundled-coupon fields are all-or-nothing: when count is set, kind/value/scope must
        // also be set so we can mint the codes correctly. expires_in_days is optional.
        private static bool ValidateBundledCoupon(UpsertEventTicketTierRequest r, out string err)
        {
            if (r.BundledCouponCount is null or 0)
            {
                err = string.Empty;
                return true;
            }
            if (r.Kind != "race_entry")
            {
                err = "Bundled coupons can only be configured on race-entry tiers.";
                return false;
            }
            if (string.IsNullOrEmpty(r.BundledCouponDiscountKind) || r.BundledCouponDiscountValue is null
                || string.IsNullOrEmpty(r.BundledCouponScope))
            {
                err = "Bundled coupon discount kind, value, and scope are required when count > 0.";
                return false;
            }
            if (r.BundledCouponDiscountKind == "percent" && r.BundledCouponDiscountValue > 10000)
            {
                err = "Percent discount can't exceed 10000 bps (100%).";
                return false;
            }
            err = string.Empty;
            return true;
        }
    }
}
