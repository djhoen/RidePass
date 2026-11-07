using Services.Helpers;
using Services.Repositories.Data.EventData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;

namespace Services.Waivers
{
    /// <summary>
    /// The gate's full waiver verdict for one ticket or pass: whether the rider may be admitted,
    /// and the facts a screen needs to explain it (age on the day, who signed, the guardian's name,
    /// an unknown DOB) and to fix it (which document is missing, and where a new signature goes).
    /// </summary>
    public sealed class WaiverGateDecision
    {
        /// <summary>Why check-in is blocked, or null when it is allowed.</summary>
        public string? BlockReason { get; init; }
        /// <summary>The waiver requirement is met (nothing required, or a valid signature on file).</summary>
        public bool Satisfied { get; init; }
        /// <summary>A signature was found for this rider (it may still fail the guardian rule).</summary>
        public bool Signed { get; init; }
        /// <summary>A waiver is required here and a document is configured to sign.</summary>
        public bool Required { get; init; }
        /// <summary>Rider-side (the guardian rule applies) as against spectator-side.</summary>
        public bool RiderAudience { get; init; } = true;
        /// <summary>Blocked because registration isn't finished (a signature alone won't fix it).</summary>
        public bool RegistrationIncomplete { get; init; }
        /// <summary>The document that has to be signed to clear a waiver block.</summary>
        public Guid? RequiredWaiverId { get; init; }
        /// <summary>Where a fixing signature belongs: "ticket" (the ticket's waiver_signature_id),
        /// "pass" (the pass's own waiver_signature_id) or "account" (a signature on the pass buyer's
        /// account for an event document). Null when there is nothing to fix.</summary>
        public string? FixTarget { get; init; }
        public DateOnly? Birthdate { get; init; }
        public int? Age { get; init; }
        public bool? IsMinor { get; init; }
        /// <summary>No birthdate anywhere for a rider. Not a block: staff should check ID.</summary>
        public bool BirthdateUnknown { get; init; }
        public bool SignedByGuardian { get; init; }
        public string? GuardianName { get; init; }
        public string? SignerName { get; init; }

        // ── Yearly birthdate check (tenant.birthdate_verify_mode, Script0305) ──
        /// <summary>The tenant's mode: off | prompt | required.</summary>
        public string BirthdateVerifyMode { get; init; } = Services.Riders.IdVerificationWindow.ModeOff;
        /// <summary>When the rider's ID/birthdate was last checked (account first, then credential).</summary>
        public DateTime? IdVerifiedAtUtc { get; init; }
        /// <summary>That check is within the tenant's window of today.</summary>
        public bool BirthdateVerifiedCurrent { get; init; }
        /// <summary>A rider whose birthdate is due for an ID check (mode on, not current).</summary>
        public bool BirthdateCheckNeeded { get; init; }
        /// <summary>Blocked only because the birthdate check is due ('required' mode).</summary>
        public bool BirthdateBlocked { get; init; }
        /// <summary>The tenant-local day of that last check (the date the block message and the gate
        /// banner show), serialized as yyyy-MM-dd.</summary>
        public DateOnly? IdVerifiedLocalDate { get; init; }
        /// <summary>The rider this decision is about (rider / holder name, else the buyer).</summary>
        public string? RiderName { get; init; }
    }

    /// <summary>The rider a gate signing link is for, resolved by the gate's own rules.</summary>
    public sealed class WaiverSignTarget
    {
        public Guid? TicketId { get; init; }
        public Guid? PassPurchaseId { get; init; }
        public Guid? EventId { get; init; }
        public Guid? PassAccountUserId { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public string DisplayName { get; init; } = "This rider";
        public WaiverGateDecision Decision { get; init; } = new();
    }

    /// <summary>
    /// Single source of truth for "may this rider be checked in given the waiver requirement?".
    /// Every redemption / check-in path (single redeem, bulk order redeem, admin check-in toggle,
    /// Loam Pass gate check-in, season pass check-in and walk-up admission, wristband linking) and
    /// every screen that previews one runs a ticket or pass through this, so a required waiver can
    /// never be skipped and a minor's waiver must be a parent / guardian's (GuardianWaiverRule).
    ///
    /// Scoped per request; lookups (tier, event, active waiver, tenant date, account, signature) are
    /// memoised for the request so a multi-ticket order costs one query per distinct row.
    /// </summary>
    public interface IWaiverCheckInGate
    {
        Task<string?> BlockReasonForTicket(Guid tenantId, EventTicketPurchase ticket);
        Task<WaiverGateDecision> EvaluateTicket(Guid tenantId, EventTicketPurchase ticket);
        /// <summary>One decision per ticket, in order, sharing the request's lookups.</summary>
        Task<List<WaiverGateDecision>> EvaluateTickets(Guid tenantId, IReadOnlyList<EventTicketPurchase> tickets);

