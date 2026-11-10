namespace Services.Repositories.Data.VoucherData
{
    /// <summary>A set of voucher codes issued together (Script0315): an employee's gift, or a vendor list.</summary>
    public class VoucherBatch
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Kind { get; set; } = VoucherKinds.Employee;
        public Guid IssuedByUserId { get; set; }
        public string? Label { get; set; }
        public string? RecipientEmail { get; set; }
        public string? Note { get; set; }
        public int CodeCount { get; set; }
        public Guid? EventId { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
