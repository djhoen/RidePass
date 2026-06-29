using Services.Payments;
using Services.Repositories.Interfaces;
using webapi.Payments;

namespace webapi.Workers
{
    /// <summary>
    /// Catches checkout purchases whose Stripe webhook never arrived. Every few minutes it
    /// finds PaymentIntents with still-pending purchase rows past a grace period, asks Stripe
    /// for the real status, and:
    ///   - succeeded  -> finalizes via the shared finalizer (flips to paid, ledger, emails,
    ///                   rewards) so a missed webhook can't leave a paid customer stuck.
    ///   - canceled   -> fails the rows, which frees the inventory the pending rows were holding.
    ///   - abandoned  -> a PI still awaiting a payment method long after creation (buyer closed
    ///                   the tab) is failed once past the abandon cutoff, again freeing inventory.
    /// All finalizer calls are idempotent, so racing with a late webhook is safe.
    /// </summary>
    public class PendingPurchaseReconciler : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<PendingPurchaseReconciler> _logger;

        private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);
        // Leave fresh carts alone — the buyer may still be entering card details / completing 3DS.
        private static readonly TimeSpan PendingGrace = TimeSpan.FromMinutes(20);
        // A PI still not paid this long after creation is treated as abandoned even if Stripe
        // still reports it awaiting a payment method, so the held inventory gets released.
        private static readonly TimeSpan AbandonCutoff = TimeSpan.FromHours(2);

        public PendingPurchaseReconciler(IServiceProvider services, ILogger<PendingPurchaseReconciler> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Stagger startup so we don't pile onto the DB right as the API boots.
            try { await Task.Delay(TimeSpan.FromSeconds(40), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try { await RunOnce(stoppingToken); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Pending-purchase reconciliation tick failed");
                }
                try { await Task.Delay(TickInterval, stoppingToken); }
                catch (TaskCanceledException) { return; }
            }
        }

        private async Task RunOnce(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IPendingPurchaseRepository>();
            var payments = scope.ServiceProvider.GetRequiredService<IPaymentProvider>();
            var finalizer = scope.ServiceProvider.GetRequiredService<IStripePurchaseFinalizer>();

            var now = DateTime.UtcNow;

            // Reconcile against Stripe FIRST (concession sales are included in this union), so any
            // concession card/online payment that succeeded but missed its webhook is finalized here
            // (flips to paid, writes the ledger entry, assigns the order number) before the blind
            // inventory sweep below could fail it.
            var stale = await repo.ListStalePendingPaymentIntents(now - PendingGrace, take: 200);
            foreach (var pi in stale)
            {
                if (ct.IsCancellationRequested) return;

                var status = await payments.GetPaymentIntentStatusAsync(pi.PaymentIntentId, pi.ConnectedAccountId, ct);
                if (status is null)
                {
                    // Stripe not configured, or the fetch failed — can't safely decide, skip.
                    continue;
                }

                if (status == "succeeded")
                {
                    _logger.LogWarning(
                        "Reconciler finalizing PaymentIntent {Pi}: succeeded at Stripe but rows were still pending (missed webhook).",
                        pi.PaymentIntentId);
                    await finalizer.ProcessPaymentIntentAsync(pi.PaymentIntentId, "payment_intent.succeeded", ct);
                }
                else if (status == "canceled")
                {
                    await finalizer.ProcessPaymentIntentAsync(pi.PaymentIntentId, "payment_intent.payment_failed", ct);
                }
                else if (status != "processing" && pi.OldestCreatedAtUtc < now - AbandonCutoff)
                {
                    // requires_payment_method / requires_confirmation / requires_action that never
                    // completed: abandoned. Cancel the PI at Stripe FIRST so it can never be charged
                    // later — otherwise a late completion would fire payment_intent.succeeded and the
                    // finalizer would revive the failed rows, double-booking inventory we just freed.
                    var afterCancel = await payments.CancelPaymentIntentAsync(pi.PaymentIntentId, pi.ConnectedAccountId, ct);
                    if (afterCancel == "succeeded")
                    {
                        // Buyer completed payment in the cancel race — fulfill instead of failing.
                        _logger.LogWarning(
                            "Reconciler: PaymentIntent {Pi} succeeded during the cancel race; finalizing.",
                            pi.PaymentIntentId);
                        await finalizer.ProcessPaymentIntentAsync(pi.PaymentIntentId, "payment_intent.succeeded", ct);
                    }
                    else if (afterCancel == "canceled")
                    {
                        _logger.LogInformation(
                            "Reconciler failed abandoned PaymentIntent {Pi} (canceled at Stripe; created {Created:o}).",
                            pi.PaymentIntentId, pi.OldestCreatedAtUtc);
                        await finalizer.ProcessPaymentIntentAsync(pi.PaymentIntentId, "payment_intent.payment_failed", ct);
                    }
                    // else (null / unexpected state): leave pending and retry on a later tick.
                }
            }

            // Now release inventory held by abandoned concession card sales (reader cancelled /
            // walk-off): a pending concession sale older than 30 min is failed so its variant stock
            // frees up. Runs AFTER the Stripe reconciliation above, so any sale that actually
            // succeeded was already finalized (no longer pending) and is safe from this blind sweep;
            // only genuine walk-offs remain pending to be failed here.
            try
            {
                var concessions = scope.ServiceProvider.GetRequiredService<IConcessionRepository>();
                var swept = await concessions.FailStalePendingSales(now - TimeSpan.FromMinutes(30));
                if (swept > 0) _logger.LogInformation("Reconciler failed {Count} stale pending concession sales.", swept);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Concession stale-pending sweep failed.");
            }
        }
    }
}
