namespace Services.Repositories.Data.SmsData
{
    /// <summary>
    /// Twilio Toll-Free Verification submission state for one tenant. One
    /// row per tenant — the row exists as a draft from the moment the admin
    /// opens the form, gets a TwilioVerificationSid + Status once submitted,
    /// and stays around through the rejection/resubmission lifecycle.
    ///
    /// Status mirrors Twilio's enum exactly: PENDING_REVIEW, IN_REVIEW,
    /// TWILIO_APPROVED, TWILIO_REJECTED, CARRIER_APPROVED, CARRIER_REJECTED.
    /// Null Status = never submitted (draft).
    /// </summary>
    public class TenantTollfreeVerification
    {
        public Guid TenantId { get; set; }

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

        public string? TwilioVerificationSid { get; set; }
        public string? Status { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime? LastSubmittedAtUtc { get; set; }
        public DateTime? LastStatusCheckedAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
