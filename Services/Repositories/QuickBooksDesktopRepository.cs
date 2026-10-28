using Dapper;
using Npgsql;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.QuickBooksData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class QuickBooksDesktopRepository : IQuickBooksDesktopRepository
    {
        private const string SetupColumns = @"
            id, tenant_id AS TenantId, sync_start_date AS SyncStartDate,
            company_product AS CompanyProduct, lists_uploaded_at_utc AS ListsUploadedAtUtc,
            created_by_user_id AS CreatedByUserId, created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string ListItemColumns = @"
            id, tenant_id AS TenantId, list_kind AS ListKind, full_name AS FullName,
            account_type AS AccountType, account_number AS AccountNumber, is_hidden AS IsHidden,
            created_at AS CreatedAt";

        // file_content is left out of list reads: it can run to hundreds of KB per batch and only
        // the download needs it.
        private const string BatchSummaryColumns = @"
            id, tenant_id AS TenantId, from_date AS FromDate, to_date AS ToDate, day_count AS DayCount,
            total_debits_cents AS TotalDebitsCents, created_by_user_id AS CreatedByUserId,
            download_count AS DownloadCount, last_downloaded_at_utc AS LastDownloadedAtUtc,
            voided_at_utc AS VoidedAtUtc, voided_by_user_id AS VoidedByUserId, created_at AS CreatedAt";

        private readonly IDbHelper _db;

        public QuickBooksDesktopRepository(IDbHelper db) => _db = db;

        // ── Setup ────────────────────────────────────────────────────────────────────────

        public async Task<QuickBooksDesktopSetup?> GetSetup(Guid tenantId)
        {
            var sql = $"SELECT {SetupColumns} FROM tenant_quickbooks_desktop WHERE tenant_id = @tenantId";
            return (await _db.Query<QuickBooksDesktopSetup>(sql, new { tenantId })).FirstOrDefault();
        }

        public async Task CreateSetup(Guid tenantId, DateOnly syncStartDate, Guid? userId)
        {
            // DO NOTHING rather than update: a second "Use QuickBooks Desktop" click must not move
            // the start date of a setup that may already have exported days.
            const string sql = @"
                INSERT INTO tenant_quickbooks_desktop (tenant_id, sync_start_date, created_by_user_id)
                VALUES (@tenantId, @syncStartDate, @userId)
                ON CONFLICT (tenant_id) DO NOTHING";
            await _db.Execute(sql, new { tenantId, syncStartDate, userId });
        }

        public async Task SetSyncStartDate(Guid tenantId, DateOnly syncStartDate)
        {
            await _db.Execute("UPDATE tenant_quickbooks_desktop SET sync_start_date = @syncStartDate WHERE tenant_id = @tenantId",
                new { tenantId, syncStartDate });
        }

        public async Task DeleteSetup(Guid tenantId)
        {
            // Lists, mappings, batches and the sync log survive on purpose, exactly as an Online
            // disconnect keeps its mapping: coming back restores the setup, and the log of what was
            // already exported stays the double-posting guard.
            await _db.Execute("DELETE FROM tenant_quickbooks_desktop WHERE tenant_id = @tenantId", new { tenantId });
        }

        // ── Uploaded lists ───────────────────────────────────────────────────────────────

        public async Task<List<QbdListItem>> ListItems(Guid tenantId, string listKind)
        {
            var sql = $@"
                SELECT {ListItemColumns} FROM qbd_list_item
                WHERE tenant_id = @tenantId AND list_kind = @listKind
                ORDER BY full_name";
            return (await _db.Query<QbdListItem>(sql, new { tenantId, listKind })).ToList();
        }

        public async Task ReplaceLists(Guid tenantId, string? product,
            IReadOnlyList<QbdListItem>? accounts, IReadOnlyList<QbdListItem>? classes)
        {
            var statements = new List<(string Sql, object? Param)>();

            void Replace(string kind, IReadOnlyList<QbdListItem> items)
            {
                // Scoped to this tenant AND this list: an upload carrying only classes must leave
                // the chart of accounts exactly as it was.
                statements.Add(("DELETE FROM qbd_list_item WHERE tenant_id = @tenantId AND list_kind = @kind",
                    new { tenantId, kind }));
                if (items.Count == 0) return;
                // One insert per list via parallel arrays rather than a statement per row; a real
                // chart of accounts is a few hundred rows.
                statements.Add((@"
                    INSERT INTO qbd_list_item (tenant_id, list_kind, full_name, account_type, account_number, is_hidden)
                    SELECT @tenantId, @kind, u.full_name, u.account_type, u.account_number, u.is_hidden
                    FROM unnest(@names::text[], @types::text[], @numbers::text[], @hidden::boolean[]) AS u(full_name, account_type, account_number, is_hidden)
                    ON CONFLICT (tenant_id, list_kind, full_name) DO NOTHING",
                    new
                    {
                        tenantId,
                        kind,
                        names = items.Select(i => i.FullName).ToArray(),
                        types = items.Select(i => i.AccountType).ToArray(),
                        numbers = items.Select(i => i.AccountNumber).ToArray(),
                        hidden = items.Select(i => i.IsHidden).ToArray(),
                    }));
            }

            if (accounts is not null) Replace("account", accounts);
            if (classes is not null) Replace("class", classes);

            statements.Add((@"
                UPDATE tenant_quickbooks_desktop
                   SET lists_uploaded_at_utc = now(),
                       company_product = COALESCE(@product, company_product)
                 WHERE tenant_id = @tenantId", new { tenantId, product }));

            await _db.ExecuteBatch(statements);
        }

        // ── Mappings (Desktop edition) ───────────────────────────────────────────────────

        public async Task<Dictionary<string, string>> ListAccountMappings(Guid tenantId)
        {
            var rows = await _db.Query<(string Key, string Name)>(
                "SELECT mapping_key, account_full_name FROM qbd_account_mapping WHERE tenant_id = @tenantId",
                new { tenantId });
            return rows.ToDictionary(r => r.Key, r => r.Name, StringComparer.Ordinal);
        }

        public async Task UpsertAccountMapping(Guid tenantId, string mappingKey, string accountFullName)
        {
            const string sql = @"
                INSERT INTO qbd_account_mapping (tenant_id, mapping_key, account_full_name)
                VALUES (@tenantId, @mappingKey, @accountFullName)
                ON CONFLICT (tenant_id, mapping_key) DO UPDATE SET account_full_name = EXCLUDED.account_full_name";
            await _db.Execute(sql, new { tenantId, mappingKey, accountFullName });
        }

        public async Task DeleteAccountMapping(Guid tenantId, string mappingKey)
        {
            await _db.Execute("DELETE FROM qbd_account_mapping WHERE tenant_id = @tenantId AND mapping_key = @mappingKey",
                new { tenantId, mappingKey });
        }

        public async Task<Dictionary<string, string>> ListClassMappings(Guid tenantId)
        {
            var rows = await _db.Query<(string Key, string Name)>(
                "SELECT bucket_key, class_full_name FROM qbd_class_mapping WHERE tenant_id = @tenantId",
                new { tenantId });
            return rows.ToDictionary(r => r.Key, r => r.Name, StringComparer.Ordinal);
        }

        public async Task UpsertClassMapping(Guid tenantId, string bucketKey, string classFullName)
        {
            const string sql = @"
                INSERT INTO qbd_class_mapping (tenant_id, bucket_key, class_full_name)
                VALUES (@tenantId, @bucketKey, @classFullName)
                ON CONFLICT (tenant_id, bucket_key) DO UPDATE SET class_full_name = EXCLUDED.class_full_name";
            await _db.Execute(sql, new { tenantId, bucketKey, classFullName });
        }

        public async Task DeleteClassMapping(Guid tenantId, string bucketKey)
        {
            await _db.Execute("DELETE FROM qbd_class_mapping WHERE tenant_id = @tenantId AND bucket_key = @bucketKey",
                new { tenantId, bucketKey });
        }

        // ── Export batches ───────────────────────────────────────────────────────────────

        public async Task<HashSet<DateOnly>> ListSettledDates(Guid tenantId, DateOnly from, DateOnly to)
        {
            // no_activity counts as settled too: a day whose sales netted to zero would otherwise be
            // rebuilt and re-recorded on every export forever. Activity is dated by when it happened,
            // so a settled day never gains new rows later.
            var rows = await _db.Query<DateOnly>(@"
                SELECT business_date FROM qbo_sync_log
                WHERE tenant_id = @tenantId AND status IN ('success', 'no_activity')
                  AND business_date BETWEEN @from AND @to", new { tenantId, from, to });
            return rows.ToHashSet();
        }

        public async Task<Guid?> CreateBatch(Guid tenantId, QbdExportBatch? batch, IReadOnlyList<QbdExportDay> days)
        {
            // Its own transaction rather than ExecuteBatch, because every day's claim has to be
            // CHECKED: a day that turns out to be already posted must abort the whole batch, not
            // leave a file whose days are half-claimed by someone else.
            Guid? batchId = batch is null ? null : Guid.NewGuid();
            await using var conn = new NpgsqlConnection(_db.ConnectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                if (batch is not null) await conn.ExecuteAsync(@"
                    INSERT INTO qbd_export_batch
                        (id, tenant_id, from_date, to_date, day_count, total_debits_cents, file_content, created_by_user_id)
                    VALUES
                        (@batchId, @TenantId, @FromDate, @ToDate, @DayCount, @TotalDebitsCents, @FileContent, @CreatedByUserId)",
                    new
                    {
                        batchId, TenantId = tenantId, batch.FromDate, batch.ToDate, batch.DayCount,
                        batch.TotalDebitsCents, batch.FileContent, batch.CreatedByUserId,
                    }, tx);

                foreach (var day in days)
                {
                    // Same upsert shape as QuickBooksRepository.TryClaimBusinessDate: an existing row
                    // is only overwritten when it did NOT succeed (an old Online failure, an earlier
                    // no_activity). A 'success' row matches nothing and the count comes back 0.
                    var docNumber = day.Status == "success" ? $"RP-{day.BusinessDate:yyyyMMdd}" : null;
                    var affected = await conn.ExecuteAsync(@"
                        INSERT INTO qbo_sync_log
                            (tenant_id, business_date, status, qbo_doc_number, entry_count, total_debits_cents,
                             attempt_count, synced_at_utc, export_batch_id, last_error)
                        VALUES
                            (@tenantId, @businessDate, @status, @docNumber, @entryCount, @totalDebits,
                             1, now(), @batchId, NULL)
                        ON CONFLICT (tenant_id, business_date) DO UPDATE SET
                            status             = EXCLUDED.status,
                            qbo_doc_number     = EXCLUDED.qbo_doc_number,
                            entry_count        = EXCLUDED.entry_count,
                            total_debits_cents = EXCLUDED.total_debits_cents,
                            attempt_count      = qbo_sync_log.attempt_count + 1,
                            synced_at_utc      = EXCLUDED.synced_at_utc,
                            export_batch_id    = EXCLUDED.export_batch_id,
                            last_error         = NULL
                        WHERE qbo_sync_log.status <> 'success'",
                        new
                        {
                            tenantId,
                            businessDate = day.BusinessDate,
                            status = day.Status,
                            docNumber,
                            entryCount = day.EntryCount,
                            totalDebits = day.TotalDebitsCents,
                            // A no-activity day is recorded so it isn't rescanned, but it isn't IN
                            // the file, so voiding the batch need not release it.
                            batchId = day.Status == "success" ? batchId : (Guid?)null,
                        }, tx);

                    if (affected != 1)
                    {
                        throw new InvalidOperationException(
                            $"{day.BusinessDate:yyyy-MM-dd} was already posted to QuickBooks while this export was being built. Nothing was exported; try again.");
                    }
                }

                await tx.CommitAsync();
                return batchId;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<List<QbdExportBatch>> ListBatches(Guid tenantId, int take = 50)
        {
            var sql = $@"
                SELECT {BatchSummaryColumns} FROM qbd_export_batch
                WHERE tenant_id = @tenantId
                ORDER BY created_at DESC
                LIMIT @take";
            return (await _db.Query<QbdExportBatch>(sql, new { tenantId, take })).ToList();
        }

        public async Task<QbdExportBatch?> GetBatch(Guid tenantId, Guid batchId)
        {
            var sql = $@"
                SELECT {BatchSummaryColumns}, file_content AS FileContent
                FROM qbd_export_batch
                WHERE id = @batchId AND tenant_id = @tenantId";
            return (await _db.Query<QbdExportBatch>(sql, new { tenantId, batchId })).FirstOrDefault();
        }

        public async Task MarkDownloaded(Guid tenantId, Guid batchId)
        {
            await _db.Execute(@"
                UPDATE qbd_export_batch
                   SET download_count = download_count + 1, last_downloaded_at_utc = now()
                 WHERE id = @batchId AND tenant_id = @tenantId", new { tenantId, batchId });
        }

        public async Task<bool> VoidBatch(Guid tenantId, Guid batchId, Guid? userId)
        {
            await using var conn = new NpgsqlConnection(_db.ConnectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                var voided = await conn.ExecuteAsync(@"
                    UPDATE qbd_export_batch
                       SET voided_at_utc = now(), voided_by_user_id = @userId
                     WHERE id = @batchId AND tenant_id = @tenantId AND voided_at_utc IS NULL",
                    new { tenantId, batchId, userId }, tx);
                if (voided != 1)
                {
                    await tx.RollbackAsync();
                    return false;
                }

                // Release exactly this batch's days, nothing else: scoped by tenant AND batch id.
                await conn.ExecuteAsync(@"
                    DELETE FROM qbo_sync_log
                     WHERE tenant_id = @tenantId AND export_batch_id = @batchId",
                    new { tenantId, batchId }, tx);

                await tx.CommitAsync();
                return true;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }
}
