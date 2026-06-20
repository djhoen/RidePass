using Services.Repositories.Interfaces;
using Services.Waitlist;

namespace webapi.Workers
{
    /// <summary>
    /// Sweeps every minute for promoted waitlist entries whose confirm deadline has
    /// passed. Marks them expired and rolls the spot to the next person in line via
    /// WaitlistPromoter. Pre-paid alternates auto-confirm at promotion time so they
    /// never appear in this sweep.
    /// </summary>
    public class WaitlistExpiryWorker : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<WaitlistExpiryWorker> _logger;
        private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

        public WaitlistExpiryWorker(IServiceProvider services, ILogger<WaitlistExpiryWorker> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Stagger startup so we don't hammer the DB right when the API boots.
            try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try { await RunOnce(stoppingToken); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Waitlist expiry tick failed");
                }
                try { await Task.Delay(TickInterval, stoppingToken); }
                catch (TaskCanceledException) { return; }
            }
        }

        private async Task RunOnce(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IEventWaitlistRepository>();
            var promoter = scope.ServiceProvider.GetRequiredService<IWaitlistPromoter>();

            var due = await repo.ListExpired(DateTime.UtcNow, take: 100);
            if (due.Count == 0) return;

            _logger.LogInformation("Expiring {Count} waitlist promotions", due.Count);
            foreach (var entry in due)
            {
                if (ct.IsCancellationRequested) return;
                await repo.MarkExpired(entry.Id);
                // Roll to next person in the same bucket. PromoteNext handles the
                // empty-bucket case as a no-op.
                await promoter.PromoteNext(entry.EventId, entry.TierId, entry.LadderGroup);
            }
        }
    }
}
