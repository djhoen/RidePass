namespace webapi.Controllers.API.Data.Counter
{
    /// <summary>A signing link for one "another rider" ticket on a Gate Sale.</summary>
    public class CounterRiderWaiverLinkRequest
    {
        public Guid PurchaseId { get; set; }
        /// <summary>Email (and text, when SMS is set up) the link. False = just the link, to sign on this device.</summary>
        public bool Deliver { get; set; }
    }
}
