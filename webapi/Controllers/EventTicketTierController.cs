using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.EventData;
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
        private readonly IInstructorRepository _instructors;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantRepository _tenants;

        public EventTicketTierController(
            IEventTicketTierRepository tiers,
            IEventRepository events,
            IInstructorRepository instructors,
            ITenantContext tenantContext,
            ITenantRepository tenants)
        {
            _tiers = tiers;
            _events = events;
            _instructors = instructors;
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

            // Coaches referenced by this event's groups, loaded once (not per tier) so the buy
            // page can show who is teaching each group.
            var coaches = await LoadCoachesFor(rows);

            // Event-wide rider capacity remaining, which is what checkout actually enforces
            // (PurchaseController / CounterController use EventSoldCount). Displayed on every
            // RIDER tier, ladder step or not, so "N spots left" can never promise more than the
            // buy path will accept. Spectator tiers don't consume rider capacity, so they are
            // left alone. Loaded once here rather than per tier.
            var eventForCapacity = await _events.GetById(eventId, _tenantContext.TenantId);
            int? riderCapacityRemaining = null;
            if (eventForCapacity?.Capacity is int cap)
            {
                var riderSold = await _tiers.EventSoldCount(eventId, _tenantContext.TenantId);
                riderCapacityRemaining = Math.Max(0, cap - riderSold);
            }

            // Standalone tiers pass through unchanged. Each price ladder collapses to its
            // ACTIVE step, augmented with capacity-remaining + next-change for buy-page copy,
            // so the buyer only ever sees (and can only add) the current price.
            var result = new List<EventTicketTierResponse>();
            foreach (var r in rows.Where(t => t.LadderGroup is null))
            {
                var standalone = ToResponse(r, sold: null, coaches);
                if (r.Audience == "rider") standalone.RemainingToCapacity = riderCapacityRemaining;
                result.Add(standalone);
            }

            var ladderGroups = rows.Where(t => t.LadderGroup is not null)
                                   .GroupBy(t => t.LadderGroup!)
                                   .ToList();
            if (ladderGroups.Count > 0)
            {
                var ev = eventForCapacity;
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
                        var fallback = ToResponse(steps.OrderBy(s => s.PriceCents).First(), sold: null, coaches);
                        result.Add(fallback);
                        continue;
                    }

                    var resp = ToResponse(state.Active, sold: null, coaches);
                    // Event-wide, not group-scoped: a rider sale on any other tier eats into the
                    // same capacity, and checkout enforces the event-wide number.
                    resp.RemainingToCapacity = state.Active.Audience == "rider" ? riderCapacityRemaining : null;
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
            var adminCoaches = await LoadCoachesFor(rows);
            var responses = new List<EventTicketTierResponse>();
            foreach (var r in rows)
            {
                var sold = await _tiers.SoldCount(r.Id);
                responses.Add(ToResponse(r, sold, adminCoaches));
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

            var groupErr = await ValidateGroup(request, ev);
            if (groupErr is not null) return new ApiResponses().BadRequestResult(groupErr);

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
                InstructorId = request.InstructorId,
                SkillLevel = Trimmed(request.SkillLevel),
                EquipmentLabel = Trimmed(request.EquipmentLabel),
                StartsAt = request.StartsAt?.ToUniversalTime(),
                EndsAt = request.EndsAt?.ToUniversalTime(),
                PartySizeIncluded = Math.Max(1, request.PartySizeIncluded),
                PartyPriceCents = request.PartyPriceCents,
                PartySizeMax = request.PartySizeMax,
            };
            tier.Id = await _tiers.Create(tier);
            return new ApiResponses().OkResult(await ToResponseAsync(tier, sold: 0));
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

            var evForGroup = await _events.GetById(eventId, _tenantContext.TenantId);
            if (evForGroup is null) return new ApiResponses().NotFoundResult("Event not found.");
            var groupErr = await ValidateGroup(request, evForGroup);
            if (groupErr is not null) return new ApiResponses().BadRequestResult(groupErr);

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
            existing.InstructorId = request.InstructorId;
            existing.SkillLevel = Trimmed(request.SkillLevel);
            existing.EquipmentLabel = Trimmed(request.EquipmentLabel);
            existing.StartsAt = request.StartsAt?.ToUniversalTime();
            existing.EndsAt = request.EndsAt?.ToUniversalTime();
            existing.PartySizeIncluded = Math.Max(1, request.PartySizeIncluded);
            existing.PartyPriceCents = request.PartyPriceCents;
            existing.PartySizeMax = request.PartySizeMax;

            await _tiers.Update(existing);
            var sold = await _tiers.SoldCount(id);
            return new ApiResponses().OkResult(await ToResponseAsync(existing, sold));
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

        private static EventTicketTierResponse ToResponse(
            EventTicketTier t, int? sold,
            IReadOnlyDictionary<Guid, Services.Repositories.Data.InstructorData.Instructor>? coaches = null)
        {
            var r = ToResponseCore(t, sold);
            if (t.InstructorId is Guid cid && coaches is not null && coaches.TryGetValue(cid, out var coach))
            {
                r.InstructorName = coach.Name;
                r.InstructorImageUrl = coach.ImageUrl;
            }
            return r;
        }

        private static EventTicketTierResponse ToResponseCore(EventTicketTier t, int? sold) => new()
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
            InstructorId = t.InstructorId,
            SkillLevel = t.SkillLevel,
            EquipmentLabel = t.EquipmentLabel,
            StartsAt = t.StartsAt,
            EndsAt = t.EndsAt,
            PartySizeIncluded = t.PartySizeIncluded,
            PartyPriceCents = t.PartyPriceCents,
            PartySizeMax = t.PartySizeMax,
        };

        // Same projection plus the coach's display fields, resolved from the instructor row so
        // the buy page and admin editor can render "who is teaching" without a second call.
        private async Task<EventTicketTierResponse> ToResponseAsync(EventTicketTier t, int? sold)
        {
            var resp = ToResponseCore(t, sold);
            if (t.InstructorId is Guid coachId)
            {
                var coach = await _instructors.Get(coachId, _tenantContext.TenantId);
                resp.InstructorName = coach?.Name;
                resp.InstructorImageUrl = coach?.ImageUrl;
            }
            return resp;
        }

        private static string? Trimmed(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

        // One query for every coach referenced by a set of tiers; empty when none are groups.
        private async Task<Dictionary<Guid, Services.Repositories.Data.InstructorData.Instructor>> LoadCoachesFor(
            IEnumerable<EventTicketTier> tiers)
        {
            var ids = tiers.Where(t => t.InstructorId.HasValue).Select(t => t.InstructorId!.Value).ToHashSet();
            if (ids.Count == 0) return new();
            return (await _instructors.List(_tenantContext.TenantId, activeOnly: false))
                .Where(i => ids.Contains(i.Id))
                .ToDictionary(i => i.Id);
        }

        // A training group's coach must belong to this tenant and be active, and the group's
        // own window (when set) must sit inside the event. Returns an error string, else null.
        private async Task<string?> ValidateGroup(UpsertEventTicketTierRequest r, Event ev)
        {
            if (r.InstructorId is Guid coachId)
            {
                var coach = await _instructors.Get(coachId, _tenantContext.TenantId);
                if (coach is null) return "That coach isn't available at this track.";
                if (!coach.IsActive) return $"\"{coach.Name}\" is no longer active.";
            }
            if (r.PartySizeMax is int pmax && pmax < Math.Max(1, r.PartySizeIncluded))
                return "The rider cap can't be smaller than the number the base price covers.";
            if (r.StartsAt.HasValue != r.EndsAt.HasValue)
                return "A group time needs both a start and an end, or neither.";
            if (r.StartsAt.HasValue && r.EndsAt.HasValue)
            {
                var s = r.StartsAt.Value.ToUniversalTime();
                var e = r.EndsAt.Value.ToUniversalTime();
                if (e <= s) return "The group's end time must be after its start time.";
                var evStart = DateTime.SpecifyKind(ev.StartsAt, DateTimeKind.Utc);
                var evEnd = DateTime.SpecifyKind(ev.EndsAt, DateTimeKind.Utc);
                if (s < evStart || e > evEnd)
                    return "A group's time has to fall inside the event's own start and end.";
            }
            return null;
        }

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
