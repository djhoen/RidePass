using Services.Helpers.Interfaces;
using Services.Repositories.Data.QuickBooksData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class AccountingEntryRepository : IAccountingEntryRepository
    {
        private const string Columns = @"
            tenant_id AS TenantId, ledger_entry_id AS LedgerEntryId,
            entry_kind AS EntryKind, source_kind AS SourceKind, source_id AS SourceId,
            occurred_at_utc AS OccurredAtUtc, business_date AS BusinessDate,
            payment_method AS PaymentMethod,
            gross_cents AS GrossCents, stripe_fee_cents AS StripeFeeCents,
            ridepass_cut_cents AS RidepassCutCents, net_to_tenant_cents AS NetToTenantCents,
            tax_cents AS TaxCents, tip_cents AS TipCents,
            gift_card_applied_cents AS GiftCardAppliedCents,
            revenue_key_override AS RevenueKeyOverride";

        private readonly IDbHelper _db;

        public AccountingEntryRepository(IDbHelper db) => _db = db;

        public async Task<List<AccountingEntry>> ListForBusinessDate(Guid tenantId, DateOnly businessDate)
        {
            var sql = $@"
                SELECT {Columns}
                FROM v_accounting_entries
                WHERE tenant_id = @tenantId
                  AND business_date = @businessDate
                ORDER BY occurred_at_utc";
            return (await _db.Query<AccountingEntry>(sql, new { tenantId, businessDate })).ToList();
        }

        public async Task<List<DateOnly>> ListBusinessDatesWithActivity(Guid tenantId, DateOnly fromDate, DateOnly toDate)
        {
            var sql = @"
                SELECT DISTINCT business_date
                FROM v_accounting_entries
                WHERE tenant_id = @tenantId
                  AND business_date >= @fromDate
                  AND business_date <= @toDate
                ORDER BY business_date";
            return (await _db.Query<DateOnly>(sql, new { tenantId, fromDate, toDate })).ToList();
        }
    }
}
