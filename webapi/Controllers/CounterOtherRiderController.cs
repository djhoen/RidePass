using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.RiderData;
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
    /// for a minor a link to their parent / guardian's account), the ticket names that rider so the gate
    /// keys its per-rider checks on them, and the waiver is collected per rider: signed on this device or
    /// by a link sent to the guardian (minor) or the rider (adult), by email and text when SMS is set up.
    /// The sale itself stays on CounterController; the gate blocks admission until each rider is signed.
    /// </summary>
    [ApiController]
    [Route("api/Counter/OtherRiders")]
    [Authorize(Policy = TenantPermissions.Policy.SalesCounter)]
    public class CounterOtherRiderController : ControllerBase
    {
        private readonly IUserRepository _users;
        private readonly IRiderDependentRepository _dependents;
        private readonly IPasswordHasher<User> _passwordHasher;
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
            IPasswordHasher<User> passwordHasher,
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
            _passwordHasher = passwordHasher;
            _tickets = tickets;
            _waiverGate = waiverGate;
            _signRequests = signRequests;
            _emailer = emailer;
            _sms = sms;
            _config = config;
            _audit = audit;
            _tenantContext = tenantContext;
        }

        private DateOnly TenantToday => TenantClock.LocalDate(_tenantContext.Tenant.Timezone, DateTime.UtcNow);

        /// <summary>
        /// Find or create the profile for a rider who isn't the paying customer. Never creates a second
        /// account for an email that already has one (the rider's or the guardian's): it is reused, and the
        /// signing link later attaches the signature to it without anyone logging in.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Resolve([FromBody] CounterOtherRiderRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;
            var today = TenantToday;

            var payer = BuyerAccounts.ForTenant(await _users.GetById(req.PayerUserId), tenantId);
            if (payer is null) return new ApiResponses().BadRequestResult("Pick the paying customer first.");

            var resp = new CounterOtherRiderResponse();
            User? riderAccount = null;
            string first, last;
            DateOnly? dob = req.Birthdate is { } b ? DateOnly.FromDateTime(b) : null;

            if (req.ExistingDependentId is Guid depId)
            {
                // A child already linked to a guardian here: same row, same rider key.
                var dep = await _dependents.GetById(depId, tenantId);
                if (dep is null) return new ApiResponses().NotFoundResult("That rider isn't on file here.");
                var guardianOfDep = BuyerAccounts.ForTenant(await _users.GetById(dep.GuardianUserId), tenantId);
                if (guardianOfDep is null) return new ApiResponses().NotFoundResult("That rider's guardian account is no longer available.");
                var depDob = GuardianWaiverRule.ToDateOnly(dep.Birthdate) ?? dob;
                if (depDob is null)
                    return new ApiResponses().BadRequestResult("Enter the rider's birthdate: it decides whether a parent or guardian has to sign.");
                if (!CounterRiderRules.IsMinor(depDob.Value, today))
                    return new ApiResponses().BadRequestResult(
                        $"{dep.FirstName} is 18 or over now and signs their own waiver: add them as an adult rider with their own email.");
                if (dep.Birthdate is null)
                    dep = await _dependents.FindOrCreate(tenantId, dep.GuardianUserId, dep.FirstName, dep.LastName,
                        depDob.Value.ToDateTime(TimeOnly.MinValue), dep.RiderUserId, TryGetStaffUserId());
                if (dep.RiderUserId is Guid ownId) riderAccount = await _users.GetById(ownId);
                Fill(resp, dep.FirstName, dep.LastName, depDob, today, riderAccount);
                FillGuardian(resp, guardianOfDep);
                resp.RiderDependentId = dep.Id;
                resp.RiderUserId = dep.RiderUserId;
                return new ApiResponses().OkResult(resp);
            }

            var input = new OtherRiderInput(req.FirstName, req.LastName, dob, req.Email, req.Phone,
                req.GuardianIsPayer, req.GuardianUserId is not null,
                req.GuardianFirstName, req.GuardianLastName, req.GuardianEmail, req.GuardianPhone);

            if (req.ExistingUserId is Guid existingId)
            {
                riderAccount = BuyerAccounts.ForTenant(await _users.GetById(existingId), tenantId);
                if (riderAccount is null) return new ApiResponses().NotFoundResult("That profile isn't available here.");
                if (riderAccount.Id == payer.Id)
                    return new ApiResponses().BadRequestResult("That is the paying customer. Add the ticket for them instead of another rider.");
                // The account's own birthdate is the gate's; the typed one only fills an account that has none.
                dob = AccountWaiverStatus.Birthdate(riderAccount) ?? dob;
                input = input with { Birthdate = dob };
                var invalid = CounterRiderRules.Validate(input, today, existingProfile: true, payer.Email);
                if (invalid is not null) return new ApiResponses().BadRequestResult(invalid);
                if (riderAccount.Birthdate is null && dob is { } fillDob)
                    await _users.UpdateBirthdate(riderAccount.Id, fillDob.ToDateTime(TimeOnly.MinValue));
                first = riderAccount.FirstName;
                last = riderAccount.LastName;
                resp.MatchedExistingRider = true;
            }
            else
            {
                var invalid = CounterRiderRules.Validate(input, today, existingProfile: false, payer.Email);
                if (invalid is not null) return new ApiResponses().BadRequestResult(invalid);
                first = req.FirstName!.Trim();
                last = req.LastName!.Trim();
                var email = req.Email?.Trim();
                if (!string.IsNullOrEmpty(email))
                {
                    var (account, created) = await FindOrCreateAccount(email, first, last, dob, req.Phone);
                    riderAccount = account;
                    resp.MatchedExistingRider = !created;
                    resp.CreatedRider = created;
                    if (riderAccount.Id == payer.Id)
                        return new ApiResponses().BadRequestResult("That is the paying customer. Add the ticket for them instead of another rider.");
                    if (!created)
                    {
                        // An existing account is the rider: its name is what the gate and the waiver use.
                        first = riderAccount.FirstName;
                        last = riderAccount.LastName;
                        dob = AccountWaiverStatus.Birthdate(riderAccount) ?? dob;
                        // The account's birthdate may make them a minor after all: then a guardian is needed.
                        var recheck = CounterRiderRules.Validate(input with { Birthdate = dob }, today,
                            existingProfile: true, payer.Email);
                        if (recheck is not null) return new ApiResponses().BadRequestResult(recheck);
                    }
                }
            }

            var isMinor = dob is { } d && CounterRiderRules.IsMinor(d, today);
            Fill(resp, first, last, dob, today, riderAccount);
            resp.RiderUserId = riderAccount?.Id;
            if (!isMinor)
            {
                await AuditProfile(resp, null);
                return new ApiResponses().OkResult(resp);
            }

            // A minor: their guardian's account (created from the name / email / phone typed, or reused).
            User? guardian;
            if (req.GuardianIsPayer) guardian = payer;
            else if (req.GuardianUserId is Guid gid)
            {
                guardian = BuyerAccounts.ForTenant(await _users.GetById(gid), tenantId);
                if (guardian is null) return new ApiResponses().NotFoundResult("That guardian profile isn't available here.");
                resp.MatchedExistingGuardian = true;
            }
            else
            {
                // Validate (above) has already required the guardian's name and a valid email.
                var (account, created) = await FindOrCreateAccount(req.GuardianEmail!.Trim(),
                    req.GuardianFirstName!.Trim(), req.GuardianLastName!.Trim(), null, req.GuardianPhone);
                guardian = account;
                resp.MatchedExistingGuardian = !created;
                resp.CreatedGuardian = created;
            }
            if (riderAccount is not null && guardian.Id == riderAccount.Id)
                return new ApiResponses().BadRequestResult("The guardian can't be the rider. Enter the parent or guardian's own details.");

            var dependent = await _dependents.FindOrCreate(tenantId, guardian.Id, first, last,
                dob?.ToDateTime(TimeOnly.MinValue), riderAccount?.Id, TryGetStaffUserId());
            resp.RiderDependentId = dependent.Id;
            FillGuardian(resp, guardian);
            await AuditProfile(resp, guardian);
            return new ApiResponses().OkResult(resp);
        }

        /// <summary>The minors already linked to this guardian here (a parent buying for their own kids).</summary>
        [HttpGet("Dependents")]
        public async Task<IActionResult> Dependents([FromQuery] Guid guardianUserId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var today = TenantToday;
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
        /// stacking new ones. Deliver = email it, and text it when the venue has SMS; otherwise the counter
        /// shows the link and a QR (or opens it to sign on this device).
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
            User? recipient;
            if (isMinor)
            {
                var dep = ticket.RiderDependentId is Guid did ? await _dependents.GetById(did, tenantId) : null;
                if (dep is null)
                    return new ApiResponses().BadRequestResult(
                        $"{target.DisplayName} is under 18 and has no parent or guardian on file. Sign on this device with their parent or guardian.");
                recipient = BuyerAccounts.ForTenant(await _users.GetById(dep.GuardianUserId), tenantId);
            }
            else
            {
                recipient = ticket.RiderUserId is Guid ruid
                    ? BuyerAccounts.ForTenant(await _users.GetById(ruid), tenantId) : null;
            }
            var to = CounterRiderRules.LinkRecipient(isMinor, recipient?.Email, recipient?.Phone,
                recipient?.Email, recipient?.Phone);
            if (recipient is null || to is null)
                return new ApiResponses().BadRequestResult(isMinor
                    ? $"There's no email for {target.DisplayName}'s parent or guardian. Sign on this device instead."
                    : $"There's no email for {target.DisplayName}. Sign on this device instead.");
            var recipientName = $"{recipient.FirstName} {recipient.LastName}".Trim();

            var latest = (await _signRequests.LatestForTickets(tenantId, new[] { ticket.Id })).FirstOrDefault();
            Services.Repositories.Data.WaiverData.WaiverSignRequestRow row;
            if (latest is { Status: "pending" or "sent" or "opened" } open
                && open.WaiverId == decision.RequiredWaiverId
                && string.Equals(open.RecipientEmail, to.Email, StringComparison.OrdinalIgnoreCase))
            {
                row = open;
                resp.Reused = true;
            }
            else
            {
                row = await _signRequests.Create(tenantId, decision.RequiredWaiverId, WaiverSignRequestMail.NewToken(),
                    to.Email, recipientName, target.EventId, TryGetStaffUserId(), ticket.Id, null, null, recipient.Id);
            }

            resp.RequestId = row.Id;
            resp.Link = WaiverSignRequestMail.BuildSigningLink(_config, tenant, row.Token);
            resp.SignAs = isMinor ? "guardian" : "adult";
            resp.RecipientName = recipientName;
            resp.RecipientEmail = to.Email;
            resp.RecipientPhone = to.Phone;
            resp.Status = CounterRiderRules.StatusLinkSent;

            if (req.Deliver)
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

        /// <summary>Each ticket's waiver as the gate will judge it (the counter polls this).</summary>
        [HttpPost("WaiverStatus")]
        public async Task<IActionResult> WaiverStatus([FromBody] CounterRiderWaiverStatusRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;
            var ids = (req.PurchaseIds ?? new List<Guid>()).Distinct().Take(50).ToList();

            var tickets = new List<Services.Repositories.Data.PaymentData.EventTicketPurchase>();
            foreach (var id in ids)
                if (await _tickets.GetById(id, tenantId) is { } t) tickets.Add(t);
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
                    RecipientEmail = r?.RecipientEmail,
                    Link = open ? WaiverSignRequestMail.BuildSigningLink(_config, _tenantContext.Tenant, r!.Token) : null,
                });
            }
            return new ApiResponses().OkResult(items);
        }

        // ── helpers ────────────────────────────────────────────────────────────────────────────

        /// <summary>The platform account for this email (a global rider, or one of this tenant's own), or a
        /// new global rider account with an unguessable password they claim later by reset.</summary>
        private async Task<(User Account, bool Created)> FindOrCreateAccount(string email, string first, string last,
            DateOnly? birthdate, string? phone)
        {
            var tenantId = _tenantContext.TenantId;
            var existing = await _users.GetGlobalByEmail(email) ?? await _users.GetByEmail(tenantId, email);
            if (existing is not null)
            {
                // Fill only what the account is missing; never overwrite what the person entered themselves.
                if (string.IsNullOrWhiteSpace(existing.Phone) && CounterRiderRules.PhoneDigits(phone) >= 7)
                    await _users.UpdatePhone(existing.Id, phone!.Trim());
                if (existing.Birthdate is null && birthdate is { } bd)
                    await _users.UpdateBirthdate(existing.Id, bd.ToDateTime(TimeOnly.MinValue));
                return ((await _users.GetById(existing.Id))!, false);
            }

            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            var user = new User
            {
                TenantId = null,          // riders are global: one account works at every tenant
                Email = email,
                FirstName = first,
                LastName = last,
                Role = "rider",
                Status = "active",
                Phone = CounterRiderRules.PhoneDigits(phone) >= 7 ? phone!.Trim() : null,
                Birthdate = birthdate?.ToDateTime(TimeOnly.MinValue),
            };
            user.PasswordHash = _passwordHasher.HashPassword(user, Convert.ToHexString(bytes));
            try
            {
                user.Id = await _users.Create(user);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                // Created by a concurrent request a moment ago: use that one.
                var raced = await _users.GetGlobalByEmail(email);
                if (raced is null) throw;
                return (raced, false);
            }
            return (user, true);
        }

        private static void Fill(CounterOtherRiderResponse resp, string first, string last, DateOnly? dob,
            DateOnly today, User? account)
        {
            resp.FirstName = first;
            resp.LastName = last;
            resp.Birthdate = dob;
            resp.Age = dob is { } d ? GuardianWaiverRule.AgeOn(d, today) : null;
            resp.IsMinor = dob is { } m && CounterRiderRules.IsMinor(m, today);
            resp.Email = account?.Email;
            resp.Phone = account?.Phone;
        }

        private static void FillGuardian(CounterOtherRiderResponse resp, User guardian)
        {
            resp.GuardianUserId = guardian.Id;
            resp.GuardianName = $"{guardian.FirstName} {guardian.LastName}".Trim();
            resp.GuardianEmail = guardian.Email;
            resp.GuardianPhone = guardian.Phone;
        }

        private Task AuditProfile(CounterOtherRiderResponse resp, User? guardian) =>
            _audit.Log("counter.other_rider_profile",
                $"Gate Sale rider {resp.FirstName} {resp.LastName}"
                    + (guardian is not null ? $" (guardian {guardian.FirstName} {guardian.LastName})" : ""),
                targetKind: "user", targetId: resp.RiderUserId ?? guardian?.Id, tenantId: _tenantContext.TenantId,
                metadata: new
                {
                    resp.RiderUserId, resp.RiderDependentId, resp.GuardianUserId,
                    resp.CreatedRider, resp.MatchedExistingRider, resp.CreatedGuardian, resp.MatchedExistingGuardian,
                });

        private Guid? TryGetStaffUserId() =>
            Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;
    }
}
