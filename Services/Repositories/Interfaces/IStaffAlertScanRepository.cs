namespace Services.Repositories.Interfaces
{
    public interface IStaffAlertScanRepository
    {
        /// <summary>
        /// Claim a tenant-local day for scanning. Returns the new row's id, or null when the day
        /// was already claimed. The claim happens BEFORE any evaluation or sending, so two
        /// overlapping sweep ticks cannot both email the same day: the second one loses the
        /// insert race and stops.
        /// </summary>
        Task<Guid?> TryClaimScan(Guid tenantId, DateOnly scanDate);

        /// <summary>Record what the claimed scan found. sentAt stays null when nothing tripped
        /// (so nothing was sent) or when the send failed.</summary>
        Task CompleteScan(Guid scanId, int flaggedCount, DateTime? sentAtUtc);

        /// <summary>The most recent local day already scanned for this tenant, or null when the
        /// tenant has never been scanned. Bounds how far back a first run reaches.</summary>
        Task<DateOnly?> GetLastScanDate(Guid tenantId);
    }
}
