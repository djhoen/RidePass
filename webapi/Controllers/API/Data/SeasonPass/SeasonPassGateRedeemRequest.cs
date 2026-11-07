namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>Walk-up gate redemption of a scanned season pass. When an event is running, the
    /// event is chosen client-side (the scanner auto-selects when only one is running) so the
    /// server never has to guess between same-day events. When the track is simply open with
    /// nothing on the calendar, EventId is omitted and the admission anchors to the tenant-local
    /// date instead.</summary>
    public class SeasonPassGateRedeemRequest
    {
        /// <summary>Null means a no-event walk-up admission. Not [Required]: that is exactly the
        /// case this field has to be able to express.</summary>
        public Guid? EventId { get; set; }

        /// <summary>"Admit and band" (Script0312): the wristband scanned or typed with the admission. Checked before
        /// admitting, so nothing happens when the band can't go on; then issued for the rider's day. Required
        /// (unless already banded today) when the track turned on wristband_required_at_checkin.</summary>
        public string? BandCode { get; set; }
    }
}