        Task<WaiverGateDecision> EvaluatePass(Guid tenantId, Guid passPurchaseId, Guid? eventId);
        /// <summary>Same, for a pass and product the caller already loaded (no re-fetch).</summary>
        Task<WaiverGateDecision> EvaluatePass(Guid tenantId, SeasonPassPurchase pass, SeasonPassProduct? product, Guid? eventId);
        Task<string?> BlockReasonForPass(Guid tenantId, Guid passPurchaseId, Guid? eventId);

        /// <summary>Generic registrant gate (no birthdate known, so no guardian rule).</summary>
        Task<string?> BlockReason(Guid tenantId, Guid eventId, bool riderAudience,
            Guid? userId, string? email, string? displayName);

        /// <summary>Resolve the rider a gate signing link targets (ticket, or pass + optional event).
        /// Null when the target is not this tenant's.</summary>
        Task<WaiverSignTarget?> ResolveSignTarget(Guid tenantId, Guid? ticketId, Guid? passPurchaseId, Guid? eventId);

        /// <summary>
        /// A gate signing link was signed: put the new signature where the gate will read it, for the
        /// target and only where the current evidence is missing or failing. Never touches redeemed
        /// tickets or check-in history, never replaces a valid guardian signature, never re-points a
        /// pass's product-waiver signature at another document. Returns how many places were filled.
        /// </summary>
        Task<int> LinkSignedWaiver(Guid tenantId, WaiverSignTarget target, Guid signatureId, Guid waiverId);

        /// <summary>The tenant's birthdate-check policy as the gate sees it (read fresh, not from the
        /// tenant resolution cache), so screens echo exactly what is enforced.</summary>
        Task<(string Mode, int ValidDays)> BirthdatePolicy(Guid tenantId);
    }

    public class WaiverCheckInGate : IWaiverCheckInGate
    {
        private readonly IEventRepository _events;
        private readonly IEventTicketTierRepository _tiers;
        private readonly IWaiverRepository _waivers;
        private readonly ISeasonPassRepository _passes;
        private readonly IUserRepository _users;
        private readonly ITenantRepository _tenants;
        private readonly IEventTicketPurchaseRepository _tickets;

        // Per-request memo (the gate is scoped).
        private readonly Dictionary<Guid, EventTicketTier?> _tierCache = new();
        private readonly Dictionary<Guid, Event?> _eventCache = new();
        private readonly Dictionary<Guid, Guid?> _activeDocCache = new();
        private readonly Dictionary<Guid, TenantFacts> _tenantCache = new();
        private readonly Dictionary<Guid, User?> _userCache = new();
        private readonly Dictionary<Guid, RiderWaiverSignature?> _sigCache = new();
        private readonly Dictionary<(Guid AccountId, string RiderKey), RiderAccountIdVerification?> _riderCheckCache = new();

        public WaiverCheckInGate(
            IEventRepository events,
            IEventTicketTierRepository tiers,
            IWaiverRepository waivers,
            ISeasonPassRepository passes,
            IUserRepository users,
            ITenantRepository tenants,
            IEventTicketPurchaseRepository tickets)
        {
            _events = events;
            _tiers = tiers;
            _waivers = waivers;
            _passes = passes;
            _users = users;
            _tenants = tenants;
            _tickets = tickets;
        }

        public async Task<(string Mode, int ValidDays)> BirthdatePolicy(Guid tenantId)
        {
            var t = await Tenant(tenantId);
            return (t.Mode, t.ValidDays);
        }

