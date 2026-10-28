using Services.Repositories.Data.QuickBooksData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Per-tenant QuickBooks Desktop state: the setup row, the uploaded lists, the Desktop-edition
    /// mappings, and the export batches. The sync log itself is shared with the Online sync and
    /// stays on <see cref="IQuickBooksRepository"/>; this repository only writes to it as part of
    /// creating or voiding a batch, in the same transaction.
    ///
    /// Every method takes tenantId and puts it in the WHERE; no method derives the tenant itself.
    /// </summary>
    public interface IQuickBooksDesktopRepository
    {
        // ── Setup ────────────────────────────────────────────────────────────────────────
        Task<QuickBooksDesktopSetup?> GetSetup(Guid tenantId);
        Task CreateSetup(Guid tenantId, DateOnly syncStartDate, Guid? userId);
        Task SetSyncStartDate(Guid tenantId, DateOnly syncStartDate);
        Task DeleteSetup(Guid tenantId);

        // ── Uploaded lists ───────────────────────────────────────────────────────────────
        Task<List<QbdListItem>> ListItems(Guid tenantId, string listKind);
        /// <summary>
        /// Replace whichever lists the upload carried (a null list is left as it was), in one
        /// transaction, and stamp the setup row.
        /// </summary>
        Task ReplaceLists(Guid tenantId, string? product,
            IReadOnlyList<QbdListItem>? accounts, IReadOnlyList<QbdListItem>? classes);

        // ── Mappings (Desktop edition) ───────────────────────────────────────────────────
        /// <summary>slot key -> account full name.</summary>
        Task<Dictionary<string, string>> ListAccountMappings(Guid tenantId);
        Task UpsertAccountMapping(Guid tenantId, string mappingKey, string accountFullName);
        Task DeleteAccountMapping(Guid tenantId, string mappingKey);
        /// <summary>bucket key -> class full name.</summary>
        Task<Dictionary<string, string>> ListClassMappings(Guid tenantId);
        Task UpsertClassMapping(Guid tenantId, string bucketKey, string classFullName);
        Task DeleteClassMapping(Guid tenantId, string bucketKey);

        // ── Export batches ───────────────────────────────────────────────────────────────
        /// <summary>Business dates in the range already posted, exported, or recorded as no activity.</summary>
        Task<HashSet<DateOnly>> ListSettledDates(Guid tenantId, DateOnly from, DateOnly to);
        /// <summary>
        /// Insert the batch and its days' sync log rows in one transaction. Days already marked
        /// 'success' are never overwritten (the caller holds the tenant's export lock and has already
        /// excluded them; this is the backstop), and hitting one rolls everything back with an
        /// InvalidOperationException. A null batch records only no-activity days, with no file.
        /// </summary>
        Task<Guid?> CreateBatch(Guid tenantId, QbdExportBatch? batch, IReadOnlyList<QbdExportDay> days);
        Task<List<QbdExportBatch>> ListBatches(Guid tenantId, int take = 50);
        Task<QbdExportBatch?> GetBatch(Guid tenantId, Guid batchId);
        Task MarkDownloaded(Guid tenantId, Guid batchId);
        /// <summary>
        /// Void a batch and delete its days' sync log rows so they export again. Returns false when
        /// the batch is not this tenant's or is already void.
        /// </summary>
        Task<bool> VoidBatch(Guid tenantId, Guid batchId, Guid? userId);
    }
}
