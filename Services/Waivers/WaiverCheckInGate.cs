using Services.Helpers;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Waivers
{
    /// <summary>
    /// The gate's full waiver verdict for one rider: whether they may be admitted, and the facts a
    /// screen needs to explain it (age on the day, who signed, the guardian's name, an unknown DOB).
    /// </summary>
    public sealed class WaiverGateDecision
    {
        /// <summary>Why check-in is blocked, or null when it is allowed.</summary>
        public string? BlockReason { get; init; }
        /// <summary>The waiver requirement is met (nothing required, or a valid signature on file).</summary>
        public bool Satisfied { get; init; }
        /// <summary>A signature was found for this rider (it may still fail the guardian rule).</summary>
        public bool Signed { get; init; }
        public int? Age { get; init; }
        public bool? IsMinor { get; init; }
        /// <summary>No birthdate anywhere for a rider whose waiver was checked. Not a block.</summary>
        public bool BirthdateUnknown { get; init; }
        public bool SignedByGuardian { get; init; }
        public string? GuardianName { get; init; }
        public string? SignerName { get; init; }

        public static WaiverGateDecision Allow(bool satisfied = true) => new() { Satisfied = satisfied };
        public static WaiverGateDecision Block(string reason) => new() { BlockReason = reason };
    }

    /// <summary>
    /// Single source of truth for "may this rider be checked in given the event's waiver
    /// requirement?". Every redemption / check-in path (single redeem, bulk order redeem,
    /// admin check-in toggle, Loam Pass gate check-in, season pass check-in and walk-up admission,
    /// wristband linking) runs a ticket or pass through this so a required waiver can never be
    /// skipped, regardless of how the rider entered. A minor's waiver must be a parent / guardian's
    /// (see GuardianWaiverRule).
    /// </summary>
    public interface IWaiverCheckInGate
    {
        /// <summary>
        /// Returns a human-readable reason to block check-in, or null when check-in is allowed.
        /// A rider (race entry or rider gate fee) must have signed the event's rider waiver; a
        /// spectator gate fee, the spectator waiver. The applicable waiver is the event's pinned
        /// version, falling back to the tenant's active waiver. A rider under 18 on the check-in day
        /// needs the waiver signed by a parent or guardian.
        /// </summary>
        Task<string?> BlockReasonForTicket(Guid tenantId, EventTicketPurchase ticket);

        /// <summary>BlockReasonForTicket with the facts behind it.</summary>
        Task<WaiverGateDecision> EvaluateTicket(Guid tenantId, EventTicketPurchase ticket);

        /// <summary>
        /// Season pass holder. A signature on the pass itself is the holder's own evidence;
        /// without one, the event's rider waiver is looked up for the pass's account (only when an
        /// event is given). Either way a minor needs a guardian's signature.
        /// </summary>
        Task<WaiverGateDecision> EvaluatePass(Guid tenantId, Guid passPurchaseId, Guid? eventId);

        /// <summary>EvaluatePass, reason only.</summary>
        Task<string?> BlockReasonForPass(Guid tenantId, Guid passPurchaseId, Guid? eventId);

        /// <summary>
        /// Lower-level gate for registrants that aren't an event ticket or a pass. Same
        /// effective-waiver resolution as BlockReasonForTicket, but no birthdate is known here, so
        /// the guardian rule cannot run; ticket and pass callers use the methods above.
        /// </summary>
        Task<string?> BlockReason(Guid tenantId, Guid eventId, bool riderAudience,
            Guid? userId, string? email, string? displayName);
    }

    public class WaiverCheckInGate : IWaiverCheckInGate
    {
        private readonly IEventRepository _events;
        private readonly IEventTicketTierRepository _tiers;
        private readonly IWaiverRepository _waivers;
        private readonly ISeasonPassRepository _passes;
        private readonly IUserRepository _users;
        private readonly ITenantRepository _tenants;
        private readonly Dictionary<Guid, DateOnly> _todayByTenant = new();

        public WaiverCheckInGate(
            IEventRepository events,
            IEventTicketTierRepository tiers,
            IWaiverRepository waivers,
            ISeasonPassRepository passes,
            IUserRepository users,
            ITenantRepository tenants)
        {
            _events = events;
            _tiers = tiers;
            _waivers = waivers;
            _passes = passes;
            _users = users;
            _tenants = tenants;
        }

        public async Task<string?> BlockReasonForTicket(Guid tenantId, EventTicketPurchase ticket) =>
            (await EvaluateTicket(tenantId, ticket)).BlockReason;

        public async Task<WaiverGateDecision> EvaluateTicket(Guid tenantId, EventTicketPurchase ticket)
        {
            var tier = await _tiers.GetById(ticket.TierId, tenantId);
            if (tier is null) return WaiverGateDecision.Allow();   // can't classify a missing tier; don't block on it

            // Race entries and rider gate fees are rider-side; a spectator gate fee is spectator-side.
            var riderAudience = tier.Kind == "race_entry"
                || (tier.Kind == "gate_fee" && tier.Audience == "rider");

            var ev = await _events.GetById(tier.EventId, tenantId);
            if (ev is null) return WaiverGateDecision.Allow();

            // Registration must be finished before admission for rider-audience tickets: registration
            // is where the rider's identity, signed waiver, and any required emergency contact are
            // captured. This is keyed on THIS ticket's rider, not on whoever bought it — a purchaser
            // who once signed can otherwise walk unregistered/unsigned riders (incl. minors) in.
            if (riderAudience && !ticket.RegistrationComplete)
            {
                var who0 = string.IsNullOrWhiteSpace(ticket.RiderFirstName) ? "This rider" : ticket.RiderFirstName;
                return WaiverGateDecision.Block(
                    $"{who0} hasn't finished registration for this event. Complete registration (rider details and waiver) before checking in.");
            }

            var required = riderAudience ? ev.RequiresRiderWaiver : ev.RequiresSpectatorWaiver;
            if (!required) return WaiverGateDecision.Allow();

            // The waiver must be signed for THIS ticket's rider. Both sale paths now link the
            // signature row (waiver_signature_id); the inline image / signed timestamp are accepted as
            // a fallback for any ticket that predates the link.
            var signed = ticket.WaiverSignatureId is not null
                || ticket.WaiverSignedAt is not null
                || !string.IsNullOrWhiteSpace(ticket.WaiverSignatureDataUrl);
            if (!signed)
            {
                // Nothing to enforce if the tenant has no waiver document configured (misconfiguration
                // escape hatch, matching the previous behavior).
                var waiverId = riderAudience ? ev.RacerWaiverId : ev.SpectatorWaiverId;
                if (waiverId is null) waiverId = (await _waivers.GetActive(tenantId))?.Id;
                if (waiverId is null) return WaiverGateDecision.Allow();

                var who = riderAudience ? "rider" : "spectator";
                var name = !string.IsNullOrWhiteSpace(ticket.RiderFirstName) ? ticket.RiderFirstName
                    : string.IsNullOrWhiteSpace(ticket.PurchaserName) ? "This attendee" : ticket.PurchaserName;
                return WaiverGateDecision.Block(
                    $"This event requires a signed {who} waiver. {name} must sign the waiver before checking in.");
            }

            // Signed. The guardian rule applies to riders: a minor's waiver must be a guardian's.
            if (!riderAudience) return new WaiverGateDecision { Satisfied = true, Signed = true };

            var sig = ticket.WaiverSignatureId is Guid sigId ? await _waivers.GetSignatureById(sigId, tenantId) : null;
            var account = ticket.PurchaserUserId is Guid uid ? await _users.GetById(uid) : null;
            var birthdate = GuardianWaiverRule.ResolveBirthdate(new RiderBirthdateFacts(
                GuardianWaiverRule.ToDateOnly(ticket.IdVerifiedDob),
                GuardianWaiverRule.ToDateOnly(ticket.RiderBirthdate),
                GuardianWaiverRule.ToDateOnly(sig?.SpectatorBirthdate),
                GuardianWaiverRule.ToDateOnly(account?.IdVerifiedDob),
                GuardianWaiverRule.ToDateOnly(account?.Birthdate),
                account is not null && GuardianWaiverRule.AccountIsRider(
                    ticket.RiderFirstName, ticket.RiderLastName, account.FirstName, account.LastName)));
            var riderName = JoinName(ticket.RiderFirstName, ticket.RiderLastName) ?? ticket.PurchaserName;
            return Judge(birthdate, await TenantToday(tenantId), SignatureFacts(sig, ticket.ParentGuardianName), riderName);
        }

        public async Task<string?> BlockReasonForPass(Guid tenantId, Guid passPurchaseId, Guid? eventId) =>
            (await EvaluatePass(tenantId, passPurchaseId, eventId)).BlockReason;

        public async Task<WaiverGateDecision> EvaluatePass(Guid tenantId, Guid passPurchaseId, Guid? eventId)
        {
            // GetPurchase is not tenant-scoped; re-assert it.
            var pass = await _passes.GetPurchase(passPurchaseId);
            if (pass is null || pass.TenantId != tenantId) return WaiverGateDecision.Allow(satisfied: false);

            var product = await _passes.GetProduct(pass.ProductId, tenantId);
            var productRequires = product?.RequiresWaiver ?? false;

            // The event's rider waiver, when an event is in play and it requires one.
            Guid? eventDocId = null;
            var eventRequires = false;
            if (eventId is Guid eid && await _events.GetById(eid, tenantId) is { } ev && ev.RequiresRiderWaiver)
            {
                eventDocId = ev.RacerWaiverId ?? (await _waivers.GetActive(tenantId))?.Id;
                eventRequires = eventDocId is not null;   // no document configured: nothing to enforce
            }

            var holderName = JoinName(pass.HolderFirstName, pass.HolderLastName) ?? pass.PurchaserName;
            RiderWaiverSignature? sig;
            if (pass.WaiverSignatureId is Guid own)
            {
                // A holder signature on the pass is the holder's own evidence for the product AND any
                // event waiver (the standard a ticket's linked signature meets). Not needed at all
                // when neither the product nor the event asks for a waiver.
                if (!productRequires && !eventRequires) return new WaiverGateDecision { Satisfied = true, Signed = true };
                sig = await _waivers.GetSignatureById(own, tenantId);
            }
            else if (eventRequires)
            {
                // No signature on the pass: the event waiver is looked up for the pass's account.
                // A pass bought for someone else is then tested against the buyer's signature; that
                // is the existing standard, kept, and the guardian rule below still uses the
                // HOLDER's birthdate.
                sig = await _waivers.GetSignature(pass.PurchaserUserId, eventDocId!.Value)
                      ?? (string.IsNullOrWhiteSpace(pass.PurchaserEmail) ? null
                          : await _waivers.GetSignatureBySignerEmailForSelf(pass.PurchaserEmail, eventDocId.Value));
                if (sig is null)
                    return WaiverGateDecision.Block(
                        $"This event requires a signed rider waiver. {holderName} must sign the waiver before checking in.");
            }
            else
            {
                // An event that requires nothing is satisfied; with no event there is no event
                // document, and the product's own waiver is the registration check's job.
                return WaiverGateDecision.Allow(satisfied: eventId is not null);
            }

            var account = await _users.GetById(pass.PurchaserUserId);
            var birthdate = GuardianWaiverRule.ResolveBirthdate(new RiderBirthdateFacts(
                GuardianWaiverRule.ToDateOnly(pass.IdVerifiedDob),
                GuardianWaiverRule.ToDateOnly(pass.HolderBirthdate),
                GuardianWaiverRule.ToDateOnly(sig?.SpectatorBirthdate),
                GuardianWaiverRule.ToDateOnly(account?.IdVerifiedDob),
                GuardianWaiverRule.ToDateOnly(account?.Birthdate),
                account is not null && GuardianWaiverRule.AccountIsRider(
                    pass.HolderFirstName, pass.HolderLastName, account.FirstName, account.LastName)));
            return Judge(birthdate, await TenantToday(tenantId), SignatureFacts(sig, null), holderName);
        }

        public async Task<string?> BlockReason(Guid tenantId, Guid eventId, bool riderAudience,
            Guid? userId, string? email, string? displayName)
        {
            var ev = await _events.GetById(eventId, tenantId);
            if (ev is null) return null;

            var required = riderAudience ? ev.RequiresRiderWaiver : ev.RequiresSpectatorWaiver;
            if (!required) return null;

            // Effective waiver = the event's pinned version, else the tenant's active waiver.
            // Same resolution the registration flow uses, so purchase and check-in agree.
            var waiverId = riderAudience ? ev.RacerWaiverId : ev.SpectatorWaiverId;
            if (waiverId is null) waiverId = (await _waivers.GetActive(tenantId))?.Id;
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

        private static WaiverGateDecision Judge(DateOnly? birthdate, DateOnly today, WaiverSignatureFacts facts, string? riderName)
        {
            var check = GuardianWaiverRule.Evaluate(birthdate, today, facts, riderName);
            return new WaiverGateDecision
            {
                BlockReason = check.BlockReason,
                Satisfied = check.BlockReason is null,
                Signed = true,
                Age = check.Age,
                IsMinor = check.IsMinor,
                BirthdateUnknown = check.BirthdateUnknown,
                SignedByGuardian = check.SignedByGuardian,
                GuardianName = check.GuardianName,
                SignerName = facts.SignerName,
            };
        }

        private static WaiverSignatureFacts SignatureFacts(RiderWaiverSignature? sig, string? legacyGuardianName) =>
            new(sig is not null, sig?.SignedByParent ?? false, sig?.ParentName, sig?.SignerName, legacyGuardianName);

        // The tenant's local date: a rider's age is judged on the day they stand at the gate.
        private async Task<DateOnly> TenantToday(Guid tenantId)
        {
            if (_todayByTenant.TryGetValue(tenantId, out var d)) return d;
            var tenant = await _tenants.GetById(tenantId);
            d = TenantClock.LocalDate(tenant?.Timezone, DateTime.UtcNow);
            _todayByTenant[tenantId] = d;
            return d;
        }

        private static string? JoinName(string? first, string? last)
        {
            var n = $"{first?.Trim()} {last?.Trim()}".Trim();
            return n.Length == 0 ? null : n;
        }
    }
}
