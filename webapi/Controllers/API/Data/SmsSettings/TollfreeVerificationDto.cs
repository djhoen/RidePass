namespace webapi.Controllers.API.Data.SmsSettings
{
    /// <summary>
    /// The full verification record returned by GET. Pre-fill on the form
    /// uses these fields directly; pre-fill from tenant address/contact
    /// happens client-side when the row is null.
    /// </summary>
    public class TollfreeVerificationDto
    {
        public string? BusinessName { get; set; }
        public string? BusinessWebsite { get; set; }
        public string? BusinessStreetAddress { get; set; }
        public string? BusinessCity { get; set; }
        public string? BusinessStateProvinceRegion { get; set; }
        public string? BusinessPostalCode { get; set; }
        public string? BusinessCountry { get; set; }

        public string? BusinessContactFirstName { get; set; }
        public string? BusinessContactLastName { get; set; }
        public string? BusinessContactEmail { get; set; }
        public string? BusinessContactPhone { get; set; }

        public string? NotificationEmail { get; set; }

        public string[]? UseCaseCategories { get; set; }
        public string? UseCaseSummary { get; set; }
        public string[]? ProductionMessageSamples { get; set; }

        public string? OptInType { get; set; }
        public string[]? OptInImageUrls { get; set; }

        public string? MessageVolume { get; set; }
        public string? AdditionalInformation { get; set; }

        public string? Status { get; set; }                 // null = draft
        public string? RejectionReason { get; set; }
        public DateTime? LastSubmittedAtUtc { get; set; }
        public DateTime? LastStatusCheckedAtUtc { get; set; }
    }
}