        /// <summary>
        /// The rider's most recent ID check, lightly: the account's row for THIS rider (one query per
        /// account + rider per request), the legacy users stamp of a tenant's own account that is the
        /// rider (from the cached account), and the credential's own stamp. The rules (rider key,
        /// latest wins) are IdCheckRules, shared with RiderIdVerification.
        /// </summary>
        private async Task<Services.Riders.IdCheck?> LatestIdCheck(Guid tenantId, User? account,
            string? riderFirst, string? riderLast, DateTime? credentialAt, DateTime? credentialDob)
        {
            Services.Riders.IdCheck? row = null, legacy = null;
            if (account is not null && (account.TenantId is null || account.TenantId == tenantId))
            {
                var key = Services.Riders.IdCheckRules.RiderKey(riderFirst, riderLast, account.FirstName, account.LastName);
                if (!_riderCheckCache.TryGetValue((account.Id, key), out var r))
                    _riderCheckCache[(account.Id, key)] = r = await _users.GetRiderIdVerification(account.Id, tenantId, key);
                if (r is not null)
                    row = new Services.Riders.IdCheck(DateTime.SpecifyKind(r.VerifiedAt, DateTimeKind.Utc), null,
                        r.VerifiedDob, Services.Riders.IdVerificationScope.Rider);
                if (account.TenantId == tenantId && account.IdVerifiedAt is { } la
                    && Services.Riders.IdCheckRules.AccountIsRider(riderFirst, riderLast, account.FirstName, account.LastName))
                    legacy = new Services.Riders.IdCheck(DateTime.SpecifyKind(la, DateTimeKind.Utc), null,
                        account.IdVerifiedDob, Services.Riders.IdVerificationScope.Rider);
            }
            var credential = credentialAt is { } ca
                ? new Services.Riders.IdCheck(DateTime.SpecifyKind(ca, DateTimeKind.Utc), null, credentialDob,
                    Services.Riders.IdVerificationScope.Credential)
                : null;
            return Services.Riders.IdCheckRules.Latest(row, legacy, credential);
        }

        // ── Tickets ──────────────────────────────────────────────────────────────────────────

        public async Task<string?> BlockReasonForTicket(Guid tenantId, EventTicketPurchase ticket) =>
            (await EvaluateTicket(tenantId, ticket)).BlockReason;

        public async Task<List<WaiverGateDecision>> EvaluateTickets(Guid tenantId, IReadOnlyList<EventTicketPurchase> tickets)
        {
            var result = new List<WaiverGateDecision>(tickets.Count);
            foreach (var t in tickets) result.Add(await EvaluateTicket(tenantId, t));
            return result;
        }

