using Services.Waivers;

namespace webapi.Helpers
{
    /// <summary>
    /// Puts a new signature where the gate reads it for a ticket / pass target and checks that it took, shared
    /// by the signing link (WaiverController.SignByToken) and a waiver pushed to a kiosk (KioskController), so
    /// both leave the gate in the same state.
    /// </summary>
    public static class GateWaiverLinking
    {
        /// <returns>How many places were linked, and the gate's block reason when the signature could not be
        /// applied (the gate still fails for this document with a fix it was for), else null.</returns>
        public static async Task<(int Linked, string? StillBlocked)> LinkAndCheck(IWaiverCheckInGate gate, Guid tenantId,
            WaiverSignTarget target, Guid signatureId, Guid waiverId)
        {
            // The gate fills only what is missing or failing and never replaces a valid guardian signature.
            var linked = await gate.LinkSignedWaiver(tenantId, target, signatureId, waiverId);
            var after = await gate.ResolveSignTarget(tenantId, target.TicketId, target.PassPurchaseId, target.EventId);
            var still = linked == 0 && after?.Decision is { BlockReason: not null, FixTarget: not null } d
                        && d.RequiredWaiverId == waiverId
                ? d.BlockReason
                : null;
            return (linked, still);
        }
    }
}
