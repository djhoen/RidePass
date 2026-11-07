namespace webapi.Controllers.API.Data.Redemption
{
    /// <summary>Gate: take (or replace) the photo on file for the rider a ticket or pass admits.</summary>
    public class TakeRiderPhotoRequest
    {
        /// <summary>"ticket" (event_ticket_purchase) or "season_pass" (season_pass_purchase).</summary>
        public string Kind { get; set; } = "ticket";
        public Guid PurchaseId { get; set; }
        /// <summary>A JPEG or PNG data URL (PhotoCapture.vue scales it to 600px JPEG).</summary>
        public string? PhotoDataUrl { get; set; }
    }
}
