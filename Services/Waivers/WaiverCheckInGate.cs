using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Waivers
{
    /// <summary>
    /// Single source of truth for "may this rider be checked in given the event's waiver
    /// requirement?". Every redemption / check-in path (single redeem, bulk order redeem,
    /// admin check-in toggle, Loam Pass gate check-in) runs a ticket through this so a
    /// required waiver can never be skipped, regardless of how the rider entered.
    /// </summary>
    public interface IWaiverCheckInGate
    {
        /// <summary>
        /// Returns a human-readable reason to block check-in, or null when check-in is allowed.
        /// A rider (race entry or rider gate fee) must have signed the event's rider waiver; a
        /// spectator gate fee, the spectator waiver. The applicable waiver is the event's pinned
        /// version, falling back to the tenant's active waiver. Authed riders are matched by
        /// user id, guests by purchaser email.
        /// </summary>
        Task<string?> BlockReasonForTicket(Guid tenantId, EventTicketPurchase ticket);

        /// <summary>
        /// Lower-level gate for registrants that aren't an event ticket (e.g. a season-pass
        /// reservation). Same effective-waiver resolution as BlockReasonForTicket.
        /// </summary>
        Task<string?> BlockReason(Guid tenantId, Guid eventId, bool riderAudience,
            Guid? userId, string? email, string? displayName);
    }

    public class WaiverCheckInGate : IWaiverCheckInGate
    {
        private readonly IEventRepository _events;
        private readonly IEventTicketTierRepository _tiers;
        private readonly IWaiverRepository _waivers;

        public WaiverCheckInGate(
            IEventRepository events,
            IEventTicketTierRepository tiers,
            IWaiverRepository waivers)
        {
            _events = events;
            _tiers = tiers;
            _waivers = waivers;
        }

        public async Task<string?> BlockReasonForTicket(Guid tenantId, EventTicketPurchase ticket)
        {
            var tier = await _tiers.GetById(ticket.TierId, tenantId);
            if (tier is null) return null;   // can't classify a missing tier; don't block on it

            // Race entries and rider gate fees are rider-side; a spectator gate fee is spectator-side.
            var riderAudience = tier.Kind == "race_entry"
                || (tier.Kind == "gate_fee" && tier.Audience == "rider");

            var ev = await _events.GetById(tier.EventId, tenantId);
            if (ev is null) return null;

            // Registration must be finished before admission for rider-audience tickets: registration
            // is where the rider's identity, signed waiver, and any required emergency contact are
            // captured. This is keyed on THIS ticket's rider, not on whoever bought it — a purchaser
            // who once signed can otherwise walk unregistered/unsigned riders (incl. minors) in.
            if (riderAudience && !ticket.RegistrationComplete)
            {
                var who0 = string.IsNullOrWhiteSpace(ticket.RiderFirstName) ? "This rider" : ticket.RiderFirstName;
                return $"{who0} hasn't finished registration for this event. Complete registration (rider details and waiver) before checking in.";
            }

            var required = riderAudience ? ev.RequiresRiderWaiver : ev.RequiresSpectatorWaiver;
            if (!required) return null;

            // The waiver must be signed for THIS ticket's rider. Both sale paths now link the
            // signature row (waiver_signature_id); the inline image / signed timestamp are accepted as
            // a fallback for any ticket that predates the link.
            var signed = ticket.WaiverSignatureId is not null
                || ticket.WaiverSignedAt is not null
                || !string.IsNullOrWhiteSpace(ticket.WaiverSignatureDataUrl);
            if (signed) return null;

            // Nothing to enforce if the tenant has no waiver document configured (misconfiguration
            // escape hatch, matching the previous behavior).
            var waiverId = riderAudience ? ev.RacerWaiverId : ev.SpectatorWaiverId;
            if (waiverId is null) waiverId = (await _waivers.GetActive(tenantId))?.Id;
            if (waiverId is null) return null;

            var who = riderAudience ? "rider" : "spectator";
            var name = !string.IsNullOrWhiteSpace(ticket.RiderFirstName) ? ticket.RiderFirstName
                : string.IsNullOrWhiteSpace(ticket.PurchaserName) ? "This attendee" : ticket.PurchaserName;
            return $"This event requires a signed {who} waiver. {name} must sign the waiver before checking in.";
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
    }
}
