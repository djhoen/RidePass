using System.Text;
using Services.Repositories.Data.PaymentData;

namespace webapi.Helpers
{
    public static class PayoutCsvBuilder
    {
        public static string Build(
            TenantPayout payout,
            IEnumerable<TenantLedgerEntry> entries,
            string tenantSubdomain,
            string tenantDisplayName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RidePass Payout Statement");
            sb.AppendLine($"Tenant,{Esc(tenantDisplayName)} ({Esc(tenantSubdomain)})");
            sb.AppendLine($"Period (UTC),{payout.PeriodStartUtc:yyyy-MM-dd} to {payout.PeriodEndUtc:yyyy-MM-dd}");
            sb.AppendLine($"Status,{payout.Status}");
            sb.AppendLine($"Date paid (UTC),{(payout.PayoutDateUtc.HasValue ? payout.PayoutDateUtc.Value.ToString("yyyy-MM-dd") : "")}");
            sb.AppendLine($"Reference,{Esc(payout.ExternalReference ?? "")}");
            sb.AppendLine($"Memo,{Esc(payout.Memo ?? "")}");
            sb.AppendLine();
            sb.AppendLine($"Total gross,{Money(payout.TotalGrossCents)}");
            sb.AppendLine($"Total Stripe fees,{Money(payout.TotalStripeFeeCents)}");
            sb.AppendLine($"Total RidePass cut,{Money(payout.TotalRidepassCutCents)}");
            sb.AppendLine($"Total adjustments,{Money(payout.TotalAdjustmentCents)}");
            sb.AppendLine($"Net paid,{Money(payout.NetPaidCents)}");
            sb.AppendLine();
            sb.AppendLine("Date (UTC),Kind,Source,Gross,Stripe Fee,RidePass Cut,Net,Stripe PaymentIntent,Memo");
            foreach (var e in entries.OrderBy(x => x.OccurredAtUtc))
            {
                sb.Append(e.OccurredAtUtc.ToString("yyyy-MM-dd HH:mm:ss")).Append(',')
                  .Append(e.EntryKind).Append(',')
                  .Append(Esc(e.SourceKind ?? "")).Append(',')
                  .Append(Money(e.GrossCents)).Append(',')
                  .Append(Money(e.StripeFeeCents)).Append(',')
                  .Append(Money(e.RidepassCutCents)).Append(',')
                  .Append(Money(e.NetToTenantCents)).Append(',')
                  .Append(Esc(e.StripePaymentIntentId ?? "")).Append(',')
                  .Append(Esc(e.Memo ?? ""))
                  .AppendLine();
            }
            return sb.ToString();
        }

        public static string FilenameFor(TenantPayout payout, string tenantSubdomain) =>
            $"payout-{(string.IsNullOrEmpty(tenantSubdomain) ? "tenant" : tenantSubdomain)}-{payout.PeriodStartUtc:yyyyMMdd}.csv";

        private static string Esc(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            if (v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r'))
            {
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            }
            return v;
        }

        private static string Money(int cents) => (cents / 100m).ToString("0.00");
    }
}
