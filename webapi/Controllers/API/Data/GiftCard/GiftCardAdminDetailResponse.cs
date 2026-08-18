namespace webapi.Controllers.API.Data.GiftCard
{
    public class GiftCardAdminDetailResponse
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = null!;
        public int InitialAmountCents { get; set; }
        public int BalanceCents { get; set; }
        public string Status { get; set; } = null!;
        public string DeliveryStatus { get; set; } = null!;
        public string? BuyerName { get; set; }
        public string? BuyerEmail { get; set; }
        public string? RecipientName { get; set; }
        public string? RecipientEmail { get; set; }
        public string? PersonalNote { get; set; }
        public bool Imported { get; set; }
        public string? ImportedFrom { get; set; }
        public DateTime? ImportedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<RedemptionRow> Redemptions { get; set; } = new();

        public class RedemptionRow
        {
            public string SourceKind { get; set; } = null!;
            public int AmountCents { get; set; }
            public DateTime RedeemedAt { get; set; }
        }
    }
}
