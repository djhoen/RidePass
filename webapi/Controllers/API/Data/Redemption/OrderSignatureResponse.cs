namespace webapi.Controllers.API.Data.Redemption
{
    /// <summary>
    /// The signature image behind one ticket in the scanned order, fetched on demand so the check-in
    /// payload doesn't carry a megabyte of base64 per attendee.
    /// </summary>
    public class OrderSignatureResponse
    {
        public Guid PurchaseId { get; set; }
        public string? AttendeeName { get; set; }
        public string? WaiverName { get; set; }
        public string? WaiverTitle { get; set; }
        public DateTime? SignedAtUtc { get; set; }
        public bool SignedByParent { get; set; }
        public string? GuardianName { get; set; }
        public string? SignerName { get; set; }
        public string? SignerEmail { get; set; }
        /// <summary>PNG data URL of the drawn signature. NULL when the row was signed without an image.</summary>
        public string? SignatureDataUrl { get; set; }
    }
}
