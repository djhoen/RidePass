using System.Net;
using System.Text;
using Services.Helpers;
using Services.Repositories.Interfaces;

namespace Services.Alerts
{
    public class StaffAlertSweepSummary
    {
        public int TenantsConsidered { get; set; }
        public int DaysScanned { get; set; }
        public int DaysFlagged { get; set; }
        public int EmailsSent { get; set; }
        public int Failures { get; set; }
    }

    /// <summary>
    /// Once a day, per tenant, run the previous local day's recorded staff actions through the
    /// tripwires and email anything that trips.
    ///
    /// Runs hourly rather than daily for the same reason the QuickBooks sync does: tenants span
    /// timezones, so a local day closes at a different UTC moment for each one, and the sweep
    /// simply scans whatever has now finished. staff_alert_scan's unique index on
    /// (tenant_id, scan_date) is what makes that safe to repeat.
    /// </summary>
    public class StaffAlertSweep
    {
        // How far back a tenant's very first sweep reaches. Without a bound, enabling alerts on a
        // track with a year of history would send a year of emails in one tick.
        private const int MaxCatchUpDays = 3;
        // A day's worth of one tenant's actions. Far above a busy track's real volume; the cap
        // exists so a runaway cannot pull an unbounded result set into memory.
        private const int MaxActionsPerDay = 5000;
        // How far back an address counts as familiar for the "new address" rule.
        private const int AddressLookbackDays = 30;

        private readonly ITenantRepository _tenants;
        private readonly IAuditLogRepository _audit;
        private readonly IStaffAlertScanRepository _scans;
        private readonly ISmtpEmailer _emailer;

        public StaffAlertSweep(ITenantRepository tenants, IAuditLogRepository audit,
            IStaffAlertScanRepository scans, ISmtpEmailer emailer)
        {
            _tenants = tenants;
            _audit = audit;
            _scans = scans;
            _emailer = emailer;
        }

        public async Task<StaffAlertSweepSummary> ScanDueTenantsAsync(CancellationToken ct = default)
        {
            var summary = new StaffAlertSweepSummary();
            if (!_emailer.IsConfigured) return summary;

            var tenants = await _tenants.ListAll();
            foreach (var tenant in tenants)
            {
                if (ct.IsCancellationRequested) break;
                if (!tenant.StaffAlertsEnabled) continue;
                if (string.IsNullOrWhiteSpace(tenant.ContactEmail)) continue;

                summary.TenantsConsidered++;
                try
                {
                    await ScanTenant(tenant, summary, ct);
                }
                catch (Exception)
                {
                    // One tenant's bad data or unreachable mailbox must not stop the sweep for
                    // everyone else. The claimed day stays unfinished and is not retried, which is
                    // the right trade: a missed digest beats a duplicate one every hour.
                    summary.Failures++;
                }
            }
            return summary;
        }

        private async Task ScanTenant(Repositories.Data.TenantData.Tenant tenant,
            StaffAlertSweepSummary summary, CancellationToken ct)
        {
            TimeZoneInfo tz;
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(tenant.Timezone);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return;
            }

            var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
            // Only completed days: today is still in progress, so scanning it would report half a
            // day and then never look again.
            var lastComplete = todayLocal.AddDays(-1);

            var last = await _scans.GetLastScanDate(tenant.Id);
            var from = last is DateOnly l
                ? l.AddDays(1)
                : lastComplete;                                   // first run: yesterday only
            if (from < lastComplete.AddDays(-MaxCatchUpDays))
                from = lastComplete.AddDays(-MaxCatchUpDays);      // long gap: don't flood

            for (var day = from; day <= lastComplete; day = day.AddDays(1))
            {
                if (ct.IsCancellationRequested) return;

                var scanId = await _scans.TryClaimScan(tenant.Id, day);
                if (scanId is not Guid claimed) continue;          // another tick already has it

                summary.DaysScanned++;

                var fromUtc = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), tz);
                var toUtc = TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), tz);

                var rows = await _audit.ListForTenant(tenant.Id, null, null, fromUtc, toUtc, MaxActionsPerDay);
                var known = await _audit.ListKnownActorAddresses(tenant.Id, fromUtc, AddressLookbackDays);

                var flags = StaffAlertRules.Evaluate(
                    rows.Select(r => new StaffActionInput
                    {
                        ActorUserId = r.ActorUserId,
                        ActorEmail = r.ActorEmail,
                        Action = r.Action,
                        Summary = r.Summary,
                        IpAddress = r.IpAddress,
                        CreatedAtUtc = r.CreatedAt,
                        Metadata = r.Metadata,
                    }),
                    tenant.StaffAlertRefundCents,
                    known);

                if (flags.Count == 0)
                {
                    await _scans.CompleteScan(claimed, 0, null);
                    continue;
                }

                summary.DaysFlagged++;
                var sent = await _emailer.Send(
                    tenant.ContactEmail!,
                    $"Staff activity worth a look at {tenant.DisplayName} ({day:MMM d})",
                    BuildEmail(tenant.DisplayName, tenant.Subdomain, day, flags));

                if (sent) summary.EmailsSent++; else summary.Failures++;
                await _scans.CompleteScan(claimed, flags.Count, sent ? DateTime.UtcNow : null);
            }
        }

        /// <summary>Grouped by person, because "who" is the first question an owner asks and the
        /// thing that decides whether the rest of the email matters.</summary>
        internal static string BuildEmail(string trackName, string subdomain, DateOnly day,
            List<StaffAlertFlag> flags)
        {
            var sb = new StringBuilder();
            sb.Append("<div style=\"font-family:system-ui,sans-serif;max-width:640px\">");
            sb.Append($"<h2 style=\"margin-bottom:4px\">Staff activity at {Esc(trackName)}</h2>");
            sb.Append($"<p style=\"color:#555;margin-top:0\">{day:dddd, MMMM d, yyyy}</p>");
            sb.Append("<p>These came up in the day's recorded activity. Most have an ordinary "
                    + "explanation; they are listed because they are the kind of thing worth "
                    + "knowing about rather than because anything is known to be wrong.</p>");

            foreach (var group in flags.GroupBy(f => f.Who).OrderBy(g => g.Key))
            {
                sb.Append($"<h3 style=\"margin-bottom:4px\">{Esc(group.Key)}</h3><ul style=\"margin-top:4px\">");
                foreach (var f in group)
                {
                    sb.Append($"<li style=\"margin-bottom:4px\">{Esc(f.Detail)}</li>");
                }
                sb.Append("</ul>");
            }

            sb.Append($"<p style=\"margin-top:24px\"><a href=\"https://{Esc(subdomain)}.ridepass.io/Admin/StaffActivity\">"
                    + "Open Staff Activity</a> to see the full record, including what each action was and where it came from.</p>");
            sb.Append("<p style=\"color:#888;font-size:12px\">You're getting this because staff alerts are on for "
                    + "this track. Turn them off or change the refund threshold under Settings, Staff Access.</p>");
            sb.Append("</div>");
            return sb.ToString();
        }

        private static string Esc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
    }
}
