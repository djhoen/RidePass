namespace webapi.Controllers.API.Data.GiftCard
{
    // One row of the admin gift card list. The code is MASKED (last 4 only) — the full code is a
    // spendable credential, so it's only returned by the single-card detail endpoint.
    public class GiftCardAdminListResponse
    {
        public List<Row> Items { get; set; } = new();
        public int Total { get; set; }

        public class Row
        {
            public Guid Id { get; set; }
            public string CodeMasked { get; set; } = null!;
            public int InitialAmountCents { get; set; }
            public int BalanceCents { get; set; }
            public string Status { get; set; } = null!;
            public string? RecipientName { get; set; }
            public string? RecipientEmail { get; set; }
            public string? BuyerName { get; set; }
            public bool Imported { get; set; }
            public string? ImportedFrom { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}
