using Services.GiftCards;
using Services.Repositories.Interfaces;

namespace webapi.Workers
{
    /// <summary>
    /// Hourly background pass: finds gift cards whose ScheduledDeliveryAtUtc is in the
    /// past but DeliveryStatus is still 'pending', and sends each delivery email. The
    /// repository's MarkDelivered call (inside SendDeliveryEmail) flips status to
    /// 'delivered' so a card never gets emailed twice. Failures stay 'pending' and get
    /// retried next tick — there's no exponential backoff because the cadence (1h) is
    /// already loose enough that a transient SMTP blip recovers within a few hours.
    /// </summary>
    public class GiftCardScheduledDeliveryWorker : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<GiftCardScheduledDeliveryWorker> _logger;
        private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(60);

        public GiftCardScheduledDeliveryWorker(
            IServiceProvider services,
            ILogger<GiftCardScheduledDeliveryWorker> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Stagger startup so the first tick doesn't pile on immediately after boot.
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnce(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gift-card delivery tick failed");
                }
                try { await Task.Delay(TickInterval, stoppingToken); }
                catch (TaskCanceledException) { return; }
            }
        }

        private async Task RunOnce(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IGiftCardRepository>();
            var delivery = scope.ServiceProvider.GetRequiredService<IGiftCardDeliveryService>();

            var due = await repo.ListPendingDelivery(DateTime.UtcNow, take: 200);
            if (due.Count == 0) return;

            _logger.LogInformation("Delivering {Count} scheduled gift cards", due.Count);
            foreach (var card in due)
            {
                if (ct.IsCancellationRequested) return;
                await delivery.SendDeliveryEmail(card);
            }
        }
    }
}
