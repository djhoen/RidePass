namespace Services.Repositories.Data.VoucherData
{
    /// <summary>A batch with who issued it and how its codes stand.</summary>
    public class VoucherBatchRow : VoucherBatch
    {
        public string IssuedByName { get; set; } = "";
        public string? EventTitle { get; set; }
        public int RedeemedCount { get; set; }
        public int VoidedCount { get; set; }
    }
}
