using Services.Helpers;
using Services.Repositories.Interfaces;

namespace webapi.Workers
{
    /// <summary>
    /// Sweeps for paid event tickets whose post-payment registration (rider details +
    /// required waiver) was never finished, and emails the purchaser a one-tap link to
    /// complete it. Only fires once the checkout is at least an hour old (so we don't
    /// nag someone who's mid-registration) and at most once per order.
    /// </summary>
    public class RegistrationReminderWorker : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<RegistrationReminderWorker> _logger;
        private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan MinAge = TimeSpan.FromHours(1);

        public RegistrationReminderWorker(IServiceProvider services, ILogger<RegistrationReminderWorker> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try { await RunOnce(stoppingToken); }
                catch (Exception ex) { _logger.LogError(ex, "Registration reminder tick failed"); }
                try { await Task.Delay(TickInterval, stoppingToken); }
                catch (TaskCanceledException) { return; }
            }
        }

        private async Task RunOnce(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var tickets = scope.ServiceProvider.GetRequiredService<IEventTicketPurchaseRepository>();
            var emailer = scope.ServiceProvider.GetRequiredService<ISmtpEmailer>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            if (!emailer.IsConfigured) return;   // ships dark until SES creds are set

            var cutoff = DateTime.UtcNow - MinAge;
            var due = await tickets.ListIncompleteForReminder(cutoff, take: 200);
            if (due.Count == 0) return;

            var rootDomain = config["App:RootDomain"] ?? "ridepass.io";

            // One email per order (shared PaymentIntent); free orders have no PI, so each
            // such ticket is its own group.
            foreach (var group in due.GroupBy(r => r.PaymentIntentId ?? $"free:{r.TicketId}"))
            {
                if (ct.IsCancellationRequested) return;
                var anchor = group.First();
                var link = $"https://{anchor.TenantSubdomain}.{rootDomain}/FinishRegistration/{anchor.RedemptionToken}";
                var firstName = anchor.PurchaserName?.Split(' ').FirstOrDefault() ?? "there";
                var html = $@"<p>Hi {System.Net.WebUtility.HtmlEncode(firstName)},</p>
<p>Thanks for your purchase for <strong>{System.Net.WebUtility.HtmlEncode(anchor.EventTitle)}</strong>. You're almost done —
we still need rider details{(group.Count() > 1 ? " for each entry" : "")} and a signed waiver before you can check in at the gate.</p>
<p><a href=""{link}"">Finish your registration</a> — it only takes a minute.</p>
<p>See you at the track!</p>";
                try
                {
                    var sent = await emailer.Send(anchor.PurchaserEmail,
                        $"Finish your registration for {anchor.EventTitle}", html);
                    // Only mark reminded on a successful send so a transient failure retries next sweep.
                    if (sent) await tickets.MarkRegistrationReminderSent(group.Select(x => x.TicketId));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed sending registration reminder to {Email}", anchor.PurchaserEmail);
                }
            }
        }
    }
}
