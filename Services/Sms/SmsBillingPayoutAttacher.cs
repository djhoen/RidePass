using Microsoft.Extensions.Logging;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Sms
{
    /// <summary>
    /// Sweeps the tenant_billing_event worklist and attaches each pending event
    /// to the tenant's ledger as a negative sms_charge adjustment. The
    /// existing MonthlyPayoutDrafter then rolls these into the next payout's
    /// total_adjustment_cents, so SMS costs net against what RidePass owes
    /// the tenant, no separate Stripe invoice required.
    ///
    /// Idempotent by construction: each billing event is selected only while
    /// pushed_to_payout_at_utc is NULL, and MarkAttachedToPayout stamps both
    /// the timestamp and the resulting ledger-entry id atomically.
    /// </summary>
    public interface ISmsBillingPayoutAttacher
    {
        Task<AttachSummary> Run(int batchSize = 50, CancellationToken ct = default);
    }

    public record AttachSummary(int Attached, int Failed);

    public class SmsBillingPayoutAttacher : ISmsBillingPayoutAttacher
    {
        private readonly ITenantBillingEventRepository _billing;
        private readonly ITenantLedgerRepository _ledger;
        private readonly ILogger<SmsBillingPayoutAttacher> _logger;

        public SmsBillingPayoutAttacher(
            ITenantBillingEventRepository billing,
            ITenantLedgerRepository ledger,
            ILogger<SmsBillingPayoutAttacher> logger)
        {
            _billing = billing;
            _ledger = ledger;
            _logger = logger;
        }

        public async Task<AttachSummary> Run(int batchSize = 50, CancellationToken ct = default)
        {
            var pending = await _billing.ListPendingPayoutAttach(batchSize);
            if (pending.Count == 0) return new(0, 0);

            int attached = 0, failed = 0;
            foreach (var ev in pending)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // Negative gross + matching negative net = a pure deduction
                    // from what RidePass owes the tenant. No Stripe fee or
                    // RidePass cut associated, this isn't a sale.
                    var entry = new TenantLedgerEntry
                    {
                        TenantId = ev.TenantId,
                        EntryKind = "sms_charge",
                        SourceKind = "tenant_billing_event",
                        SourceId = ev.Id,
                        OccurredAtUtc = ev.CreatedAt,
                        GrossCents = -ev.BilledCents,
                        StripeFeeCents = 0,
                        RidepassCutCents = 0,
                        NetToTenantCents = -ev.BilledCents,
                        PaymentMethod = "stripe",
                        Memo = BuildMemo(ev),
                    };
                    var entryId = await _ledger.Insert(entry);
                    await _billing.MarkAttachedToPayout(ev.Id, entryId);
                    attached++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex,
                        "Failed to attach billing event {Id} (tenant {TenantId}) to ledger; will retry next sweep",
                        ev.Id, ev.TenantId);
                }
            }

            return new(attached, failed);
        }

        private static string BuildMemo(Services.Repositories.Data.BillingData.TenantBillingEvent ev)
        {
            // Twilio cost is informational only — the tenant gets billed at
            // ev.BilledCents regardless of underlying cost. Including it makes
            // disputes trivially answerable: "Twilio charged us X, we charged
            // you Y."
            var costDollars = ev.TwilioCostMicros / 1_000_000m;
            return $"SMS ({ev.SourceId}) — Twilio cost ${costDollars:0.0000}";
        }
    }
}
