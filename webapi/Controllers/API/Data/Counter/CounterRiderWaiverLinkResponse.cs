namespace webapi.Controllers.API.Data.Counter
{
    public class CounterRiderWaiverLinkResponse
    {
        public Guid PurchaseId { get; set; }
        public Guid? RequestId { get; set; }
        /// <summary>signed | not_required | link_sent | needs_signature (CounterRiderRules).</summary>
        public string Status { get; set; } = string.Empty;
        public string? Link { get; set; }
        /// <summary>guardian | adult: how the link must be signed.</summary>
        public string? SignAs { get; set; }
        public string? RecipientName { get; set; }
        public string? RecipientEmail { get; set; }
        public string? RecipientPhone { get; set; }
        public bool EmailSent { get; set; }
        public bool SmsSent { get; set; }
        /// <summary>This venue can send texts (Twilio configured); otherwise show the link / QR.</summary>
        public bool SmsAvailable { get; set; }
        /// <summary>An open link for this ticket already existed and was reused.</summary>
        public bool Reused { get; set; }
    }
}
