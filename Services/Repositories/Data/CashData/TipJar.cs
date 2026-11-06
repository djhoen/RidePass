namespace Services.Repositories.Data.CashData
{
    // The tip jar (Script0298): cash outside the register that the staff who worked a day split
    // and take home straight after the shift. One split per time the jar is counted, with a
    // share per person. No ledger row and no liability behind it: the jar is money the staff
    // already took, and the record exists so the daily tips sheet is complete.
    public class TipJarSplit
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public DateOnly BusinessDate { get; set; }
        public int TotalCents { get; set; }
        public string? Note { get; set; }
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TipJarShare
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid SplitId { get; set; }
        public Guid UserId { get; set; }
        public int AmountCents { get; set; }
    }

    // A split with the recorder's display name resolved, for the Tips owed page.
    public class TipJarSplitRow : TipJarSplit
    {
        public string? CreatedByName { get; set; }
        public string? CreatedByEmail { get; set; }
    }

    // A share with the recipient's display name resolved.
    public class TipJarShareRow : TipJarShare
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
    }
}
