using Services.Riders;
using Services.Waivers;

namespace webapi.Helpers
{
    /// <summary>
    /// The rider a band would go on, through one credential (a pass, or a ticket): who they are (the gate's
    /// rider key and a display name), the credential, and how to ask the gate and the ID rule about them.
    /// Built by IRiderDayBands.ForPass / ForTicket so every band check is the same check.
    /// </summary>
    public sealed record RiderDayBandSubject(
        RiderPhotoKey Rider,
        string Name,
        Guid? PassId,
        Guid? TicketId,
        Func<Task<WaiverGateDecision>> Gate,
        Func<Task<bool>> IdVerified);
}
