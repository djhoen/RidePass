using Services.Repositories.Data.VoucherData;

namespace Services.Repositories.Interfaces
{
    /// <summary>Voucher batches, codes and staff allowances (Script0315). Tenant-scoped throughout.</summary>
    public interface IVoucherRepository
    {
        /// <summary>Insert the batch and one single-use, 100%-off coupon per code, in one transaction.</summary>
        Task<VoucherBatch> CreateBatch(VoucherBatch batch, IReadOnlyList<string> codes, string description);
        Task<bool> AnyCodeExists(Guid tenantId, IReadOnlyList<string> codes);
        /// <summary>Codes this person gave from their allowance since <paramref name="sinceUtc"/> (voided unused ones excluded).</summary>
        Task<int> CountIssuedSince(Guid tenantId, Guid userId, DateTime sinceUtc);
        Task<StaffVoucherAllowance?> GetAllowance(Guid tenantId, Guid userId);
        /// <summary>Set (or with null, make unlimited) a staff member's yearly allowance.</summary>
        Task SetAllowance(Guid tenantId, Guid userId, int? yearlyAllowance, Guid updatedByUserId);
        Task<List<StaffVoucherSummaryRow>> ListStaffSummary(Guid tenantId, DateTime sinceUtc);
        Task<List<VoucherBatchRow>> ListBatches(Guid tenantId, Guid? issuedByUserId, string? kind, int take);
        Task<VoucherBatch?> GetBatch(Guid tenantId, Guid batchId);
        Task<List<VoucherCodeRow>> ListCodes(Guid tenantId, Guid batchId);
        /// <summary>Void unused codes of a batch (all, or one). Returns how many were voided.</summary>
        Task<int> VoidUnusedCodes(Guid tenantId, Guid batchId, Guid? couponId);
    }
}
