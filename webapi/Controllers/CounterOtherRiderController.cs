using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;
using Services.Riders;
using Services.Waivers;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Counter;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Gate Sale "another rider" lines (Script0309): a payer buys a day pass for a friend, or a son's
    /// friend, in the same payment. Each rider gets their own profile (an account, reused by email, and
    /// for a minor a link to their parent / guardian's account), created when the SALE is made
    /// (CounterController, via ICounterRiderProfiles); here the counter only checks an entry, and later
    /// collects each rider's waiver: signed on this device or by a link sent to the guardian (minor) or
    /// the rider (adult), by email and text when SMS is set up. The gate blocks admission until signed.
    /// </summary>
    [ApiController]
    [Route("api/Counter/OtherRiders")]
    [Authorize(Policy = TenantPermissions.Policy.SalesCounter)]
    public class CounterOtherRiderController : ControllerBase
    {
        private readonly IUserRepository _users;
        private readonly IRiderDependentRepository _dependents;
        private readonly ICounterRiderProfiles _profiles;
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly IWaiverCheckInGate _waiverGate;
        private readonly IWaiverSignRequestRepository _signRequests;
        private readonly ISmtpEmailer _emailer;
        private readonly ISmsSender _sms;
        private readonly IConfiguration _config;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly ITenantContext _tenantContext;

        public CounterOtherRiderController(
            IUserRepository users,
            IRiderDependentRepository dependents,
            ICounterRiderProfiles profiles,
            IEventTicketPurchaseRepository tickets,
            IWaiverCheckInGate waiverGate,
            IWaiverSignRequestRepository signRequests,
            ISmtpEmailer emailer,
            ISmsSender sms,
            IConfiguration config,
            Services.Audit.IAuditLogger audit,
            ITenantContext tenantContext)
        {
            _users = users;
            _dependents = dependents;
            _profiles = profiles;
            _tickets = tickets;
            _waiverGate = waiverGate;
            _signRequests = signRequests;
            _emailer = emailer;
            _sms = sms;
            _config = config;
            _audit = audit;
            _tenantContext = tenantContext;
        }

        /// <summary>
        /// Checks an "another rider" entry before it goes in the cart: validated, existing profiles matched
        /// (an email that already has a rider account is that account; a staff email is refused). Writes
        /// nothing: the sale creates or fills the profiles when it is made, after checking again.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Resolve([FromBody] CounterOtherRiderRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var payer = BuyerAccounts.ForTenant(await _users.GetById(req.PayerUserId), _tenantContext.TenantId);
            if (payer is null) return new ApiResponses().BadRequestResult("Pick the paying customer first.");
            var (plan, error) = await _profiles.Plan(req, payer);
            if (error is not null) return new ApiResponses().BadRequestResult(error);
            return new ApiResponses().OkResult(plan!.ToDraft());
        }

        /// <summary>The minors already linked to this guardian here (a parent buying for their own kids).</summary>
        [HttpGet("Dependents")]
        public async Task<IActionResult> Dependents([FromQuery] Guid guardianUserId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var today = TenantClock.LocalDate(_tenantContext.Tenant.Timezone, DateTime.UtcNow);
            var rows = await _dependents.ListForGuardian(_tenantContext.TenantId, guardianUserId);
            return new ApiResponses().OkResult(rows.Select(r =>
            {
                var bd = GuardianWaiverRule.ToDateOnly(r.Birthdate);
                return new CounterDependentItem
                {
                    Id = r.Id, GuardianUserId = r.GuardianUserId, RiderUserId = r.RiderUserId,
                    FirstName = r.FirstName, LastName = r.LastName, Birthdate = bd,
                    IsMinor = bd is { } x && CounterRiderRules.IsMinor(x, today),
                };
            }).ToList());
        }

        /// <summary>
        /// A signing link for one "another rider" ticket: guardian mode for a minor (sent to the guardian),
        /// adult mode for an adult (sent to the rider). Reuses the open link for the ticket rather than
        /// stacking new ones. Deliver = email it, and text it when the venue has SMS (needs an email on
        /// file). Deliver = false is "sign on this device": no email needed, the counter opens the link.
        /// </summary>
        [HttpPost("WaiverLink")]
        public async Task<IActionResult> WaiverLink([FromBody] CounterRiderWaiverLinkRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;
            var tenant = _tenantContext.Tenant;

            var ticket = await _tickets.GetById(req.PurchaseId, tenantId);
            if (ticket is null) return new ApiResponses().NotFoundResult("Ticket not found.");
            if (ticket.RiderUserId is null && ticket.RiderDependentId is null)
                return new ApiResponses().BadRequestResult("This ticket is the customer's own. Their waiver is signed in the sale's waiver step.");
            if (ticket.Status is not ("pending" or "paid"))
                return new ApiResponses().BadRequestResult($"This ticket is {ticket.Status}.");

            var target = await _waiverGate.ResolveSignTarget(tenantId, ticket.Id, null, null);
            if (target is null) return new ApiResponses().NotFoundResult("Ticket not found.");
            var decision = target.Decision;
            var resp = new CounterRiderWaiverLinkResponse
            {
                PurchaseId = ticket.Id,
                SmsAvailable = _sms.IsConfiguredFor(tenant),
                Status = CounterRiderRules.LineStatus(decision.Required, decision.Satisfied, null),
            };
            if (resp.Status is CounterRiderRules.StatusSigned or CounterRiderRules.StatusNotRequired)
                return new ApiResponses().OkResult(resp);
            if (decision.RegistrationIncomplete || decision.FixTarget is null || decision.RequiredWaiverId is null)
                return new ApiResponses().BadRequestResult($"A signature won't fix this: {decision.BlockReason}");

            // Who signs, and where the link goes: the gate's own age verdict decides.
            var isMinor = decision.IsMinor == true;
            User? recipient = null;
            if (isMinor)
            {
                var dep = ticket.RiderDependentId is Guid did ? await _dependents.GetById(did, tenantId) : null;
                if (dep is not null) recipient = BuyerAccounts.ForTenant(await _users.GetById(dep.GuardianUserId), tenantId);
            }
            else if (ticket.RiderUserId is Guid ruid)
            {
                recipient = BuyerAccounts.ForTenant(await _users.GetById(ruid), tenantId);
            }
            var to = CounterRiderRules.LinkRecipient(isMinor, recipient?.Email, recipient?.Phone,
                recipient?.Email, recipient?.Phone);
            if (req.Deliver && to is null)
                return new ApiResponses().BadRequestResult(isMinor
                    ? $"There's no email for {target.DisplayName}'s parent or guardian. Sign on this device instead."
                    : $"There's no email for {target.DisplayName}. Sign on this device instead.");
            var recipientName = recipient is null ? target.DisplayName : $"{recipient.FirstName} {recipient.LastName}".Trim();
            var recipientEmail = to?.Email ?? string.Empty;   // device signing needs none

            var latest = (await _signRequests.LatestForTickets(tenantId, new[] { ticket.Id })).FirstOrDefault();
            Services.Repositories.Data.WaiverData.WaiverSignRequestRow row;
            if (latest is { Status: "pending" or "sent" or "opened" } open
                && open.WaiverId == decision.RequiredWaiverId
                && (!req.Deliver || string.Equals(open.RecipientEmail, recipientEmail, StringComparison.OrdinalIgnoreCase)))
            {
                row = open;
                resp.Reused = true;
            }
            else
            {
                row = await _signRequests.Create(tenantId, decision.RequiredWaiverId, WaiverSignRequestMail.NewToken(),
                    recipientEmail, recipientName, target.EventId, TryGetStaffUserId(), ticket.Id, null, null, recipient?.Id);
            }

            resp.RequestId = row.Id;
            resp.Link = WaiverSignRequestMail.BuildSigningLink(_config, tenant, row.Token);
            resp.SignAs = isMinor ? "guardian" : "adult";
            resp.RecipientName = recipientName;
            resp.RecipientEmail = string.IsNullOrEmpty(row.RecipientEmail) ? null : row.RecipientEmail;
            resp.RecipientPhone = to?.Phone;
            resp.Status = CounterRiderRules.StatusLinkSent;

            if (req.Deliver && to is not null)
            {
                resp.EmailSent = await WaiverSignRequestMail.TrySend(_emailer, _config, tenant, _signRequests, row);
                if (resp.SmsAvailable && to.Phone is not null)
                {
                    var riderFirst = string.IsNullOrWhiteSpace(target.FirstName) ? target.DisplayName : target.FirstName!;
                    resp.SmsSent = await _sms.Send(tenant, to.Phone,
                        CounterRiderRules.SmsBody(tenant.DisplayName, riderFirst, isMinor, resp.Link), TryGetStaffUserId());
                    if (resp.SmsSent && !resp.EmailSent) await _signRequests.MarkSent(row.Id, tenantId);
                }
                await _audit.Log("counter.other_rider_waiver_link",
                    $"Waiver link for {target.DisplayName} sent to {(isMinor ? "guardian " : "")}{recipientName}",
                    targetKind: "event_ticket_purchase", targetId: ticket.Id, tenantId: tenantId,
                    metadata: new { requestId = row.Id, resp.EmailSent, resp.SmsSent, resp.SignAs, reused = resp.Reused });
            }
            return new ApiResponses().OkResult(resp);
        }

        /// <summary>Each ticket's waiver as the gate will judge it (the counter polls this). One ticket query,
        /// one batched gate evaluation, one link query.</summary>
        [HttpPost("WaiverStatus")]
        public async Task<IActionResult> WaiverStatus([FromBody] CounterRiderWaiverStatusRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;
            var ids = (req.PurchaseIds ?? new List<Guid>()).Distinct().Take(50).ToList();

            var tickets = await _tickets.ListByIds(ids, tenantId);
            var decisions = await _waiverGate.EvaluateTickets(tenantId, tickets);
            var requests = (await _signRequests.LatestForTickets(tenantId, tickets.Select(t => t.Id).ToList()))
                .Where(r => r.EventTicketPurchaseId is not null)
                .ToDictionary(r => r.EventTicketPurchaseId!.Value);

            var items = new List<CounterRiderWaiverStatusItem>();
            for (var i = 0; i < tickets.Count; i++)
            {
                var t = tickets[i];
                var d = decisions[i];
                requests.TryGetValue(t.Id, out var r);
                var open = r is { Status: "pending" or "sent" or "opened" };
                items.Add(new CounterRiderWaiverStatusItem
                {
                    PurchaseId = t.Id,
                    RiderName = d.RiderName,
                    IsMinor = d.IsMinor == true,
                    Status = CounterRiderRules.LineStatus(d.Required, d.Satisfied, r?.Status),
                    SignedByGuardian = d.SignedByGuardian,
                    GuardianName = d.GuardianName,
                    SignerName = d.SignerName,
                    BlockReason = d.BlockReason,
                    RequestStatus = r?.Status,
                    RequestSentAtUtc = r?.SentAt is { } s ? DateTime.SpecifyKind(s, DateTimeKind.Utc) : null,
                    RecipientEmail = string.IsNullOrEmpty(r?.RecipientEmail) ? null : r!.RecipientEmail,
                    Link = open ? WaiverSignRequestMail.BuildSigningLink(_config, _tenantContext.Tenant, r!.Token) : null,
                });
            }
            return new ApiResponses().OkResult(items);
        }

        private Guid? TryGetStaffUserId() =>
            Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;
    }
}
