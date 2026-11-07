namespace webapi.Controllers.API.Data.Redemption
{
    /// <summary>
    /// One attending person in the scanned order, with their waiver status. A rider who bought a
    /// gate fee plus three race classes is ONE attendee here (the tickets are grouped by registrant),
    /// so the gate reads "who is walking in and have they signed" rather than a list of line items.
    /// </summary>
    public class OrderWaiverAttendee
    {
        /// <summary>Stable key for the person: the registrant id when the order was grouped, else the ticket id.</summary>
        public string AttendeeKey { get; set; } = null!;
        /// <summary>Every ticket this person holds in the order (so the UI can cross-link to the Items tab).</summary>
        public List<Guid> PurchaseIds { get; set; } = new();
        /// <summary>Name captured at registration. NULL when registration never happened, itself a red flag at the gate.</summary>
        public string? Name { get; set; }
        public string Audience { get; set; } = "rider";      // rider | spectator
        /// <summary>The birthdate the gate judges age by, as a calendar date (yyyy-MM-dd).</summary>
        public DateOnly? Birthdate { get; set; }
        public int? Age { get; set; }
        public bool IsMinor { get; set; }
        /// <summary>The tiers this person holds ("Pro 250", "Rider Gate Fee").</summary>
        public List<string> Items { get; set; } = new();
        public bool RegistrationComplete { get; set; }

        /// <summary>True when this event requires a waiver for this person's audience AND a waiver
        /// document is actually configured (nothing to enforce otherwise).</summary>
        public bool WaiverRequired { get; set; }
        public bool WaiverSigned { get; set; }
        /// <summary>The waiver this person must sign (or, once signed, the one they did sign).</summary>
        public string? WaiverName { get; set; }
        public DateTime? SignedAtUtc { get; set; }
        public bool SignedByParent { get; set; }
        public string? GuardianName { get; set; }
        public string? SignerName { get; set; }
        public string? SignerEmail { get; set; }
        /// <summary>True when a signature image can be pulled up for this person.</summary>
        public bool HasSignatureImage { get; set; }
        /// <summary>Which ticket to pass to Order/{token}/Signature/{purchaseId} to view the image.</summary>
        public Guid? SignaturePurchaseId { get; set; }
        /// <summary>Why this person can't be checked in yet (unsigned waiver, unfinished registration).
        /// NULL when they're clear.</summary>
        public string? BlockReason { get; set; }
        /// <summary>A rider with no birthdate on file anywhere. Not a block: check their ID.</summary>
        public bool BirthdateUnknown { get; set; }
        /// <summary>Staff can send a waiver signing link to fix the block on the spot.</summary>
        public bool CanRequestSignature { get; set; }
        /// <summary>Suggested recipient for that link: the buyer when the buyer isn't this rider.</summary>
        public string? GuardianEmail { get; set; }

        // ── Yearly birthdate check ──
        /// <summary>The birthdate the rider gave (ticket), to show beside the ID's when they differ.</summary>
        public DateOnly? RecordedBirthdate { get; set; }
        /// <summary>When this rider's ID/birthdate was last checked (account first, then ticket).</summary>
        public DateTime? IdVerifiedAtUtc { get; set; }
        /// <summary>The tenant-local day of that check (yyyy-MM-dd): the date the banner and the block message show.</summary>
        public DateOnly? IdVerifiedLocalDate { get; set; }
        /// <summary>The tenant checks birthdates and this rider's check is missing or out of date.</summary>
        public bool BirthdateCheckNeeded { get; set; }
        /// <summary>'required' mode: admission is blocked until the ID is checked.</summary>
        public bool BirthdateBlocked { get; set; }
        /// <summary>The rider ticket to record an ID check against (Order/{token}/VerifyId).</summary>
        public Guid? VerifyPurchaseId { get; set; }

        // ── Turned 18 since their waiver ──
        /// <summary>An adult whose covering waiver dates from when they were a minor: blocked until they sign as an adult.</summary>
        public bool AdultWaiverNeeded { get; set; }
        public DateOnly? TurnedEighteenOn { get; set; }
        /// <summary>The rider's own email when known (their account), for a signing link addressed to them.</summary>
        public string? RiderEmail { get; set; }

        // Rider photo on file (tenant.rider_photo_mode)
        /// <summary>The tenant's mode: off | prompt | required.</summary>
        public string RiderPhotoMode { get; set; } = "off";
        public bool HasPhoto { get; set; }
        /// <summary>rider (fetch via Redemption/RiderPhoto/{RiderPhotoId}) | account (PhotoUrl) | pass.</summary>
        public string? PhotoSource { get; set; }
        public Guid? RiderPhotoId { get; set; }
        public string? PhotoUrl { get; set; }
        public DateTime? PhotoTakenAtUtc { get; set; }
        /// <summary>Mode on and no photo on file ('prompt' shows it, admits).</summary>
        public bool PhotoNeeded { get; set; }
        /// <summary>'required' mode: admission is blocked until a photo is taken.</summary>
        public bool PhotoBlocked { get; set; }
        /// <summary>The rider ticket a photo is recorded against (Redemption/RiderPhoto).</summary>
        public Guid? PhotoPurchaseId { get; set; }
    }
}