        public async Task<WaiverGateDecision> EvaluateTicket(Guid tenantId, EventTicketPurchase ticket)
        {
            var tier = await Tier(ticket.TierId, tenantId);
            if (tier is null) return new WaiverGateDecision { Satisfied = true };   // can't classify; don't block

            // Race entries and rider gate fees are rider-side; a spectator gate fee is spectator-side.
            var riderAudience = tier.Kind == "race_entry"
                || (tier.Kind == "gate_fee" && tier.Audience == "rider");

            var ev = await Event(tier.EventId, tenantId);
            if (ev is null) return new WaiverGateDecision { Satisfied = true, RiderAudience = riderAudience };

            // Age facts, for display and the guardian rule. The ticket's linked signature is this
            // rider's own (registration writes one per registrant), so its attendee DOB may be used;
            // the buyer account's only when the account IS the rider.
            var sig = ticket.WaiverSignatureId is Guid sigId ? await Signature(sigId, tenantId) : null;
            var account = ticket.PurchaserUserId is Guid uid ? await User(uid) : null;
            // The rider's recorded ID check (account first, then this ticket). Its DOB is the
            // verified one, which wins for age.
            var idCheck = riderAudience
                ? await LatestIdCheck(tenantId, account, ticket.RiderFirstName, ticket.RiderLastName,
                    ticket.IdVerifiedAt, ticket.IdVerifiedDob)
                : null;
            var birthdate = GuardianWaiverRule.ResolveBirthdate(new RiderBirthdateFacts(
                GuardianWaiverRule.ToDateOnly(idCheck?.VerifiedDob ?? ticket.IdVerifiedDob),
                GuardianWaiverRule.ToDateOnly(ticket.RiderBirthdate),
                GuardianWaiverRule.ToDateOnly(sig?.SpectatorBirthdate),
                GuardianWaiverRule.ToDateOnly(account?.IdVerifiedDob),
                GuardianWaiverRule.ToDateOnly(account?.Birthdate),
                account is not null && GuardianWaiverRule.AccountIsRider(
                    ticket.RiderFirstName, ticket.RiderLastName, account.FirstName, account.LastName)));
            var tenant = await Tenant(tenantId);
            var ages = new AgeFacts(birthdate, tenant.Today, riderAudience, tenant.Verify(idCheck),
                JoinName(ticket.RiderFirstName, ticket.RiderLastName) ?? ticket.PurchaserName);
            var today = tenant.Today;

            // Registration must be finished before admission for rider-audience tickets: it is where
            // the rider's identity, signed waiver, and any required emergency contact are captured.
            // Keyed on THIS ticket's rider, not on whoever bought it.
            if (riderAudience && !ticket.RegistrationComplete)
            {
                var who0 = string.IsNullOrWhiteSpace(ticket.RiderFirstName) ? "This rider" : ticket.RiderFirstName;
                return ages.Apply(new WaiverGateDecision
                {
                    RiderAudience = true,
                    RegistrationIncomplete = true,
                    BlockReason = $"{who0} hasn't finished registration for this event. Complete registration (rider details and waiver) before checking in.",
                });
            }

            var requiredByEvent = riderAudience ? ev.RequiresRiderWaiver : ev.RequiresSpectatorWaiver;
            // Effective document = the event's pinned version for this audience, else the tenant's
            // active waiver. None configured: nothing to enforce (misconfiguration escape hatch).
            var doc = requiredByEvent
                ? (riderAudience ? ev.RacerWaiverId : ev.SpectatorWaiverId) ?? await ActiveDoc(tenantId)
                : null;
            if (doc is null)
                return ages.Apply(new WaiverGateDecision { Satisfied = true, RiderAudience = riderAudience,
                    Signed = sig is not null });

            // Signed for THIS ticket's rider: the linked signature row, else (tickets that predate the
            // link) the inline image / signed timestamp.
            var signed = ticket.WaiverSignatureId is not null
                || ticket.WaiverSignedAt is not null
                || !string.IsNullOrWhiteSpace(ticket.WaiverSignatureDataUrl);
            if (!signed)
            {
                var who = riderAudience ? "rider" : "spectator";
                var name = !string.IsNullOrWhiteSpace(ticket.RiderFirstName) ? ticket.RiderFirstName
                    : string.IsNullOrWhiteSpace(ticket.PurchaserName) ? "This attendee" : ticket.PurchaserName;
                return ages.Apply(new WaiverGateDecision
                {
                    RiderAudience = riderAudience, Required = true, RequiredWaiverId = doc, FixTarget = "ticket",
                    BlockReason = $"This event requires a signed {who} waiver. {name} must sign the waiver before checking in.",
                });
            }

            // Spectators: a signature is enough. Riders: a minor's must be a guardian's.
            if (!riderAudience)
                return ages.Apply(new WaiverGateDecision { Satisfied = true, Signed = true, Required = true,
                    RiderAudience = false, RequiredWaiverId = doc, SignerName = sig?.SignerName });

            var riderName = JoinName(ticket.RiderFirstName, ticket.RiderLastName) ?? ticket.PurchaserName;
            var check = GuardianWaiverRule.Evaluate(birthdate, today, Facts(sig, ticket.ParentGuardianName), riderName);
            return ages.Apply(new WaiverGateDecision
            {
                RiderAudience = true, Required = true, Signed = true, RequiredWaiverId = doc,
                Satisfied = check.BlockReason is null,
                BlockReason = check.BlockReason,
                FixTarget = check.BlockReason is null ? null : "ticket",
                SignedByGuardian = check.SignedByGuardian, GuardianName = check.GuardianName,
                SignerName = sig?.SignerName,
            });
        }

        // ── Season passes ────────────────────────────────────────────────────────────────────

        public async Task<string?> BlockReasonForPass(Guid tenantId, Guid passPurchaseId, Guid? eventId) =>
            (await EvaluatePass(tenantId, passPurchaseId, eventId)).BlockReason;

        public async Task<WaiverGateDecision> EvaluatePass(Guid tenantId, Guid passPurchaseId, Guid? eventId)
        {
            // GetPurchase is not tenant-scoped; the overload below re-asserts the tenant.
            var pass = await _passes.GetPurchase(passPurchaseId);
            if (pass is null || pass.TenantId != tenantId) return new WaiverGateDecision();
            return await EvaluatePass(tenantId, pass, await _passes.GetProduct(pass.ProductId, tenantId), eventId);
        }

