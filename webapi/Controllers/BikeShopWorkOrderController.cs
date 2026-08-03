using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.BikeShop;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // The service bench: work orders accrue labor + parts, then bill out through a shop_sale at
    // pickup. Parts consume stock when added to a committed job (estimates consume nothing until
    // accepted); the bill-out sale carries work_order_id so depletion never runs twice.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
    public class BikeShopWorkOrderController : ControllerBase
    {
        private readonly IBikeShopRepository _shop;
        private readonly IChargeRouter _chargeRouter;
        private readonly IPaymentProvider _payments;
        private readonly IFeeCalculator _feeCalculator;
        private readonly ITenantLedgerRepository _ledger;
        private readonly ITenantContext _tenantContext;
        private readonly Services.Helpers.ISmtpEmailer _emailer;
        private readonly Services.Helpers.ISmsSender _sms;
        private readonly ILogger<BikeShopWorkOrderController> _logger;
        private readonly IConfiguration _config;
        private readonly ITenantCreditRepository _credit;
        private readonly IDiscountPresetRepository _discounts;
        private readonly Services.Pricing.ISeasonPassPerkResolver _perks;
        private readonly webapi.Security.IManagerPinService _managerPin;
        private readonly Services.Audit.IAuditLogger _audit;

        public BikeShopWorkOrderController(IBikeShopRepository shop, IChargeRouter chargeRouter,
            IPaymentProvider payments, IFeeCalculator feeCalculator, ITenantLedgerRepository ledger,
            ITenantContext tenantContext, Services.Helpers.ISmtpEmailer emailer, IConfiguration config,
            ITenantCreditRepository credit,
            Services.Helpers.ISmsSender sms, ILogger<BikeShopWorkOrderController> logger,
            IDiscountPresetRepository discounts, webapi.Security.IManagerPinService managerPin,
            Services.Pricing.ISeasonPassPerkResolver perks,
            Services.Audit.IAuditLogger audit)
        {
            _discounts = discounts;
            _perks = perks;
            _managerPin = managerPin;
            _audit = audit;
            _shop = shop;
            _chargeRouter = chargeRouter;
            _payments = payments;
            _feeCalculator = feeCalculator;
            _ledger = ledger;
            _tenantContext = tenantContext;
            _emailer = emailer;
            _sms = sms;
            _logger = logger;
            _config = config;
            _credit = credit;
        }

        private Guid TenantId => _tenantContext.TenantId;
        private Guid? UserId => Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;

        // Staff who can be assigned to a job. UsersManage guards the full users screen; the
        // bench only needs id + name, so this stays inside ShopCounter with a minimal projection.
        [HttpGet("Technicians")]
        public async Task<IActionResult> Technicians([FromServices] IUserRepository users)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var staff = await users.ListByTenant(TenantId);
            return new ApiResponses().OkResult(staff
                .Where(u => u.Status == "active")
                .Select(u => new { id = u.Id, name = $"{u.FirstName} {u.LastName}".Trim() })
                .OrderBy(u => u.name));
        }

        [HttpGet("WorkOrders")]
        public async Task<IActionResult> List([FromQuery] bool includeClosed = false, [FromQuery] int limit = 100)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await _shop.ListWorkOrders(TenantId, includeClosed, Math.Clamp(limit, 1, 500)));
        }

        [HttpGet("WorkOrders/{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var wo = await _shop.GetWorkOrder(id, TenantId);
            return wo is null ? new ApiResponses().NotFoundResult("Work order not found.") : new ApiResponses().OkResult(wo);
        }

        [HttpPost("WorkOrders")]
        public async Task<IActionResult> Create([FromBody] UpsertShopWorkOrderRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.BikeShopEnabled)
                return new ApiResponses().BadRequestResult("The bike shop isn't turned on for this track.");
            if (req.SubjectItemId is null && req.CustomerBikeId is null && string.IsNullOrWhiteSpace(req.CustomerBikeDesc))
                return new ApiResponses().BadRequestResult("Describe the bike being serviced (or pick a shop unit).");
            if (req.SubjectItemId is not null && await _shop.GetItem(req.SubjectItemId.Value, TenantId) is null)
                return new ApiResponses().BadRequestResult("That shop unit doesn't exist.");

            var statuses = await LoadStatusMap();
            if (!statuses.TryGetValue(req.Status, out var startStatus) || !startStatus.IsActive)
                return new ApiResponses().BadRequestResult("That work order status isn't available.");
            // A new order can't start in a terminal status.
            if (startStatus.Behavior is "cancelled" or "done")
                return new ApiResponses().BadRequestResult("A new work order can't start in that status.");

            // Attaching to a visit is only allowed for a group the tenant already owns.
            if (req.GroupId is Guid gid && !await _shop.GroupExistsForTenant(gid, TenantId))
                return new ApiResponses().BadRequestResult("That customer visit doesn't exist.");

            var wo = Map(new ShopWorkOrder { TenantId = TenantId }, req);
            wo.GroupId = req.GroupId;
            var id = await _shop.CreateWorkOrder(wo);
            return new ApiResponses().OkResult(new { id });
        }

        [HttpPut("WorkOrders/{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertShopWorkOrderRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var existing = await _shop.GetWorkOrder(id, TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Work order not found.");
            if (existing.Status == "picked_up")
                return new ApiResponses().BadRequestResult("This work order is closed (picked up).");
            if (req.SubjectItemId is not null && await _shop.GetItem(req.SubjectItemId.Value, TenantId) is null)
                return new ApiResponses().BadRequestResult("That shop unit doesn't exist.");

            // Resolve behaviors from the tenant's status definitions. The seven built-in CODES are
            // the behavioral backbone, so this drives the same inventory rules whatever a tenant
            // named or colored its statuses, and works for custom ('open') stages too.
            var statuses = await LoadStatusMap();
            if (!statuses.TryGetValue(req.Status, out var target) || !target.IsActive)
                return new ApiResponses().BadRequestResult("That work order status isn't available.");
            // 'done' (picked up) is a billing outcome, not a manual status: bill the job out instead.
            if (target.Behavior == "done")
                return new ApiResponses().BadRequestResult("Bill the work order out to mark it picked up.");
            var fromBehavior = statuses.TryGetValue(existing.Status, out var cur) ? cur.Behavior : "open";

            // Status transitions with inventory side effects:
            //   estimate -> anything committed: consume the quoted parts now.
            //   anything -> cancelled: hand consumed parts back to the shelf.
            //   committed -> estimate is NOT allowed (would need a reversal that surprises staff).
            if (fromBehavior != "estimate" && target.Behavior == "estimate")
                return new ApiResponses().BadRequestResult("A committed work order can't go back to an estimate.");
            var committing = fromBehavior == "estimate" && target.Behavior is not ("estimate" or "cancelled");
            var cancelling = target.Behavior == "cancelled" && fromBehavior != "cancelled";

            await _shop.UpdateWorkOrder(Map(new ShopWorkOrder { Id = id, TenantId = TenantId }, req));
            if (committing) await _shop.ConsumePartsForWorkOrder(id, TenantId, UserId);
            if (cancelling) await _shop.ReverseConsumedParts(id, TenantId, UserId);

            // Notify the customer when the order enters a status flagged for it, and only on an
            // actual change so bouncing the status can't re-notify. The built-in "ready" keeps its
            // claim-once "your bike is ready" path; other notify statuses send a status update.
            if (req.Status != existing.Status && target.NotifyCustomer)
            {
                if (target.Behavior == "ready") await SendReadyNotice(id);
                else await SendStatusNotice(id, target);
            }

            return new ApiResponses().OkResult();
        }

        /// <summary>Tells the customer their bike is ready, by email and by text when we have a
        /// mobile. Best effort on purpose: a failed send must never fail the status change staff
        /// just made, so the notice is claimed first and failures are swallowed.</summary>
        private async Task SendReadyNotice(Guid workOrderId)
        {
            var t = _tenantContext.Tenant;
            // Nothing to send if the tenant turned both channels off; don't burn the once-only
            // claim on a no-op, or enabling a channel later would find the notice already spent.
            if (!t.ShopReadyNotifyEmail && !t.ShopReadyNotifySms) return;
            if (!await _shop.TryClaimReadyNotice(workOrderId, TenantId)) return;

            var wo = await _shop.GetWorkOrder(workOrderId, TenantId);
            if (wo is null) return;

            var tenant = _tenantContext.Tenant;
            var bike = wo.CustomerBikeDesc ?? "your bike";
            try
            {
                if (tenant.ShopReadyNotifyEmail && !string.IsNullOrWhiteSpace(wo.CustomerEmail) && _emailer.IsConfigured)
                {
                    static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);
                    var total = wo.Lines.Sum(l => l.UnitPriceCents * l.Quantity);
                    var owing = Math.Max(0, total - (wo.DepositPaidAt is not null ? wo.DepositCents : 0));
                    var html =
                        $"<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:480px\">" +
                        $"<h2 style=\"margin:0 0 8px\">{Enc(tenant.DisplayName)}</h2>" +
                        $"<p>Hi {Enc(wo.CustomerName)},</p>" +
                        $"<p><strong>{Enc(bike)} is ready to pick up.</strong></p>" +
                        (owing > 0
                            ? $"<p>Balance due at pickup: <strong>{ShopMoney(owing)}</strong>.</p>"
                            : "") +
                        $"<p style=\"font-size:12px;color:#666\">See you soon.</p></div>";
                    await _emailer.Send(wo.CustomerEmail!, $"{tenant.DisplayName}: {bike} is ready",
                        html, null, Services.Email.TenantEmailIdentity.For(tenant));
                }
                if (tenant.ShopReadyNotifySms && !string.IsNullOrWhiteSpace(wo.CustomerPhone) && _sms.IsConfiguredFor(tenant))
                {
                    await _sms.Send(tenant, wo.CustomerPhone!,
                        $"{tenant.DisplayName}: {bike} is ready to pick up.", UserId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ready notice failed for work order {Id}", workOrderId);
            }
        }

        /// <summary>The tenant's statuses keyed by code (case-insensitive), used to resolve
        /// behavior and the notify flag during transitions.</summary>
        private async Task<Dictionary<string, ShopWorkOrderStatus>> LoadStatusMap() =>
            (await _shop.ListWorkOrderStatuses(TenantId))
                .ToDictionary(s => s.Code, s => s, StringComparer.OrdinalIgnoreCase);

        /// <summary>A generic "status update" notice for a non-ready status flagged notify_customer.
        /// Best effort, same channels/toggles as the ready notice; no persistent claim (it only
        /// fires on an actual staff-made status change, so it can't loop).</summary>
        private async Task SendStatusNotice(Guid workOrderId, ShopWorkOrderStatus status)
        {
            var tenant = _tenantContext.Tenant;
            if (!tenant.ShopReadyNotifyEmail && !tenant.ShopReadyNotifySms) return;
            var wo = await _shop.GetWorkOrder(workOrderId, TenantId);
            if (wo is null) return;
            var bike = wo.CustomerBikeDesc ?? "your bike";
            try
            {
                if (tenant.ShopReadyNotifyEmail && !string.IsNullOrWhiteSpace(wo.CustomerEmail) && _emailer.IsConfigured)
                {
                    static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);
                    var html =
                        $"<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:480px\">" +
                        $"<h2 style=\"margin:0 0 8px\">{Enc(tenant.DisplayName)}</h2>" +
                        $"<p>Hi {Enc(wo.CustomerName)},</p>" +
                        $"<p>An update on {Enc(bike)}: <strong>{Enc(status.Name)}</strong>.</p>" +
                        $"<p style=\"font-size:12px;color:#666\">We'll be in touch.</p></div>";
                    await _emailer.Send(wo.CustomerEmail!, $"{tenant.DisplayName}: update on {bike}",
                        html, null, Services.Email.TenantEmailIdentity.For(tenant));
                }
                if (tenant.ShopReadyNotifySms && !string.IsNullOrWhiteSpace(wo.CustomerPhone) && _sms.IsConfiguredFor(tenant))
                {
                    await _sms.Send(tenant, wo.CustomerPhone!,
                        $"{tenant.DisplayName}: {bike} - {status.Name}.", UserId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Status notice failed for work order {Id}", workOrderId);
            }
        }

        private static ShopWorkOrder Map(ShopWorkOrder wo, UpsertShopWorkOrderRequest req)
        {
            wo.CustomerName = req.CustomerName.Trim();
            wo.CustomerPhone = string.IsNullOrWhiteSpace(req.CustomerPhone) ? null : req.CustomerPhone.Trim();
            wo.CustomerEmail = string.IsNullOrWhiteSpace(req.CustomerEmail) ? null : req.CustomerEmail.Trim();
            wo.CustomerUserId = req.CustomerUserId;
            wo.SubjectItemId = req.SubjectItemId;
            wo.CustomerBikeDesc = string.IsNullOrWhiteSpace(req.CustomerBikeDesc) ? null : req.CustomerBikeDesc.Trim();
            wo.CustomerBikeId = req.CustomerBikeId;
            wo.Status = req.Status;
            wo.AssignedTechUserId = req.AssignedTechUserId;
            wo.IntakeNotes = string.IsNullOrWhiteSpace(req.IntakeNotes) ? null : req.IntakeNotes.Trim();
            wo.CustomerNotes = string.IsNullOrWhiteSpace(req.CustomerNotes) ? null : req.CustomerNotes.Trim();
            wo.PromisedAt = req.PromisedAt;
            return wo;
        }

        [HttpPost("WorkOrders/{id:guid}/Lines")]
        public async Task<IActionResult> AddLine(Guid id, [FromBody] AddShopWorkOrderLineRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var wo = await _shop.GetWorkOrder(id, TenantId);
            if (wo is null) return new ApiResponses().NotFoundResult("Work order not found.");
            if (wo.Status is "picked_up" or "cancelled")
                return new ApiResponses().BadRequestResult("This work order is closed.");

            int price;
            decimal? laborHours = null;
            int? laborRateCents = null;
            if (req.LineKind == "labor")
            {
                if (string.IsNullOrWhiteSpace(req.Description))
                    return new ApiResponses().BadRequestResult("Labor needs a description.");

                var shopRate = _tenantContext.Tenant.ShopLaborRateCents;
                if (req.LaborHours is > 0 && shopRate is > 0)
                {
                    // Priced from time: the rate is the tenant's (server-side, never the client's),
                    // and the money is derived so the stored hours * rate always reconciles to it.
                    laborHours = req.LaborHours.Value;
                    laborRateCents = shopRate.Value;
                    price = (int)Math.Round(laborHours.Value * laborRateCents.Value, MidpointRounding.AwayFromZero);
                }
                else
                {
                    // Flat labor charge (no hours, or no rate configured): take the typed price.
                    if (req.UnitPriceCents is null)
                        return new ApiResponses().BadRequestResult("Labor needs a price.");
                    price = req.UnitPriceCents.Value;
                }
            }
            else
            {
                if (req.VariantId is null)
                    return new ApiResponses().BadRequestResult("Pick the part being used.");
                var variant = await _shop.GetVariant(req.VariantId.Value, TenantId);
                if (variant is null) return new ApiResponses().BadRequestResult("That part doesn't exist.");
                if (variant.TrackingKind != "pool")
                    return new ApiResponses().BadRequestResult("Serialized units aren't consumed as parts — sell them at the register.");
                // Default to the shelf price; staff can override (e.g. warranty part at $0).
                price = req.UnitPriceCents ?? variant.SalePriceCents
                    ?? throw new InvalidOperationException("unreachable");
                if (req.UnitPriceCents is null && variant.SalePriceCents is null)
                    return new ApiResponses().BadRequestResult("That part has no sale price — enter one on the line.");
            }

            var lineId = await _shop.AddWorkOrderLine(new ShopWorkOrderLine
            {
                WorkOrderId = id,
                LineKind = req.LineKind,
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
                VariantId = req.LineKind == "part" ? req.VariantId : null,
                Quantity = req.Quantity,
                UnitPriceCents = price,
                LaborHours = laborHours,
                LaborRateCents = laborRateCents,
                // Estimated time is a labor concept only.
                EstimatedMinutes = req.LineKind == "labor" ? req.EstimatedMinutes : null,
            }, TenantId, UserId);
            return lineId is null
                ? new ApiResponses().BadRequestResult("Could not add the line.")
                : new ApiResponses().OkResult(new { id = lineId });
        }

        [HttpDelete("WorkOrderLines/{lineId:guid}")]
        public async Task<IActionResult> RemoveLine(Guid lineId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var n = await _shop.RemoveWorkOrderLine(lineId, TenantId, UserId);
            return n == 0 ? new ApiResponses().NotFoundResult("Line not found.") : new ApiResponses().OkResult();
        }

        /// <summary>Start the labor timer on this job.</summary>
        [HttpPost("WorkOrders/{id:guid}/Timer/Start")]
        public async Task<IActionResult> StartTimer(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var wo = await _shop.GetWorkOrder(id, TenantId);
            if (wo is null) return new ApiResponses().NotFoundResult("Work order not found.");
            if (wo.Status is "picked_up" or "cancelled")
                return new ApiResponses().BadRequestResult("This work order is closed.");
            await _shop.StartWorkOrderTimer(id, TenantId);
            return new ApiResponses().OkResult(await _shop.GetWorkOrder(id, TenantId));
        }

        /// <summary>Stop the timer, folding the elapsed minutes into the actual total.</summary>
        [HttpPost("WorkOrders/{id:guid}/Timer/Stop")]
        public async Task<IActionResult> StopTimer(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _shop.StopWorkOrderTimer(id, TenantId);
            var wo = await _shop.GetWorkOrder(id, TenantId);
            return wo is null ? new ApiResponses().NotFoundResult("Work order not found.") : new ApiResponses().OkResult(wo);
        }

        /// <summary>Set the accumulated actual minutes by hand (stops the timer).</summary>
        [HttpPut("WorkOrders/{id:guid}/ActualMinutes")]
        public async Task<IActionResult> SetActualMinutes(Guid id, [FromBody] SetActualMinutesRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.Minutes < 0) return new ApiResponses().BadRequestResult("Minutes can't be negative.");
            var n = await _shop.SetWorkOrderActualMinutes(id, TenantId, req.Minutes);
            if (n == 0) return new ApiResponses().NotFoundResult("Work order not found.");
            return new ApiResponses().OkResult(await _shop.GetWorkOrder(id, TenantId));
        }

        /// <summary>Start (or return) a customer visit for this order, so another bike can be added
        /// to the same visit. Returns the shared group id.</summary>
        [HttpPost("WorkOrders/{id:guid}/Group")]
        public async Task<IActionResult> EnsureGroup(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var groupId = await _shop.EnsureWorkOrderGroup(id, TenantId);
            return groupId is null
                ? new ApiResponses().NotFoundResult("Work order not found.")
                : new ApiResponses().OkResult(new { groupId });
        }

        /// <summary>Approve or decline a single line (the customer's call, recorded by staff). A
        /// declined line won't consume stock or be billed.</summary>
        [HttpPut("WorkOrderLines/{lineId:guid}/Approval")]
        public async Task<IActionResult> SetLineApproval(Guid lineId, [FromBody] SetLineApprovalRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.Status is not ("pending" or "approved" or "declined"))
                return new ApiResponses().BadRequestResult("Status must be approved, declined, or pending.");
            var n = await _shop.SetLineApproval(lineId, TenantId, req.Status, UserId);
            return n == 0 ? new ApiResponses().NotFoundResult("Line not found.") : new ApiResponses().OkResult();
        }

        /// <summary>Approve every still-pending line at once.</summary>
        [HttpPost("WorkOrders/{id:guid}/ApproveAllLines")]
        public async Task<IActionResult> ApproveAllLines(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (await _shop.GetWorkOrder(id, TenantId) is null)
                return new ApiResponses().NotFoundResult("Work order not found.");
            await _shop.ApproveAllPendingLines(id, TenantId, UserId);
            return new ApiResponses().OkResult();
        }

        /// <summary>QC sign-off. A non-null checker (validated as active tenant staff) records the
        /// review and stamps the time; null clears it.</summary>
        [HttpPut("WorkOrders/{id:guid}/QcCheck")]
        public async Task<IActionResult> SetQcCheck(Guid id, [FromBody] SetWorkOrderQcRequest req,
            [FromServices] IUserRepository users)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.CheckedByUserId is Guid checker)
            {
                // The reviewer must be active staff of THIS tenant, so a checker id can't be spoofed
                // to another tenant's user (or a rider).
                var staff = await users.ListByTenant(TenantId);
                if (!staff.Any(u => u.Id == checker && u.Status == "active"))
                    return new ApiResponses().BadRequestResult("The reviewer must be an active staff member.");
            }
            var n = await _shop.SetWorkOrderQcCheck(id, TenantId, req.CheckedByUserId);
            if (n == 0) return new ApiResponses().NotFoundResult("Work order not found.");
            var wo = await _shop.GetWorkOrder(id, TenantId);
            return new ApiResponses().OkResult(wo);
        }

        /// <summary>Append an internal note (staff-only thread; never shown to the customer).</summary>
        [HttpPost("WorkOrders/{id:guid}/Notes")]
        public async Task<IActionResult> AddNote(Guid id, [FromBody] AddShopWorkOrderNoteRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var body = req.Body?.Trim();
            if (string.IsNullOrEmpty(body)) return new ApiResponses().BadRequestResult("The note is empty.");
            var note = await _shop.AddWorkOrderNote(id, TenantId, body, UserId);
            return note is null
                ? new ApiResponses().NotFoundResult("Work order not found.")
                : new ApiResponses().OkResult(note);
        }

        // ── Work order statuses (tenant-customizable) ───────────────────────────────

        /// <summary>The tenant's statuses (built-in + custom), for the bench UI and the editor.</summary>
        [HttpGet("WorkOrderStatuses")]
        public async Task<IActionResult> ListStatuses()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await _shop.ListWorkOrderStatuses(TenantId));
        }

        /// <summary>Persist a drag-drop reorder of the stages in one round trip.</summary>
        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost("WorkOrderStatuses/Reorder")]
        public async Task<IActionResult> ReorderStatuses([FromBody] ReorderWorkOrderStatusesRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.Items.Count == 0) return new ApiResponses().OkResult();
            await _shop.UpdateWorkOrderStatusSortOrders(TenantId,
                req.Items.Select(i => i.Id).ToList(), req.Items.Select(i => i.SortOrder).ToList());
            return new ApiResponses().OkResult();
        }

        /// <summary>Add a custom working stage. Configuration, so gated above the bench.</summary>
        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost("WorkOrderStatuses")]
        public async Task<IActionResult> CreateStatus([FromBody] CreateWorkOrderStatusRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var name = req.Name?.Trim();
            if (string.IsNullOrEmpty(name)) return new ApiResponses().BadRequestResult("Name is required.");

            var existing = await _shop.ListWorkOrderStatuses(TenantId);
            var codes = existing.Select(s => s.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var code = UniqueSlug(name, codes);
            if (code.Length == 0) return new ApiResponses().BadRequestResult("Give the status a name with letters or numbers.");
            var nextSort = existing.Count == 0 ? 100 : existing.Max(s => s.SortOrder) + 10;

            var created = await _shop.CreateWorkOrderStatus(TenantId, code, name,
                string.IsNullOrWhiteSpace(req.Color) ? "grey" : req.Color.Trim(), req.NotifyCustomer, nextSort);
            return created is null
                ? new ApiResponses().BadRequestResult("Could not create the status.")
                : new ApiResponses().OkResult(created);
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("WorkOrderStatuses/{id:guid}")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateWorkOrderStatusRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var name = req.Name?.Trim();
            if (string.IsNullOrEmpty(name)) return new ApiResponses().BadRequestResult("Name is required.");
            var status = await _shop.GetWorkOrderStatus(id, TenantId);
            if (status is null) return new ApiResponses().NotFoundResult("Status not found.");

            // A built-in can't be turned off (its behavior is load-bearing), and the default can't
            // be turned off (a new order needs somewhere to start).
            var isActive = req.IsActive;
            if (status.IsBuiltin) isActive = true;
            if (status.IsDefault && !isActive)
                return new ApiResponses().BadRequestResult("The default status can't be turned off.");
            if (!isActive && await _shop.CountWorkOrdersInStatus(TenantId, status.Code) > 0)
                return new ApiResponses().BadRequestResult("Some work orders are in this status. Move them first.");

            await _shop.UpdateWorkOrderStatusPresentation(id, TenantId, name,
                string.IsNullOrWhiteSpace(req.Color) ? "grey" : req.Color.Trim(),
                req.NotifyCustomer, req.SortOrder, isActive);
            return new ApiResponses().OkResult(await _shop.GetWorkOrderStatus(id, TenantId));
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("WorkOrderStatuses/{id:guid}/Default")]
        public async Task<IActionResult> SetDefaultStatus(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var n = await _shop.SetDefaultWorkOrderStatus(id, TenantId);
            return n == 0
                ? new ApiResponses().BadRequestResult("That status can't be the default (must be an active, non-terminal status).")
                : new ApiResponses().OkResult(await _shop.ListWorkOrderStatuses(TenantId));
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpDelete("WorkOrderStatuses/{id:guid}")]
        public async Task<IActionResult> DeleteStatus(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var n = await _shop.DeleteWorkOrderStatus(id, TenantId);
            return n == 0
                ? new ApiResponses().BadRequestResult("Only a custom status with no work orders in it can be deleted. Turn it off instead.")
                : new ApiResponses().OkResult();
        }

        // Slug from a name (lowercase, underscores), made unique against existing codes.
        private static string UniqueSlug(string name, HashSet<string> taken)
        {
            var chars = name.Trim().ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
            var slug = new string(chars);
            while (slug.Contains("__")) slug = slug.Replace("__", "_");
            slug = slug.Trim('_');
            if (slug.Length > 32) slug = slug[..32];
            if (slug.Length == 0) return "";
            var candidate = slug;
            var n = 2;
            while (taken.Contains(candidate)) candidate = $"{slug}_{n++}";
            return candidate;
        }

        // What the order-from-supplier dialog needs: open POs to append to and suppliers for a
        // new one. The full purchasing screen is CatalogManage; this minimal projection stays
        // inside ShopCounter so a bench tech can raise a special order without purchasing rights.
        /// <summary>Drops a saved job's labor and parts onto this work order. Part prices resolve
        /// to the variant's CURRENT price unless the template pinned one, and a part whose variant
        /// has since been deactivated is skipped and named rather than quoted at zero.</summary>
        [HttpPost("WorkOrders/{id:guid}/ApplyJobTemplate/{templateId:guid}")]
        public async Task<IActionResult> ApplyJobTemplate(Guid id, Guid templateId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var wo = await _shop.GetWorkOrder(id, TenantId);
            if (wo is null) return new ApiResponses().NotFoundResult("Work order not found.");
            if (wo.Status is "picked_up" or "cancelled")
                return new ApiResponses().BadRequestResult("This work order is closed.");

            var (added, skipped) = await _shop.ApplyJobTemplate(templateId, id, TenantId);
            if (added == 0 && skipped.Count == 0)
                return new ApiResponses().BadRequestResult("That job has no lines to add.");
            return new ApiResponses().OkResult(new { added, skipped });
        }

        [HttpGet("SpecialOrderOptions")]
        public async Task<IActionResult> SpecialOrderOptions()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var pos = (await _shop.ListPurchaseOrders(TenantId))
                .Where(p => p.Status is "open" or "ordered")
                .Select(p => new { id = p.Id, reference = p.Reference, status = p.Status, supplierId = p.SupplierId });
            var suppliers = (await _shop.ListSuppliers(TenantId, activeOnly: true))
                .Select(s => new { id = s.Id, name = s.Name });
            return new ApiResponses().OkResult(new { pos, suppliers });
        }

        // ── Special orders: put a part line on a supplier PO ──────────────────────
        // The part isn't on the shelf, so order it: add a line to an existing open PO (or spin up
        // a new one), link the work-order line to it, and park the job in awaiting_parts. When the
        // PO line is received, arrival processing consumes the part, advances the job, and emails
        // the customer (BikeShopController.ReceiveLine).
        [HttpPost("WorkOrders/{id:guid}/Lines/{lineId:guid}/Order")]
        public async Task<IActionResult> OrderLineFromSupplier(Guid id, Guid lineId, [FromBody] OrderShopWoPartRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var wo = await _shop.GetWorkOrder(id, TenantId);
            if (wo is null) return new ApiResponses().NotFoundResult("Work order not found.");
            if (wo.Status is "picked_up" or "cancelled")
                return new ApiResponses().BadRequestResult("This work order is closed.");
            var line = wo.Lines.FirstOrDefault(l => l.Id == lineId);
            if (line is null) return new ApiResponses().NotFoundResult("Line not found on this work order.");
            if (line.LineKind != "part" || line.VariantId is null)
                return new ApiResponses().BadRequestResult("Only a part line can be ordered from a supplier.");
            if (line.ArrivedAt is not null)
                return new ApiResponses().BadRequestResult("This part has already arrived.");
            if (line.PoLineId is not null)
                return new ApiResponses().BadRequestResult("This part is already on order.");

            var variant = await _shop.GetVariant(line.VariantId.Value, TenantId);
            if (variant is null) return new ApiResponses().BadRequestResult("That part no longer exists.");

            Guid poId;
            if (req.PoId is not null)
            {
                var po = await _shop.GetPurchaseOrder(req.PoId.Value, TenantId);
                if (po is null) return new ApiResponses().NotFoundResult("Purchase order not found.");
                if (po.Status is not ("open" or "ordered"))
                    return new ApiResponses().BadRequestResult("That purchase order is closed. Pick an open one.");
                poId = po.Id;
            }
            else
            {
                if (req.SupplierId is not null &&
                    !(await _shop.ListSuppliers(TenantId, activeOnly: false)).Any(s => s.Id == req.SupplierId))
                    return new ApiResponses().BadRequestResult("That supplier doesn't exist.");
                poId = await _shop.CreatePurchaseOrder(new ShopPurchaseOrder
                {
                    TenantId = TenantId,
                    SupplierId = req.SupplierId,
                    Status = "open",
                    Notes = $"Special order for {wo.CustomerName}",
                    CreatedByUserId = UserId,
                });
            }

            var poLineId = await _shop.AddPurchaseOrderLine(new ShopPoLine
            {
                PoId = poId,
                VariantId = line.VariantId.Value,
                QuantityOrdered = line.Quantity,
                UnitCostCents = req.UnitCostCents ?? variant.CostCents ?? 0,
            }, TenantId);
            if (!await _shop.LinkWorkOrderLineToPoLine(lineId, TenantId, poLineId))
                return new ApiResponses().BadRequestResult("Could not link the part to the order. Reload and try again.");

            // Park the job until the parts land (a quote stays a quote).
            if (wo.Status is "intake" or "in_progress" or "ready")
            {
                wo.Status = "awaiting_parts";
                await _shop.UpdateWorkOrder(wo);
            }
            return new ApiResponses().OkResult(new { poId, poLineId });
        }

        // ── Deposits ──────────────────────────────────────────────────────────────

        [HttpPost("WorkOrders/{id:guid}/Deposit")]
        public async Task<IActionResult> SetDeposit(Guid id, [FromBody] SetShopWoDepositRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var wo = await _shop.GetWorkOrder(id, TenantId);
            if (wo is null) return new ApiResponses().NotFoundResult("Work order not found.");
            if (wo.Status is "picked_up" or "cancelled")
                return new ApiResponses().BadRequestResult("This work order is closed.");
            if (await _shop.SetWorkOrderDeposit(id, TenantId, req.DepositCents) == 0)
                return new ApiResponses().BadRequestResult("The deposit has already been paid, so it can't be changed now.");
            return new ApiResponses().OkResult();
        }

        // Email the customer a link to pay the deposit online. The link resolves publicly by
        // token on the tenant's own subdomain.
        [HttpPost("WorkOrders/{id:guid}/DepositRequest")]
        public async Task<IActionResult> SendDepositRequest(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var wo = await _shop.GetWorkOrder(id, TenantId);
            if (wo is null) return new ApiResponses().NotFoundResult("Work order not found.");
            if (wo.Status is "picked_up" or "cancelled")
                return new ApiResponses().BadRequestResult("This work order is closed.");
            if (wo.DepositCents < 50)
                return new ApiResponses().BadRequestResult("Set a deposit of at least 50 cents first.");
            if (wo.DepositPaidAt is not null)
                return new ApiResponses().BadRequestResult("The deposit has already been paid.");
            if (string.IsNullOrWhiteSpace(wo.CustomerEmail))
                return new ApiResponses().BadRequestResult("This work order has no customer email. Add one first.");
            if (!_emailer.IsConfigured)
                return new ApiResponses().BadRequestResult("Email isn't set up on this server.");

            var tenant = _tenantContext.Tenant;
            var apex = _config["App:RootDomain"] ?? "ridepass.io";
            var link = $"https://{tenant.Subdomain}.{apex}/PayDeposit/{wo.DepositRequestToken}";
            var bike = wo.CustomerBikeDesc ?? "your bike";
            static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);
            var html =
                $"<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:480px\">" +
                $"<h2 style=\"margin:0 0 8px\">{Enc(tenant.DisplayName)}</h2>" +
                $"<p>Hi {Enc(wo.CustomerName)},</p>" +
                $"<p>We're ready to get started on {Enc(bike)}. To confirm the job, please pay the " +
                $"<strong>{ShopMoney(wo.DepositCents)}</strong> deposit:</p>" +
                $"<p style=\"margin:16px 0\"><a href=\"{link}\" style=\"background:#1976d2;color:#fff;padding:10px 18px;" +
                $"border-radius:6px;text-decoration:none\">Pay deposit</a></p>" +
                $"<p style=\"font-size:12px;color:#666\">Or paste this link into your browser:<br/>{link}</p></div>";
            if (!await _emailer.Send(wo.CustomerEmail!, $"{tenant.DisplayName}: deposit for your service order",
                    html, null, Services.Email.TenantEmailIdentity.For(tenant)))
                return new ApiResponses().BadRequestResult("Could not send the email. Check the address and try again.");

            await _shop.MarkWorkOrderDepositRequestSent(id, TenantId);
            return new ApiResponses().OkResult();
        }

        // Customer paid the deposit in person; record it and book the ledger entry (cash sits in
        // the till, so the tenant owes us only our cut; same convention as every cash sale path).
        [HttpPost("WorkOrders/{id:guid}/DepositCash")]
        public async Task<IActionResult> RecordCashDeposit(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var wo = await _shop.GetWorkOrder(id, TenantId);
            if (wo is null) return new ApiResponses().NotFoundResult("Work order not found.");
            if (wo.Status is "picked_up" or "cancelled")
                return new ApiResponses().BadRequestResult("This work order is closed.");
            if (wo.DepositCents <= 0)
                return new ApiResponses().BadRequestResult("Set a deposit amount first.");
            if (!await _shop.TryMarkWorkOrderDepositPaid(id, TenantId, "cash"))
                return new ApiResponses().BadRequestResult("The deposit has already been paid.");

            try
            {
                var calc = await _feeCalculator.Calculate(TenantId, wo.DepositCents, 0, 0, DateTime.UtcNow);
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = TenantId,
                    EntryKind = "sale",
                    SourceKind = "shop_wo_deposit",
                    SourceId = id,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = wo.DepositCents,
                    StripeFeeCents = 0,
                    RidepassCutCents = calc.RidepassCutCents,
                    NetToTenantCents = -calc.RidepassCutCents,
                    PaymentMethod = "cash",
                    SoldByUserId = UserId,
                    Memo = "Bike shop repair deposit, cash",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505") { /* idempotent */ }
            return new ApiResponses().OkResult();
        }

        // Give back whatever part of the deposit the customer can still claim: the whole thing
        // before billing, the unapplied overage after a bill it didn't fully cover, or the
        // applied portion once that sale has itself been refunded. Card money returns through
        // Stripe on whichever account captured it; cash comes out of the drawer.
        [Authorize(Policy = TenantPermissions.Policy.SalesRefund)]
        [HttpPost("WorkOrders/{id:guid}/DepositRefund")]
        public async Task<IActionResult> RefundDeposit(Guid id, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var wo = await _shop.GetWorkOrder(id, TenantId);
            if (wo is null) return new ApiResponses().NotFoundResult("Work order not found.");
            if (wo.DepositPaidAt is null) return new ApiResponses().BadRequestResult("No paid deposit to refund.");

            // The portion applied to a live (unrefunded) bill isn't refundable here; it already
            // paid for goods. Refund that sale first and this frees up.
            var appliedLive = 0;
            if (wo.SaleId is not null)
            {
                var sale = await _shop.GetSale(wo.SaleId.Value, TenantId);
                if (sale is not null && sale.Status is "pending" or "paid")
                    appliedLive = sale.DepositAppliedCents;
            }
            var refundable = wo.DepositCents - wo.DepositRefundedCents - appliedLive;
            if (refundable <= 0)
                return new ApiResponses().BadRequestResult(appliedLive > 0
                    ? "The deposit was applied to the bill. Refund the sale first."
                    : "Nothing is left to refund on this deposit.");

            if (wo.DepositPaymentMethod is "stripe" or "stripe_direct" && !string.IsNullOrEmpty(wo.DepositPiId))
            {
                var isDirect = wo.DepositPaymentMethod == "stripe_direct";
                try
                {
                    // Keyed on the running refunded count so a double-click retries the same
                    // partial refund instead of issuing a second one.
                    await _payments.RefundAsync(wo.DepositPiId!, refundable,
                        idempotencyKey: $"shop_wo_dep_refund_{wo.Id}_{wo.DepositRefundedCents}",
                        connectedAccountId: isDirect ? wo.DepositStripeAccountId : null,
                        refundApplicationFee: isDirect, ct: ct);
                }
                catch (Exception ex)
                {
                    return new ApiResponses().BadRequestResult($"Could not refund the card: {ex.Message}");
                }
            }

            if (!await _shop.TryAddWorkOrderDepositRefund(id, TenantId, refundable, wo.DepositRefundedCents))
                return new ApiResponses().BadRequestResult("The deposit changed. Reload and try again.");
            await WriteDepositRefundLedger(wo, refundable);
            return new ApiResponses().OkResult(new
            {
                refundedCents = refundable,
                cash = wo.DepositPaymentMethod == "cash",
            });
        }

        // Proportional negative mirror of the deposit's sale entry, sized to the part being
        // returned, so payouts net correctly across partial refunds (the excess at pickup and a
        // later remainder each get their own entry; Script0193 carved this source out of the
        // one-refund-per-source index).
        private async Task WriteDepositRefundLedger(ShopWorkOrder wo, int cents)
        {
            var entry = await _ledger.GetSaleEntryForSource(TenantId, "shop_wo_deposit", wo.Id);
            if (entry is null || entry.GrossCents <= 0) return;
            long Part(long v) => -(v * cents / entry.GrossCents);
            try
            {
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = TenantId,
                    EntryKind = "refund",
                    SourceKind = "shop_wo_deposit",
                    SourceId = wo.Id,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = (int)Part(entry.GrossCents),
                    StripeFeeCents = (int)Part(entry.StripeFeeCents),
                    RidepassCutCents = (int)Part(entry.RidepassCutCents),
                    NetToTenantCents = (int)Part(entry.NetToTenantCents),
                    StripePaymentIntentId = entry.StripePaymentIntentId,
                    PaymentMethod = wo.DepositPaymentMethod,
                    SoldByUserId = UserId,
                    Memo = cents >= wo.DepositCents ? "Bike shop repair deposit refund" : "Bike shop repair deposit partial refund",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505") { /* idempotent */ }
        }

        private static string ShopMoney(int cents) => "$" + (cents / 100m).ToString("0.00");

        // Bill the job out as a shop sale (parts taxed by their category, labor untaxed for now).
        // Cash settles at the counter; card returns a client secret and the finalizer completes it,
        // flipping the work order to picked_up either way.
        [HttpPost("WorkOrders/{id:guid}/Bill")]
        public async Task<IActionResult> Bill(Guid id, [FromBody] BillShopWorkOrderRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var wo = await _shop.GetWorkOrder(id, TenantId);
            if (wo is null) return new ApiResponses().NotFoundResult("Work order not found.");
            if (wo.Status is "picked_up" or "cancelled" or "estimate")
                return new ApiResponses().BadRequestResult(
                    wo.Status == "estimate" ? "Accept the estimate before billing it." : "This work order is closed.");
            // Declined lines are for the record only: they don't bill and weren't consumed.
            var billableLines = wo.Lines.Where(l => l.ApprovalStatus != "declined").ToList();
            if (billableLines.Count == 0)
                return new ApiResponses().BadRequestResult(
                    wo.Lines.Count == 0 ? "There's nothing on this work order to bill."
                    : "Every line was declined, so there's nothing to bill.");
            if (wo.SaleId is not null)
            {
                var prior = await _shop.GetSale(wo.SaleId.Value, TenantId);
                if (prior is not null && prior.Status is "pending" or "paid")
                    return new ApiResponses().BadRequestResult("This work order already has a sale in flight.");
            }

            // Tax: parts by their product's category (resolved like the register); labor untaxed.
            var partInfos = (await _shop.GetVariantsForSale(
                    billableLines.Where(l => l.VariantId is not null).Select(l => l.VariantId!.Value), TenantId))
                .ToDictionary(v => v.Id);
            int subtotal = 0, taxTotal = 0;
            var saleLines = new List<ShopSaleLine>();
            foreach (var l in billableLines)
            {
                var info = l.VariantId is not null ? partInfos.GetValueOrDefault(l.VariantId.Value) : null;
                var lineBase = l.UnitPriceCents * l.Quantity;
                var rate = info?.TaxRateBps ?? 0;
                var lineTax = rate > 0 ? (int)Math.Round(lineBase * rate / 10000.0, MidpointRounding.AwayFromZero) : 0;
                subtotal += lineBase;
                taxTotal += lineTax;
                saleLines.Add(new ShopSaleLine
                {
                    VariantId = l.VariantId,   // null for labor (not a catalog item)
                    Quantity = l.Quantity,
                    NameSnapshot = l.LineKind == "labor" ? (l.Description ?? "Labor") : (l.Description ?? "Part"),
                    UnitPriceCents = l.UnitPriceCents,
                    TaxCents = lineTax,
                    TaxRateBps = rate,
                    UnitCostCentsFrozen = info?.CostCents,   // labor lines carry no cost (pure margin)
                });
            }

            // Shop supply fee: a percentage of LABOR only, capped, and shown as its own line so
            // the customer can see what it is rather than finding it buried in a labor rate.
            // Untaxed, matching how labor is treated above.
            var shopTenant = _tenantContext.Tenant;
            if (shopTenant.ShopSupplyFeeBps > 0)
            {
                var laborSubtotal = billableLines
                    .Where(l => l.LineKind == "labor")
                    .Sum(l => l.UnitPriceCents * l.Quantity);
                var fee = (int)Math.Round(laborSubtotal * shopTenant.ShopSupplyFeeBps / 10000.0,
                    MidpointRounding.AwayFromZero);
                if (shopTenant.ShopSupplyFeeCapCents is int cap) fee = Math.Min(fee, cap);
                if (fee > 0)
                {
                    subtotal += fee;
                    saleLines.Add(new ShopSaleLine
                    {
                        VariantId = null,          // not a catalog item, same as labor
                        Quantity = 1,
                        NameSnapshot = shopTenant.ShopSupplyFeeLabel,
                        UnitPriceCents = fee,
                        TaxCents = 0,
                        TaxRateBps = 0,
                        UnitCostCentsFrozen = null,   // pure margin, like labor
                    });
                }
            }

            // Season pass holder perk. 'retail' is the right surface for the same reason the staff
            // discount uses 'shop_sale': a repair bills out as a shop sale, so a perk that covers
            // the bike shop covers work done on a bike too. Applies to labour and the supply fee as
            // well as parts, matching the staff discount below rather than inventing a second rule
            // (if a track ever wants labour carved out, this and the staff spread are the two
            // places, and it should be a decision rather than a difference nobody chose).
            var perk = await _perks.Resolve(
                wo.CustomerUserId, _tenantContext.Tenant, "retail", subtotal, DateTime.UtcNow);
            var benefitDiscount = perk.DiscountCents;

            // Staff-applied discount. Scoped to 'shop_sale' because that is what a repair bills
            // out as, so one "VMBA 15% off bike shop" covers a repair as well as a set of grips.
            Services.Repositories.Data.DiscountData.DiscountPreset? staffDiscount = null;
            var staffDiscountCents = 0;
            Guid? discountAuthorizedBy = null;
            if (req.DiscountPresetId is Guid presetId)
            {
                staffDiscount = await _discounts.Get(presetId, TenantId);
                if (staffDiscount is null || !staffDiscount.IsActive)
                    return new ApiResponses().BadRequestResult("That discount isn't available.");
                if (!staffDiscount.AppliesTo(Services.Repositories.Data.DiscountData.DiscountSurfaces.ShopSale))
                    return new ApiResponses().BadRequestResult(
                        $"\"{staffDiscount.Name}\" doesn't apply to bike shop work.");
                if (staffDiscount.RequiresManager)
                {
                    if (UserId is not Guid staffUserId)
                        return new ApiResponses().BadRequestResult("Not signed in.");
                    var pin = await _managerPin.VerifyAsync(TenantId, staffUserId, req.ManagerPin);
                    if (!pin.Authorized)
                        return new ApiResponses().BadRequestResult(
                            pin.Error ?? $"A manager PIN is required to apply \"{staffDiscount.Name}\".");
                    discountAuthorizedBy = pin.AuthorizedUserId;
                }
                staffDiscountCents = staffDiscount.DiscountFor(Math.Max(0, subtotal - benefitDiscount));
            }

            // A repair can now carry two discounts (the holder perk and a staff discount), so this
            // goes through the shared stacking policy like the register: with stacking off exactly
            // one survives and it is the larger, so the customer still gets the best deal going.
            var stacked = Services.Discounts.DiscountStacking.Resolve(
                benefitDiscount, staffDiscountCents, 0, _tenantContext.Tenant.AllowDiscountStacking);
            benefitDiscount = stacked.BenefitCents;
            staffDiscountCents = stacked.StaffCents;
            // Cleared so nothing downstream records a discount that wasn't given: a dropped staff
            // discount must not be snapshotted on the sale or sent for review as though it applied.
            if (staffDiscountCents == 0) { staffDiscount = null; discountAuthorizedBy = null; }
            var discountTotal = Math.Min(subtotal, benefitDiscount + staffDiscountCents);

            // Spread it across the billable lines and recompute tax on the net, exactly as the
            // register does. Leaving tax on the gross would overcharge the customer on a discounted
            // repair, which is the sort of error nobody spots until an audit.
            if (discountTotal > 0 && subtotal > 0)
            {
                var handedOut = 0;
                for (var i = 0; i < saleLines.Count; i++)
                {
                    var l = saleLines[i];
                    var lineBase = l.UnitPriceCents * l.Quantity;
                    l.DiscountCents = i == saleLines.Count - 1
                        ? discountTotal - handedOut
                        : (int)((long)discountTotal * lineBase / subtotal);
                    handedOut += l.DiscountCents;
                }
                var recomputed = 0;
                foreach (var l in saleLines)
                {
                    var net = Math.Max(0, l.UnitPriceCents * l.Quantity - l.DiscountCents);
                    l.TaxCents = l.TaxRateBps > 0
                        ? (int)Math.Round(net * l.TaxRateBps / 10000.0, MidpointRounding.AwayFromZero)
                        : 0;
                    recomputed += l.TaxCents;
                }
                taxTotal = recomputed;
            }

            // A repair bills out as a shop_sale, so it carries the platform charge like any other
            // shop sale, computed by the same ServiceChargeSplit. That INCLUDES labour lines. If a
            // track should not owe a charge on labour, this is the one place to carve it out
            // (the sale is tagged with WorkOrderId below), but it should be a decision rather than
            // an accident of which controller happened to build the sale.
            var billingTenant = _tenantContext.Tenant;
            // Charged on the DISCOUNTED subtotal: the platform's cut follows what the track
            // actually collected, not what it would have collected at full price.
            var netSubtotal = Math.Max(0, subtotal - discountTotal);
            var (shopServiceCharge, buyerFee) = Services.Payments.ServiceChargeSplit.Compute(
                netSubtotal, billingTenant.ServiceChargeBps, billingTenant.ShopBuyerPaidServiceChargeBps);

            // Tax on the buyer's share of the fee, at the tenant's default category rate. Only
            // queried when there is actually a fee to tax, so the default configuration (fee 0)
            // pays nothing for this. See Services.Payments.ShopFeeTax.
            var feeTaxCents = 0;
            if (buyerFee > 0 && _tenantContext.Tenant.ShopTaxServiceChargeTaxable)
            {
                var defaultRate = (await _shop.ListTaxCategories(TenantId, activeOnly: true))
                    .FirstOrDefault(c => c.IsDefault)?.RateBps;
                feeTaxCents = Services.Payments.ShopFeeTax.Compute(
                    // This path prices tax-EXCLUSIVE (the sale is written with
                    // PricesIncludeTax false), so the fee's tax is added rather than extracted.
                    buyerFee, taxable: true, defaultRate, pricesIncludeTax: false);
                taxTotal += feeTaxCents;
            }

            var total = netSubtotal + buyerFee + taxTotal;
            // A paid deposit prepays part of the job: the payment collects the remainder and the
            // ledger books only that (the deposit has its own entry). What's still available on
            // the deposit is what was paid minus anything already refunded/credited back.
            var availableDeposit = wo.DepositPaidAt is not null ? wo.DepositCents - wo.DepositRefundedCents : 0;
            var depositCredit = Math.Min(availableDeposit, total);
            var due = total - depositCredit;

            // ── Deposit exceeds the bill: settle the overage now, the cashier's way ────
            // (Industry pattern: the overage is surfaced and explicitly dispatched, never
            // silently stranded. 'refund' sends it back — Stripe partial refund for a card
            // deposit, from the drawer for cash; 'credit' keeps it as store credit.)
            var depositExcess = availableDeposit - depositCredit;
            var excessHandled = "";
            if (depositExcess > 0)
            {
                if (req.ExcessAction is null)
                    return new ApiResponses().BadRequestResult(
                        $"The deposit exceeds this bill by {ShopMoney(depositExcess)}. Choose whether to refund it or keep it as store credit.");
                if (req.ExcessAction == "credit")
                {
                    var account = await _credit.GetOrCreateAccount(TenantId,
                        wo.CustomerUserId, wo.CustomerEmail, wo.CustomerPhone, wo.CustomerName);
                    if (account is null)
                        return new ApiResponses().BadRequestResult(
                            "Keeping the overage as credit needs a customer email or phone on the work order.");
                    if (!await _shop.TryAddWorkOrderDepositRefund(id, TenantId, depositExcess, wo.DepositRefundedCents))
                        return new ApiResponses().BadRequestResult("The deposit changed. Reload and try again.");
                    await _credit.TryAdjust(account.Id, TenantId, depositExcess, "deposit_excess",
                        "shop_work_order", id, "Deposit exceeded the final bill", UserId);
                    // Money stays with the tenant, so no ledger movement; the liability moved
                    // from the deposit onto the credit account.
                    excessHandled = "credit";
                }
                else
                {
                    if (wo.DepositPaymentMethod is "stripe" or "stripe_direct" && !string.IsNullOrEmpty(wo.DepositPiId))
                    {
                        var depDirect = wo.DepositPaymentMethod == "stripe_direct";
                        try
                        {
                            await _payments.RefundAsync(wo.DepositPiId!, depositExcess,
                                idempotencyKey: $"shop_wo_dep_excess_{wo.Id}_{wo.DepositRefundedCents}",
                                connectedAccountId: depDirect ? wo.DepositStripeAccountId : null,
                                refundApplicationFee: depDirect, ct: ct);
                        }
                        catch (Exception ex)
                        {
                            return new ApiResponses().BadRequestResult($"Could not refund the deposit overage: {ex.Message}");
                        }
                    }
                    if (!await _shop.TryAddWorkOrderDepositRefund(id, TenantId, depositExcess, wo.DepositRefundedCents))
                        return new ApiResponses().BadRequestResult("The deposit changed. Reload and try again.");
                    await WriteDepositRefundLedger(wo, depositExcess);
                    excessHandled = "refund";
                }
            }
            var isCard = req.PaymentMethod == "card" && due > 0;
            var tenant = _tenantContext.Tenant;
            if (isCard)
            {
                if (due < 50) return new ApiResponses().BadRequestResult(
                    "Less than 50 cents is left after the deposit. Settle it as cash.");
                if (tenant.StripeChargeMode == "direct" && string.IsNullOrEmpty(tenant.StripeConnectAccountId))
                    return new ApiResponses().BadRequestResult("This track charges on its own Stripe account but hasn't connected one yet.");
            }

            var sale = new ShopSale
            {
                TenantId = TenantId,
                BuyerUserId = wo.CustomerUserId,
                BuyerName = wo.CustomerName,
                BuyerEmail = wo.CustomerEmail,
                Status = "pending",
                SubtotalCents = subtotal,
                DiscountCents = discountTotal,
                DiscountPresetId = staffDiscount?.Id,
                DiscountLabel = Services.Pricing.SeasonPassPerk.LabelFor(
                    perk, benefitDiscount, staffDiscount?.Name, staffDiscountCents),
                DiscountAuthorizedByUserId = discountAuthorizedBy,
                TaxCents = taxTotal,
                TotalCents = total,
                ServiceChargeCents = shopServiceCharge,
                DepositAppliedCents = depositCredit,
                PaymentMethod = isCard ? "stripe" : "cash",
                SoldByUserId = UserId,
                WorkOrderId = id,   // marks the sale so depletion skips it (parts consumed on the bench)
            };
            var (saleId, receipt) = await _shop.CreateSale(sale, saleLines);
            await _shop.SetWorkOrderSale(id, TenantId, saleId);

            // Same review surface as the register's discounts and every refund: money off with
            // nothing the customer had to produce.
            if (staffDiscount is not null && staffDiscountCents > 0)
            {
                await _audit.Log(
                    "shop.discount_applied",
                    $"Applied \"{staffDiscount.Name}\" to a repair bill, taking off ${staffDiscountCents / 100m:0.00}",
                    targetKind: "shop_sale",
                    targetId: saleId,
                    tenantId: TenantId,
                    metadata: new
                    {
                        discountName = staffDiscount.Name,
                        discountPresetId = staffDiscount.Id,
                        discountCents = staffDiscountCents,
                        subtotalCents = subtotal,
                        workOrderId = id,
                        requiredManager = staffDiscount.RequiresManager,
                        authorizedByUserId = discountAuthorizedBy,
                    });
            }

            if (!isCard)
            {
                if (await _shop.TryMarkSalePaid(saleId, TenantId))
                {
                    var orderNumber = await _shop.NextOrderNumber(TenantId);
                    await _shop.SetSaleOrderNumber(saleId, orderNumber);
                    if (due > 0) await WriteCashLedger(saleId, due);
                    await _shop.MarkWorkOrderPickedUpBySale(saleId);
                    return new ApiResponses().OkResult(new { saleId, receiptToken = receipt, status = "paid", orderNumber,
                        totalCents = total, depositAppliedCents = depositCredit, dueCents = due,
                        depositExcessCents = depositExcess, excessAction = excessHandled,
                        depositWasCash = wo.DepositPaymentMethod == "cash" });
                }
                return new ApiResponses().OkResult(new { saleId, receiptToken = receipt, status = "paid",
                    totalCents = total, depositAppliedCents = depositCredit, dueCents = due,
                    depositExcessCents = depositExcess, excessAction = excessHandled,
                    depositWasCash = wo.DepositPaymentMethod == "cash" });
            }

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = TenantId.ToString(),
                ["sale_kind"] = "shop_sale",
                ["shop_sale_id"] = saleId.ToString(),
                ["shop_work_order_id"] = id.ToString(),
            };
            PaymentIntentCreated intent;
            ChargePlan plan;
            try
            {
                // Same as the register and the online store: in direct mode the application fee is
                // how RidePass collects, so route the charge snapshotted on the bill-out sale.
                // Clamped to `due` inside ChargeRouter, which matters here because a paid deposit
                // can leave very little still owing.
                plan = _chargeRouter.Plan(tenant, serviceFeeCents: shopServiceCharge, chargeAmountCents: due);
                intent = await _payments.CreatePaymentIntentAsync(due, "usd", metadata, sale.BuyerEmail,
                    connectedAccountId: plan.ConnectedAccountId, applicationFeeCents: plan.ApplicationFeeCents, ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                await _shop.MarkSaleFailed(saleId);
                return new ApiResponses().BadRequestResult(ex.Message);
            }
            await _shop.SetSalePaymentIntent(saleId, intent.IntentId);
            if (plan.IsDirect) await _shop.MarkSaleDirectCharge(saleId, TenantId, plan.ConnectedAccountId!);
            return new ApiResponses().OkResult(new
            {
                saleId, receiptToken = receipt, status = "pending", clientSecret = intent.ClientSecret,
                totalCents = total, depositAppliedCents = depositCredit, dueCents = due,
            });
        }

        private async Task WriteCashLedger(Guid saleId, int totalCents)
        {
            try
            {
                var calc = await _feeCalculator.Calculate(TenantId, totalCents, 0, 0, DateTime.UtcNow);
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = TenantId,
                    EntryKind = "sale",
                    SourceKind = "shop_sale",
                    SourceId = saleId,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = totalCents,
                    StripeFeeCents = 0,
                    RidepassCutCents = calc.RidepassCutCents,
                    NetToTenantCents = -calc.RidepassCutCents,
                    PaymentMethod = "cash",
                    SoldByUserId = UserId,
                    Memo = "Bike shop repair, cash",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505") { /* idempotent */ }
        }
    
        // ── Customer bikes ────────────────────────────────────────────────────
        // Intake looks a serial up BEFORE creating anything. Three outcomes, and the counter needs
        // to tell them apart:
        //   1. We already have this bike  -> reuse it, and show its repair history.
        //   2. We sold a unit with this serial but never serviced it -> prefill from the sale.
        //   3. Unknown -> a blank bike for staff to fill in.
        [HttpGet("Bikes/Lookup")]
        public async Task<IActionResult> LookupBike([FromQuery] string serial)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (string.IsNullOrWhiteSpace(serial))
                return new ApiResponses().BadRequestResult("Enter a serial number to look up.");

            var existing = await _shop.FindCustomerBikeBySerial(serial, TenantId);
            if (existing is not null)
            {
                var history = await _shop.ListBikeHistory(existing.Id, TenantId);
                return new ApiResponses().OkResult(new
                {
                    match = "known_bike",
                    bike = existing,
                    displayName = existing.DisplayName,
                    history,
                });
            }

            var sold = await _shop.FindSoldUnitBySerial(serial, TenantId);
            if (sold is not null)
            {
                return new ApiResponses().OkResult(new
                {
                    match = "sold_by_us",
                    // Not persisted yet: a suggestion the counter can accept or edit.
                    suggestion = new
                    {
                        serial = sold.Serial,
                        brand = sold.Brand,
                        model = sold.Model,
                        soldItemId = sold.ItemId,
                        customerUserId = sold.BuyerUserId,
                        customerName = sold.BuyerName,
                    },
                    soldAt = sold.SoldAt,
                    history = Array.Empty<object>(),
                });
            }

            return new ApiResponses().OkResult(new { match = "unknown", history = Array.Empty<object>() });
        }

        /// <summary>Bikes already on file for a customer (by account, or by phone for walk-ins).</summary>
        [HttpGet("Bikes")]
        public async Task<IActionResult> ListBikes([FromQuery] Guid? customerUserId, [FromQuery] string? phone)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var bikes = await _shop.ListCustomerBikes(TenantId, customerUserId, phone);
            return new ApiResponses().OkResult(bikes.Select(b => new
            {
                b.Id, b.Serial, b.Brand, b.Model, b.ModelYear, b.Color, b.Size, b.Notes,
                b.CustomerName, b.CustomerPhone, displayName = b.DisplayName,
            }));
        }

        [HttpGet("Bikes/{id:guid}/History")]
        public async Task<IActionResult> BikeHistory(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var bike = await _shop.GetCustomerBike(id, TenantId);
            if (bike is null) return new ApiResponses().NotFoundResult("Bike not found.");
            return new ApiResponses().OkResult(await _shop.ListBikeHistory(id, TenantId));
        }

        /// <summary>
        /// Create or update a bike. Find-or-create on serial so two counter staff taking the same
        /// bike in can't produce two records: a serial that already exists updates that row rather
        /// than colliding with the unique index.
        /// </summary>
        [HttpPost("Bikes")]
        public async Task<IActionResult> UpsertBike([FromBody] UpsertShopCustomerBikeRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var serial = string.IsNullOrWhiteSpace(req.Serial) ? null : req.Serial.Trim();
            ShopCustomerBike? target = req.Id is Guid id ? await _shop.GetCustomerBike(id, TenantId) : null;
            if (target is null && serial is not null)
                target = await _shop.FindCustomerBikeBySerial(serial, TenantId);

            if (target is null)
            {
                var created = new ShopCustomerBike
                {
                    TenantId = TenantId,
                    CustomerUserId = req.CustomerUserId,
                    CustomerName = Blank(req.CustomerName),
                    CustomerPhone = Blank(req.CustomerPhone),
                    Serial = serial,
                    Brand = Blank(req.Brand),
                    Model = Blank(req.Model),
                    ModelYear = req.ModelYear,
                    Color = Blank(req.Color),
                    Size = Blank(req.Size),
                    Notes = Blank(req.Notes),
                    SoldItemId = req.SoldItemId,
                };
                created.Id = await _shop.CreateCustomerBike(created);
                return new ApiResponses().OkResult(new { created.Id, displayName = created.DisplayName, created = true });
            }

            // A bike that changed hands keeps its history: update the owner in place rather than
            // forking a second record for the same physical bike.
            target.CustomerUserId = req.CustomerUserId ?? target.CustomerUserId;
            target.CustomerName = Blank(req.CustomerName) ?? target.CustomerName;
            target.CustomerPhone = Blank(req.CustomerPhone) ?? target.CustomerPhone;
            target.Serial = serial ?? target.Serial;
            target.Brand = Blank(req.Brand) ?? target.Brand;
            target.Model = Blank(req.Model) ?? target.Model;
            target.ModelYear = req.ModelYear ?? target.ModelYear;
            target.Color = Blank(req.Color) ?? target.Color;
            target.Size = Blank(req.Size) ?? target.Size;
            target.Notes = Blank(req.Notes) ?? target.Notes;
            target.SoldItemId = req.SoldItemId ?? target.SoldItemId;
            await _shop.UpdateCustomerBike(target);
            return new ApiResponses().OkResult(new { target.Id, displayName = target.DisplayName, created = false });
        }

        private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
}
