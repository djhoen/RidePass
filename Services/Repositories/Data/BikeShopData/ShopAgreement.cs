namespace Services.Repositories.Data.BikeShopData
{
    /// <summary>A versioned shop document a customer signs: the rental agreement, or the
    /// authorization to perform a repair. One active version per kind per tenant; superseded
    /// versions stay so old signatures keep meaning what they meant.</summary>
    public class ShopAgreement
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Kind { get; set; } = "rental_agreement";   // rental_agreement | work_order_terms
        public int Version { get; set; } = 1;
        public string Title { get; set; } = null!;
        public string Body { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>A captured signature against one agreement version, attached to a work order or
    /// a rental (exactly one). Signed on the shop's tablet with the customer present.</summary>
    public class ShopAgreementSignature
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid AgreementId { get; set; }
        public Guid? WorkOrderId { get; set; }
        public Guid? RentalId { get; set; }
        public int AgreementVersion { get; set; }
        public string SignerName { get; set; } = null!;
        public string? SignerEmail { get; set; }
        public string SignatureDataUrl { get; set; } = null!;
        public DateTime SignedAt { get; set; }
        public string? IpAddress { get; set; }
        public Guid? WitnessedByUserId { get; set; }
    }

    /// <summary>Whether a rental may be handed over. Both gates must pass: the track's liability
    /// waiver and the shop's rental agreement. Carries the reasons so the counter can say exactly
    /// what is missing instead of a bare refusal.</summary>
    public class RentalCheckoutReadiness
    {
        public bool AgreementRequired { get; set; }
        public bool AgreementSigned { get; set; }
        public bool WaiverRequired { get; set; }

        /// <summary>Riders who must sign (shop_rental.riders_required) and how many have.</summary>
        public int RidersRequired { get; set; } = 1;
        public int RidersSigned { get; set; }
        /// <summary>Who has signed so far, so the counter can see who is still outstanding.</summary>
        public List<RentalSignerInfo> Signers { get; set; } = new();

        /// <summary>
        /// EVERY rider must have signed, not merely one. A rental of three bikes for three kids
        /// needs three signatures; accepting one is how unwaivered riders used to get gear.
        /// </summary>
        public bool WaiverSigned => !WaiverRequired || RidersSigned >= RidersRequired;
        public int RidersOutstanding => Math.Max(0, RidersRequired - RidersSigned);

        public bool CanCheckOut => (!AgreementRequired || AgreementSigned) && WaiverSigned;
    }
}
