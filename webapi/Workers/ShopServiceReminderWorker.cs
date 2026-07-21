using Services.Helpers;
using Services.Repositories.Interfaces;

namespace webapi.Workers
{
    /// <summary>
    /// Emails a follow-up service reminder some months after a repair was picked up ("time for
    /// another look at that bike"). The interval is per tenant and defaults to OFF, so a track
    /// opts in rather than discovering it mailed its customers.
    ///
    /// Each reminder is claimed before sending, so a slow send or an overlapping tick can't mail
    /// the same customer twice months later, which is the failure everyone actually notices.
    /// </summary>
    public class ShopServiceReminderWorker : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<ShopServiceReminderWorker> _logger;
        // Reminders are months out; nothing is gained by sweeping often.
        private static readonly TimeSpan TickInterval = TimeSpan.FromHours(6);

        public ShopServiceReminderWorker(IServiceProvider services, ILogger<ShopServiceReminderWorker> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Let the app finish starting before the first sweep.
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try { await RunOnce(stoppingToken); }
                catch (Exception ex) { _logger.LogError(ex, "Shop service reminder tick failed"); }
                try { await Task.Delay(TickInterval, stoppingToken); }
                catch (TaskCanceledException) { return; }
            }
        }

        private async Task RunOnce(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var shop = scope.ServiceProvider.GetRequiredService<IBikeShopRepository>();
            var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
            var emailer = scope.ServiceProvider.GetRequiredService<ISmtpEmailer>();
            var suppression = scope.ServiceProvider.GetRequiredService<IEmailSuppressionRepository>();

            if (!emailer.IsConfigured) return;   // ships dark until mail creds are set

            var due = await shop.ListDueServiceReminders(take: 200);
            if (due.Count == 0) return;

            foreach (var wo in due)
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    var tenant = await tenants.GetById(wo.TenantId);
                    // A tenant that turned the shop (or reminders) off since pickup shouldn't
                    // still be mailing. Claim it anyway so it stops coming back around.
                    if (tenant is null || !tenant.BikeShopEnabled || tenant.ShopServiceReminderDays <= 0)
                    {
                        await shop.TryClaimServiceReminder(wo.Id);
                        continue;
                    }

                    // This is marketing-adjacent, so honour opt-outs and hard bounces. Claim it
                    // either way: a suppressed address will still be suppressed next sweep.
                    if (await suppression.IsSuppressed(wo.CustomerEmail!, wo.TenantId, marketing: true))
                    {
                        await shop.TryClaimServiceReminder(wo.Id);
                        continue;
                    }

                    // Claim BEFORE sending: a duplicate reminder is worse than a missed one.
                    if (!await shop.TryClaimServiceReminder(wo.Id)) continue;

                    static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);
                    var bike = wo.CustomerBikeDesc ?? "your bike";
                    var html =
                        $"<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:480px\">" +
                        $"<h2 style=\"margin:0 0 8px\">{Enc(tenant.DisplayName)}</h2>" +
                        $"<p>Hi {Enc(wo.CustomerName)},</p>" +
                        $"<p>It's been a while since we last serviced {Enc(bike)}. If it's due for " +
                        $"a check over, we're happy to take a look.</p>" +
                        $"<p style=\"font-size:12px;color:#666\">Reply to this email or call the shop to book it in.</p></div>";

                    if (!await emailer.Send(wo.CustomerEmail!,
                            $"{tenant.DisplayName}: time for a service?",
                            html, null, Services.Email.TenantEmailIdentity.For(tenant)))
                    {
                        _logger.LogWarning("Service reminder send failed for work order {Id}", wo.Id);
                    }
                }
                catch (Exception ex)
                {
                    // One bad row must not stall the sweep for everyone else.
                    _logger.LogError(ex, "Service reminder failed for work order {Id}", wo.Id);
                }
            }
        }
    }
}
