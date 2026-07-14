namespace Services.Repositories.Data.PaymentData
{
    /// <summary>
    /// Check-in waiver-panel projection: one row per ticket in the gate's event+purchaser scope,
    /// denormalized with the tier's audience (so the gate knows which waiver applies) and the
    /// linked signature row (so it can show who signed, when, and on whose behalf). Rows are
    /// grouped by registrant into one card per attending person by the caller.
    /// </summary>
    public class OrderAttendeeWaiverRow
    {
        public Guid PurchaseId { get; set; }
        // Groups a rider's gate fee + race classes from one order into a single person.
        // NULL on ungrouped/legacy rows, where the ticket itself is the person.
        public Guid? RegistrantId { get; set; }
        public string TierName { get; set; } = null!;
        public string TierKind { get; set; } = null!;        // race_entry | gate_fee | spectator_pass
        public string TierAudience { get; set; } = "rider";  // rider | spectator
        public string Status { get; set; } = null!;
        public bool RegistrationComplete { get; set; }
        public string PurchaserName { get; set; } = null!;

        // Rider identity captured at registration (may be blank when registration never finished).
        public string? RiderFirstName { get; set; }
        public string? RiderLastName { get; set; }
        public DateTime? RiderBirthdate { get; set; }

        // Signature: the normalized rider_waiver_signature link, plus the legacy inline copies
        // kept on the ticket for rows that predate the link.
        public Guid? WaiverSignatureId { get; set; }
        public DateTime? WaiverSignedAt { get; set; }        // legacy inline stamp
        public bool HasInlineSignatureImage { get; set; }    // legacy inline PNG on the ticket
        public DateTime? SignatureSignedAt { get; set; }
        public bool SignatureHasImage { get; set; }
        public bool SignedByParent { get; set; }
        public string? ParentName { get; set; }
        public string? SignerName { get; set; }
        public string? SignerEmail { get; set; }
        // The attendee's DOB as captured on the signature row (guests/spectators), used when
        // the ticket itself has no rider birthdate.
        public DateTime? SignatureBirthdate { get; set; }

        // The waiver document that was actually signed (from the signature row, else the
        // ticket's pinned waiver_id). NULL when nothing has been signed.
        public Guid? SignedWaiverId { get; set; }
        public string? SignedWaiverName { get; set; }
        public string? SignedWaiverTitle { get; set; }
    }
}