        public async Task<WaiverGateDecision> EvaluatePass(Guid tenantId, SeasonPassPurchase pass,
            SeasonPassProduct? product, Guid? eventId)
        {
            if (pass.TenantId != tenantId) return new WaiverGateDecision();

            var productRequires = product?.RequiresWaiver ?? false;
            // The product waiver is the tenant's active document (what pass registration signs).
            var productDoc = productRequires ? await ActiveDoc(tenantId) : null;

            Guid? eventDoc = null;
            if (eventId is Guid eid && await Event(eid, tenantId) is { RequiresRiderWaiver: true } ev)
                eventDoc = ev.RacerWaiverId ?? await ActiveDoc(tenantId);   // none configured: nothing to enforce

            var account = await User(pass.PurchaserUserId);
            var accountIsHolder = account is not null && GuardianWaiverRule.AccountIsRider(
                pass.HolderFirstName, pass.HolderLastName, account.FirstName, account.LastName);
            var holderName = JoinName(pass.HolderFirstName, pass.HolderLastName) ?? pass.PurchaserName;

            // The pass's own signature is the holder's (pass registration signs for the holder).
            var own = pass.WaiverSignatureId is Guid ownId ? await Signature(ownId, tenantId) : null;

            // Evidence to judge, with where a fix would go and for which document.
            var checks = new List<(RiderWaiverSignature Sig, string Fix, Guid Doc)>();
            if (productDoc is Guid pd && own is not null) checks.Add((own, "pass", pd));   // presence: registration's job

            RiderWaiverSignature? accountSig = null;
            var eventMissing = false;
            if (eventDoc is Guid ed)
            {
                if (own is not null && own.WaiverId == ed)
                {
                    // The holder's own signature IS this event's document.
                    if (!checks.Any(c => c.Sig.Id == own.Id)) checks.Add((own, "pass", ed));
                }
                else
                {
                    // Otherwise the event document is looked up for the pass's account (the existing
                    // standard). By email only when there is no account.
                    accountSig = pass.PurchaserUserId != Guid.Empty
                        ? await _waivers.GetSignature(pass.PurchaserUserId, ed)
                        : string.IsNullOrWhiteSpace(pass.PurchaserEmail) ? null
                            : await _waivers.GetSignatureBySignerEmailForSelf(pass.PurchaserEmail, ed);
                    if (accountSig is null || accountSig.TenantId != tenantId) { accountSig = null; eventMissing = true; }
                    else checks.Add((accountSig, "account", ed));
                }
            }

            // Birthdate: holder-verified, holder DOB, then a signature's attendee DOB ONLY when that
            // signature is the holder's (their own pass signature, or an account signature that names
            // the holder, or names nobody while the account IS the holder), then the account's own
            // DOB when the account is the holder. A buyer's DOB never stands in for a holder's.
            DateOnly? sigBirth = GuardianWaiverRule.ToDateOnly(own?.SpectatorBirthdate);
            if (sigBirth is null && accountSig is not null && SignatureCoversHolder(accountSig, pass, accountIsHolder))
                sigBirth = GuardianWaiverRule.ToDateOnly(accountSig.SpectatorBirthdate);
            // The holder's most recent recorded ID check (the account's row for this holder, or this
            // pass); its DOB wins for age.
            var idCheck = await LatestIdCheck(tenantId, account, pass.HolderFirstName, pass.HolderLastName,
                pass.IdVerifiedAt, pass.IdVerifiedDob);
            var birthdate = GuardianWaiverRule.ResolveBirthdate(new RiderBirthdateFacts(
                GuardianWaiverRule.ToDateOnly(idCheck?.VerifiedDob ?? pass.IdVerifiedDob),
                GuardianWaiverRule.ToDateOnly(pass.HolderBirthdate),
                sigBirth,
                GuardianWaiverRule.ToDateOnly(account?.IdVerifiedDob),
                GuardianWaiverRule.ToDateOnly(account?.Birthdate),
                accountIsHolder));
            var tenant = await Tenant(tenantId);
            var today = tenant.Today;
            var ages = new AgeFacts(birthdate, today, RiderAudience: true, tenant.Verify(idCheck), holderName);
            var required = productDoc is not null || eventDoc is not null;

            if (eventMissing)
                return ages.Apply(new WaiverGateDecision
                {
                    Required = true, RequiredWaiverId = eventDoc, FixTarget = "account", Signed = own is not null,
                    BlockReason = $"This event requires a signed rider waiver. {holderName} must sign the waiver before checking in.",
                });

            WaiverGateDecision? shown = null;
            foreach (var (sig, fix, doc) in checks)
            {
                var g = GuardianWaiverRule.Evaluate(birthdate, today, Facts(sig, null), holderName);
                var d = new WaiverGateDecision
                {
                    Required = true, Signed = true, RequiredWaiverId = doc,
                    Satisfied = g.BlockReason is null, BlockReason = g.BlockReason,
                    FixTarget = g.BlockReason is null ? null : fix,
                    SignedByGuardian = g.SignedByGuardian, GuardianName = g.GuardianName, SignerName = sig.SignerName,
                };
                if (g.BlockReason is not null) return ages.Apply(d);
                shown = d;   // the last (event) evidence is what staff are shown
            }

            // Nothing failed. Satisfied unless the product needs a signature the pass doesn't have
            // (that is registration's block), or there is nothing at all to judge for a walk-up.
            var satisfied = required
                ? !(productDoc is not null && own is null)
                : eventId is not null || own is not null;
            return ages.Apply(new WaiverGateDecision
            {
                Required = required, Satisfied = satisfied, Signed = own is not null || accountSig is not null,
                SignedByGuardian = shown?.SignedByGuardian ?? false, GuardianName = shown?.GuardianName,
                SignerName = shown?.SignerName ?? own?.SignerName,
            });
        }

