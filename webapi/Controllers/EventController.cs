using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Notifications;
using Services.Repositories.Data.EventData;
using Services.Repositories.Interfaces;
using Services.Storage;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Event;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EventController : ControllerBase
    {
        private readonly IEventRepository _events;
        private readonly ITenantEventTypeRepository _eventTypes;
        private readonly IEventTicketTierRepository _tiers;
        private readonly IEventExtraRepository _extras;
        private readonly IWaiverRepository _waivers;
        private readonly IEventNotifier _notifier;
        private readonly ITenantContext _tenantContext;
        private readonly IImageStorage _imageStorage;

        public EventController(
            IEventRepository events,
            ITenantEventTypeRepository eventTypes,
            IEventTicketTierRepository tiers,
            IEventExtraRepository extras,
            IWaiverRepository waivers,
            IEventNotifier notifier,
            ITenantContext tenantContext,
            IImageStorage imageStorage)
        {
            _events = events;
            _eventTypes = eventTypes;
            _tiers = tiers;
            _extras = extras;
            _waivers = waivers;
            _notifier = notifier;
            _tenantContext = tenantContext;
            _imageStorage = imageStorage;
        }

        [HttpGet]
        public async Task<IActionResult> GetInRange([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved for this request.");
            }

            if (toUtc <= fromUtc)
            {
                return new ApiResponses().BadRequestResult("toUtc must be after fromUtc.");
            }

            var events = await _events.GetInRange(_tenantContext.TenantId, fromUtc.ToUniversalTime(), toUtc.ToUniversalTime());
            var types = (await _eventTypes.GetAllForTenant(_tenantContext.TenantId)).ToDictionary(t => t.Id);
            var tiersByEvent = await _tiers.GetForEvents(events.Select(e => e.Id), _tenantContext.TenantId, activeOnly: true);

            // Same pattern for extras: batch the eligibility map + products map +
            // sold-counts in one go so the per-row build can stay synchronous.
            var extrasByEvent = await _extras.ListEligibilityForEvents(events.Select(e => e.Id));
            var allExtras = (await _extras.ListProducts(_tenantContext.TenantId, activeOnly: false))
                .ToDictionary(p => p.Id);
            var extrasSold = await _extras.SumSoldForEvents(events.Select(e => e.Id));
            // Variants: pulled tenant-wide and filtered to active. Tenant-wide sold-counts
            // since variant inventory is tenant-wide (not per-event).
            var variantsByProduct = await _extras.ListVariantsForProducts(allExtras.Keys);
            var variantSold = await _extras.SumSoldVariants(
                variantsByProduct.Values.SelectMany(v => v).Select(v => v.Id));

            var response = events.Select(ev =>
            {
                var r = MapResponse(ev, types);
                if (tiersByEvent.TryGetValue(ev.Id, out var tiers) && tiers.Count > 0)
                {
                    r.HasActiveTiers = true;
                    r.HasSpectatorTiers = tiers.Any(t => t.Kind == "gate_fee" && t.Audience == "spectator");
                    r.HasRaceEntryTiers = tiers.Any(t => t.Kind == "race_entry");
                    r.MinTicketPriceCents = tiers.Min(t => t.PriceCents);
                }
                if (extrasByEvent.TryGetValue(ev.Id, out var extraEligibility) && extraEligibility.Count > 0)
                {
                    // Render in catalog sort_order — eligibility rows arrive in insert order,
                    // which is meaningless to riders. Mirrors the pass-product ordering above.
                    var orderedEligibility = extraEligibility
                        .Where(e => allExtras.ContainsKey(e.ProductId))
                        .OrderBy(e => allExtras[e.ProductId].SortOrder)
                        .ThenBy(e => allExtras[e.ProductId].Name);
                    foreach (var elig in orderedEligibility)
                    {
                        if (!allExtras.TryGetValue(elig.ProductId, out var prod) || !prod.IsActive) continue;
                        // Expired products drop off the rider-facing list. Admin still
                        // sees them in the catalog so they can re-extend the date.
                        if (prod.ExpiresAt.HasValue && prod.ExpiresAt.Value <= DateTime.UtcNow) continue;
                        var sold = extrasSold.GetValueOrDefault((ev.Id, prod.Id), 0);
                        var variantList = variantsByProduct.TryGetValue(prod.Id, out var vs)
                            ? vs.Where(v => v.IsActive).Select(v =>
                            {
                                var vsold = variantSold.GetValueOrDefault(v.Id, 0);
                                return new EligibleExtraVariant
                                {
                                    Id = v.Id,
                                    Size = v.Size, Color = v.Color, Gender = v.Gender,
                                    PriceCents = v.PriceCents ?? prod.PriceCents,
                                    ImageUrl = v.ImageUrl ?? prod.ImageUrl,
                                    Inventory = v.Inventory,
                                    Sold = vsold,
                                    Remaining = v.Inventory.HasValue ? Math.Max(0, v.Inventory.Value - vsold) : -1,
                                };
                            }).ToList()
                            : new List<EligibleExtraVariant>();
                        r.EligibleExtras.Add(new EligibleExtra
                        {
                            ProductId = prod.Id,
                            Name = prod.Name,
                            Kind = prod.Kind,
                            PriceCents = prod.PriceCents,
                            ImageUrl = prod.ImageUrl,
                            Inventory = elig.Inventory,
                            Sold = sold,
                            Remaining = elig.Inventory.HasValue ? Math.Max(0, elig.Inventory.Value - sold) : -1,
                            RequiresWaiver = prod.RequiresWaiver,
                            RiderPaidServiceChargeBps = prod.RiderPaidServiceChargeBps,
                            Variants = variantList,
                        });
                    }
                }
                return r;
            }).ToList();
            return new ApiResponses().OkResult(response);
        }

        // Public single-event detail. Used by the shareable /Event/{id} landing
        // page so visitors can see what's happening and buy a spectator pass
        // without needing an account. Tenant scope comes from the subdomain via
        // TenantResolutionMiddleware — same as the bulk GET above.
        [HttpGet("Public/{id:guid}")]
        public async Task<IActionResult> GetPublic(Guid id)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved for this request.");
            }
            var ev = await _events.GetById(id, _tenantContext.TenantId);
            if (ev is null) return new ApiResponses().NotFoundResult("Event not found.");

            // Don't leak draft/cancelled events to anonymous visitors.
            if (ev.Status != "scheduled")
            {
                return new ApiResponses().NotFoundResult("Event not found.");
            }

            var types = (await _eventTypes.GetAllForTenant(_tenantContext.TenantId)).ToDictionary(t => t.Id);
            var resp = await MapResponseAsync(ev, types);

            // Tier summary (so the landing page knows whether to show spectator
            // and race-entry CTAs, and the starting price).
            var tiers = await _tiers.GetForEvent(ev.Id, _tenantContext.TenantId, activeOnly: true);
            if (tiers.Count > 0)
            {
                resp.HasActiveTiers = true;
                resp.HasSpectatorTiers = tiers.Any(t => t.Kind == "gate_fee" && t.Audience == "spectator");
                resp.HasRaceEntryTiers = tiers.Any(t => t.Kind == "race_entry");
                resp.MinTicketPriceCents = tiers.Min(t => t.PriceCents);
            }
            return new ApiResponses().OkResult(resp);
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertEventRequest request)
        {
            var typeCheck = await _eventTypes.GetById(request.EventTypeId, _tenantContext.TenantId);
            if (typeCheck is null)
            {
                return new ApiResponses().BadRequestResult("Invalid event type for this tenant.");
            }

            if (request.EndsAtUtc < request.StartsAtUtc)
            {
                return new ApiResponses().BadRequestResult("EndsAt must be on or after StartsAt.");
            }

            var spectatorErr = await ValidateWaiverForEvent(request.SpectatorWaiverId, request.EndsAtUtc.ToUniversalTime(), "spectator");
            if (spectatorErr is not null) return new ApiResponses().BadRequestResult(spectatorErr);
            var racerErr = await ValidateWaiverForEvent(request.RacerWaiverId, request.EndsAtUtc.ToUniversalTime(), "racer");
            if (racerErr is not null) return new ApiResponses().BadRequestResult(racerErr);

            if (!request.AllowsRiders && !request.AllowsSpectators)
            {
                return new ApiResponses().BadRequestResult("An event must allow riders, spectators, or both.");
            }

            var ev = new Event
            {
                TenantId = _tenantContext.TenantId,
                EventTypeId = request.EventTypeId,
                Title = request.Title,
                Description = request.Description,
                StartsAt = request.StartsAtUtc.ToUniversalTime(),
                EndsAt = request.EndsAtUtc.ToUniversalTime(),
                AllDay = request.AllDay,
                Capacity = request.Capacity,
                LocationLabel = request.LocationLabel,
                Status = request.Status,
                AllowsRiders = request.AllowsRiders,
                AllowsSpectators = request.AllowsSpectators,
                RequiresRiderWaiver = request.RequiresRiderWaiver,
                RequiresSpectatorWaiver = request.RequiresSpectatorWaiver,
                SpectatorWaiverId = request.SpectatorWaiverId,
                RacerWaiverId = request.RacerWaiverId,
                ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl,
                ScheduleJson = SerializeSchedule(request.Schedule),
            };

            ev.Id = await _events.Create(ev);
            if (request.EligibleExtras is not null)
            {
                await _extras.ReplaceEligibility(ev.Id, request.EligibleExtras
                    .Select(e => new Services.Repositories.Data.ExtrasData.EventExtraEligibility
                    {
                        EventId = ev.Id, ProductId = e.ProductId, Inventory = e.Inventory,
                    }));
            }
            FireAndForgetNotify(ev);
            var types = new Dictionary<Guid, Services.Repositories.Data.TenantData.TenantEventType> { [typeCheck.Id] = typeCheck };
            return new ApiResponses().OkResult(await MapResponseAsync(ev, types));
        }

        private void FireAndForgetNotify(Event ev)
        {
            // Background fan-out so the admin's create request returns immediately.
            // If the process dies mid-fan-out, some subscribers won't be notified — acceptable for v1.
            _ = Task.Run(async () =>
            {
                try { await _notifier.NotifyNewEvent(ev.TenantId, ev); }
                catch { /* logged inside the notifier */ }
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertEventRequest request)
        {
            var existing = await _events.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Event not found.");
            }

            var typeCheck = await _eventTypes.GetById(request.EventTypeId, _tenantContext.TenantId);
            if (typeCheck is null)
            {
                return new ApiResponses().BadRequestResult("Invalid event type for this tenant.");
            }

            if (request.EndsAtUtc < request.StartsAtUtc)
            {
                return new ApiResponses().BadRequestResult("EndsAt must be on or after StartsAt.");
            }

            var spectatorErr = await ValidateWaiverForEvent(request.SpectatorWaiverId, request.EndsAtUtc.ToUniversalTime(), "spectator");
            if (spectatorErr is not null) return new ApiResponses().BadRequestResult(spectatorErr);
            var racerErr = await ValidateWaiverForEvent(request.RacerWaiverId, request.EndsAtUtc.ToUniversalTime(), "racer");
            if (racerErr is not null) return new ApiResponses().BadRequestResult(racerErr);

            if (!request.AllowsRiders && !request.AllowsSpectators)
            {
                return new ApiResponses().BadRequestResult("An event must allow riders, spectators, or both.");
            }

            existing.EventTypeId = request.EventTypeId;
            existing.Title = request.Title;
            existing.Description = request.Description;
            existing.StartsAt = request.StartsAtUtc.ToUniversalTime();
            existing.EndsAt = request.EndsAtUtc.ToUniversalTime();
            existing.AllDay = request.AllDay;
            existing.Capacity = request.Capacity;
            existing.LocationLabel = request.LocationLabel;
            existing.Status = request.Status;
            existing.AllowsRiders = request.AllowsRiders;
            existing.AllowsSpectators = request.AllowsSpectators;
            existing.RequiresRiderWaiver = request.RequiresRiderWaiver;
            existing.RequiresSpectatorWaiver = request.RequiresSpectatorWaiver;
            existing.SpectatorWaiverId = request.SpectatorWaiverId;
            existing.RacerWaiverId = request.RacerWaiverId;
            existing.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl;
            existing.ScheduleJson = SerializeSchedule(request.Schedule);

            await _events.Update(existing);
            if (request.EligibleExtras is not null)
            {
                await _extras.ReplaceEligibility(existing.Id, request.EligibleExtras
                    .Select(e => new Services.Repositories.Data.ExtrasData.EventExtraEligibility
                    {
                        EventId = existing.Id, ProductId = e.ProductId, Inventory = e.Inventory,
                    }));
            }
            var types = new Dictionary<Guid, Services.Repositories.Data.TenantData.TenantEventType> { [typeCheck.Id] = typeCheck };
            return new ApiResponses().OkResult(await MapResponseAsync(existing, types));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var existing = await _events.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Event not found.");
            }

            try
            {
                await _events.Delete(id, _tenantContext.TenantId);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23503")
            {
                // event → tier → purchase chain has paid purchases pinning the tiers;
                // deleting would orphan real money. Surface that to the admin instead
                // of a 500. Cancelling the event keeps the paid rows intact.
                return new ApiResponses().BadRequestResult(
                    "This event has tickets, race entries, or reservations on file and can't be deleted. " +
                    "Set status to Cancelled instead — that keeps the receipts intact.");
            }
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("{id:guid}/Duplicate")]
        public async Task<IActionResult> Duplicate(Guid id)
        {
            var source = await _events.GetById(id, _tenantContext.TenantId);
            if (source is null)
            {
                return new ApiResponses().NotFoundResult("Event not found.");
            }

            var shift = TimeSpan.FromDays(7);
            var clone = new Event
            {
                TenantId = source.TenantId,
                EventTypeId = source.EventTypeId,
                Title = source.Title,
                Description = source.Description,
                StartsAt = source.StartsAt + shift,
                EndsAt = source.EndsAt + shift,
                AllDay = source.AllDay,
                Capacity = source.Capacity,
                LocationLabel = source.LocationLabel,
                Status = "scheduled",
                AllowsRiders = source.AllowsRiders,
                AllowsSpectators = source.AllowsSpectators,
                RequiresRiderWaiver = source.RequiresRiderWaiver,
                RequiresSpectatorWaiver = source.RequiresSpectatorWaiver,
                SpectatorWaiverId = source.SpectatorWaiverId,
                RacerWaiverId = source.RacerWaiverId,
                ImageUrl = source.ImageUrl,
                ScheduleJson = source.ScheduleJson,
            };
            clone.Id = await _events.Create(clone);
            // Carry over the source event's extras eligibility so the duplicated event
            // already has the same add-ons without re-selecting.
            var srcExtras = await _extras.ListEligibilityForEvent(source.Id);
            if (srcExtras.Count > 0)
            {
                await _extras.ReplaceEligibility(clone.Id, srcExtras.Select(e =>
                    new Services.Repositories.Data.ExtrasData.EventExtraEligibility
                    {
                        EventId = clone.Id, ProductId = e.ProductId, Inventory = e.Inventory,
                    }));
            }

            // Carry over the ticket tiers (incl. price-ladder steps) so the duplicate is
            // sellable as-is. Relative date triggers (days-before) stay correct against the
            // shifted start; an absolute date trigger is shifted by the same offset.
            var srcTiers = await _tiers.GetForEvent(source.Id, _tenantContext.TenantId, activeOnly: false);
            foreach (var t in srcTiers)
            {
                await _tiers.Create(new Services.Repositories.Data.PaymentData.EventTicketTier
                {
                    TenantId = clone.TenantId,
                    EventId = clone.Id,
                    Kind = t.Kind,
                    Audience = t.Audience,
                    Required = t.Required,
                    Name = t.Name,
                    PriceCents = t.PriceCents,
                    Inventory = t.Inventory,
                    SortOrder = t.SortOrder,
                    IsActive = t.IsActive,
                    RiderPaidServiceChargeBps = t.RiderPaidServiceChargeBps,
                    LadderGroup = t.LadderGroup,
                    MinSold = t.MinSold,
                    EffectiveDaysBefore = t.EffectiveDaysBefore,
                    EffectiveAtUtc = t.EffectiveAtUtc.HasValue ? t.EffectiveAtUtc.Value.Add(shift) : (DateTime?)null,
                    BundledCouponCount = t.BundledCouponCount,
                    BundledCouponDiscountKind = t.BundledCouponDiscountKind,
                    BundledCouponDiscountValue = t.BundledCouponDiscountValue,
                    BundledCouponScope = t.BundledCouponScope,
                    BundledCouponExpiresInDays = t.BundledCouponExpiresInDays,
                });
            }
            FireAndForgetNotify(clone);

            var type = await _eventTypes.GetById(source.EventTypeId, _tenantContext.TenantId);
            var types = type is null
                ? new Dictionary<Guid, Services.Repositories.Data.TenantData.TenantEventType>()
                : new Dictionary<Guid, Services.Repositories.Data.TenantData.TenantEventType> { [type.Id] = type };
            return new ApiResponses().OkResult(await MapResponseAsync(clone, types));
        }

        /// <summary>
        /// Uploads a per-event cover image and returns its public URL. The frontend
        /// then patches the event via the regular Update endpoint with that URL —
        /// keeps the upload decoupled from row mutation so a stale upload can be
        /// discarded by simply not saving the form.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Image")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken ct)
        {
            if (file is null || file.Length == 0)
                return new ApiResponses().BadRequestResult("File is required.");
            if (file.Length > 5 * 1024 * 1024)
                return new ApiResponses().BadRequestResult("File exceeds 5 MB limit.");
            var allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["image/png"] = ".png",
                ["image/jpeg"] = ".jpg",
                ["image/webp"] = ".webp",
            };
            if (!allowed.TryGetValue(file.ContentType, out var ext))
                return new ApiResponses().BadRequestResult($"Unsupported content type: {file.ContentType}.");

            await using var stream = file.OpenReadStream();
            var url = await _imageStorage.SaveAsync(stream, _tenantContext.TenantId, "event", ext, ct);
            return new ApiResponses().OkResult(new { imageUrl = url });
        }

        // Reject event-waiver attachments where the waiver expires before the event
        // ends — riders signing the day-of would be signing an already-dead waiver.
        // Also rejects unknown / cross-tenant waiver ids defensively.
        private async Task<string?> ValidateWaiverForEvent(Guid? waiverId, DateTime eventEndsAtUtc, string audience)
        {
            if (!waiverId.HasValue) return null;
            var waiver = await _waivers.GetById(waiverId.Value, _tenantContext.TenantId);
            if (waiver is null) return $"Selected {audience} waiver isn't available.";
            if (waiver.ExpiresAt.HasValue && waiver.ExpiresAt.Value < eventEndsAtUtc)
            {
                return $"Selected {audience} waiver expires before this event ends — pick a waiver that's valid through the event.";
            }
            return null;
        }

        private static EventResponse MapResponse(Event ev, IReadOnlyDictionary<Guid, Services.Repositories.Data.TenantData.TenantEventType> types)
        {
            types.TryGetValue(ev.EventTypeId, out var type);
            return new EventResponse
            {
                Id = ev.Id,
                EventTypeId = ev.EventTypeId,
                EventTypeCode = type?.Code ?? string.Empty,
                EventTypeName = type?.Name ?? string.Empty,
                EventTypeColor = type?.Color ?? "#616161",
                EventTypeImageUrl = type?.ImageUrl,
                Title = ev.Title,
                Description = ev.Description,
                StartsAtUtc = DateTime.SpecifyKind(ev.StartsAt, DateTimeKind.Utc),
                EndsAtUtc = DateTime.SpecifyKind(ev.EndsAt, DateTimeKind.Utc),
                AllDay = ev.AllDay,
                Capacity = ev.Capacity,
                LocationLabel = ev.LocationLabel,
                Status = ev.Status,
                AllowsRiders = ev.AllowsRiders,
                AllowsSpectators = ev.AllowsSpectators,
                RequiresRiderWaiver = ev.RequiresRiderWaiver,
                RequiresSpectatorWaiver = ev.RequiresSpectatorWaiver,
                SpectatorWaiverId = ev.SpectatorWaiverId,
                RacerWaiverId = ev.RacerWaiverId,
                ImageUrl = ev.ImageUrl,
                Schedule = DeserializeSchedule(ev.ScheduleJson),
            };
        }

        private static string SerializeSchedule(List<ScheduleItem>? items)
        {
            var clean = (items ?? new List<ScheduleItem>())
                .Select(s => new ScheduleItem { Time = (s.Time ?? "").Trim(), Label = (s.Label ?? "").Trim() })
                .Where(s => s.Time.Length > 0 || s.Label.Length > 0)
                .ToList();
            return System.Text.Json.JsonSerializer.Serialize(clean);
        }

        private static List<ScheduleItem> DeserializeSchedule(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<ScheduleItem>();
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<ScheduleItem>>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new List<ScheduleItem>();
            }
            catch { return new List<ScheduleItem>(); }
        }

        // Single-event variant that hydrates the extras eligibility list. Used on
        // create/update so the admin sees the add-on list reflected back.
        private async Task<EventResponse> MapResponseAsync(Event ev,
            IReadOnlyDictionary<Guid, Services.Repositories.Data.TenantData.TenantEventType> types)
        {
            var resp = MapResponse(ev, types);
            // Extras eligibility — hydrate per-event inventory + sold + remaining + variants.
            var extraRows = await _extras.ListEligibilityForEvent(ev.Id);
            if (extraRows.Count > 0)
            {
                var extraProducts = (await _extras.ListProducts(_tenantContext.TenantId, activeOnly: false))
                    .ToDictionary(p => p.Id);
                var variantsByProduct = await _extras.ListVariantsForProducts(extraProducts.Keys);
                var variantSold = await _extras.SumSoldVariants(
                    variantsByProduct.Values.SelectMany(v => v).Select(v => v.Id));
                // Render in catalog sort_order — eligibility rows arrive in insert order.
                var orderedExtraRows = extraRows
                    .Where(e => extraProducts.ContainsKey(e.ProductId))
                    .OrderBy(e => extraProducts[e.ProductId].SortOrder)
                    .ThenBy(e => extraProducts[e.ProductId].Name);
                foreach (var elig in orderedExtraRows)
                {
                    if (!extraProducts.TryGetValue(elig.ProductId, out var prod) || !prod.IsActive) continue;
                    if (prod.ExpiresAt.HasValue && prod.ExpiresAt.Value <= DateTime.UtcNow) continue;
                    var sold = await _extras.SumSold(ev.Id, elig.ProductId);
                    var variantList = variantsByProduct.TryGetValue(prod.Id, out var vs)
                        ? vs.Where(v => v.IsActive).Select(v =>
                        {
                            var vsold = variantSold.GetValueOrDefault(v.Id, 0);
                            return new EligibleExtraVariant
                            {
                                Id = v.Id,
                                Size = v.Size, Color = v.Color, Gender = v.Gender,
                                PriceCents = v.PriceCents ?? prod.PriceCents,
                                ImageUrl = v.ImageUrl ?? prod.ImageUrl,
                                Inventory = v.Inventory,
                                Sold = vsold,
                                Remaining = v.Inventory.HasValue ? Math.Max(0, v.Inventory.Value - vsold) : -1,
                            };
                        }).ToList()
                        : new List<EligibleExtraVariant>();
                    resp.EligibleExtras.Add(new EligibleExtra
                    {
                        ProductId = prod.Id,
                        Name = prod.Name,
                        Kind = prod.Kind,
                        PriceCents = prod.PriceCents,
                        ImageUrl = prod.ImageUrl,
                        Inventory = elig.Inventory,
                        Sold = sold,
                        Remaining = elig.Inventory.HasValue ? Math.Max(0, elig.Inventory.Value - sold) : -1,
                        RequiresWaiver = prod.RequiresWaiver,
                        RiderPaidServiceChargeBps = prod.RiderPaidServiceChargeBps,
                        Variants = variantList,
                    });
                }
            }
            return resp;
        }
    }
}
