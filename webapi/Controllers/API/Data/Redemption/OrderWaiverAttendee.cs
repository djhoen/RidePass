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
        public DateTime? Birthdate { get; set; }
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
    }
}