        // ── Generic ──────────────────────────────────────────────────────────────────────────

        public async Task<string?> BlockReason(Guid tenantId, Guid eventId, bool riderAudience,
            Guid? userId, string? email, string? displayName)
        {
            var ev = await Event(eventId, tenantId);
            if (ev is null) return null;

            var required = riderAudience ? ev.RequiresRiderWaiver : ev.RequiresSpectatorWaiver;
            if (!required) return null;

            var waiverId = (riderAudience ? ev.RacerWaiverId : ev.SpectatorWaiverId) ?? await ActiveDoc(tenantId);
            if (waiverId is null) return null;   // tenant has no waiver configured; nothing to enforce

            var signed = userId.HasValue
                ? await _waivers.GetSignature(userId.Value, waiverId.Value) is not null
                : !string.IsNullOrWhiteSpace(email)
                    && await _waivers.GetSignatureBySignerEmailForSelf(email, waiverId.Value) is not null;
            if (signed) return null;

            var who = riderAudience ? "rider" : "spectator";
            var name = string.IsNullOrWhiteSpace(displayName) ? "This rider" : displayName;
            return $"This event requires a signed {who} waiver. {name} must sign the waiver before checking in.";
        }

        // ── Signing links ────────────────────────────────────────────────────────────────────

        public async Task<WaiverSignTarget?> ResolveSignTarget(Guid tenantId, Guid? ticketId, Guid? passPurchaseId, Guid? eventId)
        {
            if (ticketId is Guid tid)
            {
                var t = await _tickets.GetById(tid, tenantId);
                if (t is null) return null;
                var tier = await Tier(t.TierId, tenantId);
                return new WaiverSignTarget
                {
                    TicketId = t.Id,
                    EventId = tier?.EventId,
                    FirstName = t.RiderFirstName, LastName = t.RiderLastName,
                    DisplayName = JoinName(t.RiderFirstName, t.RiderLastName) ?? t.PurchaserName,
                    Decision = await EvaluateTicket(tenantId, t),
                };
            }
            if (passPurchaseId is Guid pid)
            {
                var p = await _passes.GetPurchase(pid);
                if (p is null || p.TenantId != tenantId) return null;
                return new WaiverSignTarget
                {
                    PassPurchaseId = p.Id,
                    EventId = eventId,
                    PassAccountUserId = p.PurchaserUserId,
                    FirstName = p.HolderFirstName, LastName = p.HolderLastName,
                    DisplayName = JoinName(p.HolderFirstName, p.HolderLastName) ?? p.PurchaserName,
                    Decision = await EvaluatePass(tenantId, p, await _passes.GetProduct(p.ProductId, tenantId), eventId),
                };
            }
            return null;
        }

        public async Task<int> LinkSignedWaiver(Guid tenantId, WaiverSignTarget target, Guid signatureId, Guid waiverId)
        {
            if (target.TicketId is Guid tid)
            {
                // The anchor ticket and the same rider's other tickets (same registrant), but only
                // the ones of the SAME audience the gate currently fails for this document, and only
                // unredeemed ones. The repository re-checks paid + no valid guardian signature.
                var siblings = await _tickets.ListRegistrantTickets(tid, tenantId);
                var decisions = await EvaluateTickets(tenantId, siblings);
                var ids = siblings.Zip(decisions)
                    .Where(x => x.First.Status == "paid"
                                && x.Second.FixTarget == "ticket"
                                && x.Second.RiderAudience == target.Decision.RiderAudience
                                && x.Second.RequiredWaiverId == waiverId)
                    .Select(x => x.First.Id)
                    .ToList();
                return ids.Count == 0 ? 0 : await _tickets.LinkWaiverSignature(ids, tenantId, signatureId, waiverId);
            }

            if (target.PassPurchaseId is Guid pid)
            {
                // Decided again now, so a link signed after something else fixed the pass is a no-op.
                var now = (await ResolveSignTarget(tenantId, null, pid, target.EventId))?.Decision;
                if (now is null || now.RequiredWaiverId != waiverId) return 0;
                return now.FixTarget switch
                {
                    // The pass's own signature for its own document: fill / fix it.
                    "pass" => await _passes.LinkWaiverSignatureForHolder(pid, tenantId, signatureId, waiverId),
                    // An event document: a signature on the account the gate reads for that event,
                    // leaving the pass's product signature alone.
                    "account" when target.PassAccountUserId is Guid uid =>
                        await _waivers.AttachAccountSignature(tenantId, uid, waiverId, signatureId),
                    _ => 0,
                };
            }
            return 0;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────────

        /// <summary>A signature found on the pass's account covers the HOLDER when it names them
        /// (attendee name), or names nobody and the account owner is the holder.</summary>
        private static bool SignatureCoversHolder(RiderWaiverSignature sig, SeasonPassPurchase pass, bool accountIsHolder)
        {
            if (string.IsNullOrWhiteSpace(sig.SpectatorFirstName) && string.IsNullOrWhiteSpace(sig.SpectatorLastName))
                return accountIsHolder;
            return GuardianWaiverRule.AccountIsRider(pass.HolderFirstName, pass.HolderLastName,
                sig.SpectatorFirstName, sig.SpectatorLastName);
        }

        private static WaiverSignatureFacts Facts(RiderWaiverSignature? sig, string? legacyGuardianName) =>
            new(sig is not null, sig?.SignedByParent ?? false, sig?.ParentName, sig?.SignerName, legacyGuardianName);

        /// <summary>The yearly birthdate check, as it stands for one rider today.</summary>
        private readonly record struct VerifyFacts(string Mode, DateTime? VerifiedAtUtc, DateOnly? VerifiedLocalDate, bool Current);

        /// <summary>The tenant's date, zone and birthdate-check policy, loaded once per request.</summary>
        private sealed record TenantFacts(DateOnly Today, TimeZoneInfo Zone, string Mode, int ValidDays)
        {
            public VerifyFacts Verify(Services.Riders.IdCheck? check)
            {
                var at = check?.VerifiedAtUtc;
                return new VerifyFacts(Mode, at,
                    Services.Riders.IdVerificationWindow.VerifiedLocalDate(at, Zone),
                    Services.Riders.IdVerificationWindow.IsCurrent(at, Today, ValidDays, Zone));
            }
        }

        /// <summary>
        /// Age and birthdate-check facts every decision carries, whatever the outcome; and the ONE
        /// place the yearly birthdate check is enforced: in 'required' mode a rider-side ticket or
        /// pass whose birthdate wasn't checked against an ID within the window is blocked, on every
        /// path that asks the gate (when nothing else already blocks it).
        /// </summary>
        private readonly record struct AgeFacts(DateOnly? Birthdate, DateOnly Today, bool RiderAudience,
            VerifyFacts Verify = default, string? RiderName = null)
        {
            public WaiverGateDecision Apply(WaiverGateDecision d)
            {
                int? age = Birthdate is { } b ? GuardianWaiverRule.AgeOn(b, Today) : null;
                if (age < 0) age = null;
                var mode = Verify.Mode ?? Services.Riders.IdVerificationWindow.ModeOff;
                var checkNeeded = RiderAudience && mode != Services.Riders.IdVerificationWindow.ModeOff && !Verify.Current;
                var birthdateBlock = checkNeeded && mode == Services.Riders.IdVerificationWindow.ModeRequired
                    && d.BlockReason is null;
                return new WaiverGateDecision
                {
                    BlockReason = birthdateBlock ? BirthdateBlockMessage(RiderName, Verify.VerifiedLocalDate) : d.BlockReason,
                    Satisfied = d.Satisfied, Signed = d.Signed, Required = d.Required,
                    RiderAudience = d.RiderAudience, RegistrationIncomplete = d.RegistrationIncomplete,
                    RequiredWaiverId = d.RequiredWaiverId, FixTarget = d.FixTarget,
                    SignedByGuardian = d.SignedByGuardian, GuardianName = d.GuardianName, SignerName = d.SignerName,
                    Birthdate = Birthdate,
                    Age = age,
                    IsMinor = age is null ? null : age < GuardianWaiverRule.AdultAge,
                    BirthdateUnknown = RiderAudience && Birthdate is null,
                    BirthdateVerifyMode = mode,
                    IdVerifiedAtUtc = Verify.VerifiedAtUtc,
                    BirthdateVerifiedCurrent = Verify.Current,
                    BirthdateCheckNeeded = checkNeeded,
                    BirthdateBlocked = birthdateBlock,
                    IdVerifiedLocalDate = Verify.VerifiedLocalDate,
                    RiderName = RiderName,
                };
            }
        }

        public static string BirthdateBlockMessage(string? riderName, DateOnly? lastChecked) =>
            $"{(string.IsNullOrWhiteSpace(riderName) ? "This rider" : riderName.Trim())}'s birthdate needs checking against an ID "
            + $"(last checked {(lastChecked is { } d ? d.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture) : "never")}).";

        private async Task<EventTicketTier?> Tier(Guid id, Guid tenantId)
        {
            if (!_tierCache.TryGetValue(id, out var v)) _tierCache[id] = v = await _tiers.GetById(id, tenantId);
            return v;
        }

        private async Task<Event?> Event(Guid id, Guid tenantId)
        {
            if (!_eventCache.TryGetValue(id, out var v)) _eventCache[id] = v = await _events.GetById(id, tenantId);
            return v;
        }

        private async Task<Guid?> ActiveDoc(Guid tenantId)
        {
            if (!_activeDocCache.TryGetValue(tenantId, out var v))
                _activeDocCache[tenantId] = v = (await _waivers.GetActive(tenantId))?.Id;
            return v;
        }

        private async Task<User?> User(Guid id)
        {
            if (id == Guid.Empty) return null;
            if (!_userCache.TryGetValue(id, out var v)) _userCache[id] = v = await _users.GetById(id);
            return v;
        }

        private async Task<RiderWaiverSignature?> Signature(Guid id, Guid tenantId)
        {
            if (!_sigCache.TryGetValue(id, out var v)) _sigCache[id] = v = await _waivers.GetSignatureById(id, tenantId);
            return v;
        }

        // The tenant's local date: a rider's age is judged on the day they stand at the gate.
        private async Task<TenantFacts> Tenant(Guid tenantId)
        {
            if (_tenantCache.TryGetValue(tenantId, out var f)) return f;
            var tenant = await _tenants.GetById(tenantId);
            var mode = Services.Riders.IdVerificationWindow.IsValidMode(tenant?.BirthdateVerifyMode)
                ? tenant!.BirthdateVerifyMode : Services.Riders.IdVerificationWindow.ModeOff;
            f = new TenantFacts(TenantClock.LocalDate(tenant?.Timezone, DateTime.UtcNow),
                TenantClock.Resolve(tenant?.Timezone), mode, tenant?.BirthdateVerifyValidDays ?? 365);
            _tenantCache[tenantId] = f;
            return f;
        }

        private static string? JoinName(string? first, string? last)
        {
            var n = $"{first?.Trim()} {last?.Trim()}".Trim();
            return n.Length == 0 ? null : n;
        }
    }
}
