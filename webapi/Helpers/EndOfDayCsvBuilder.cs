using webapi.Controllers.API.Data.Reports;

namespace webapi.Helpers
{
    /// <summary>
    /// The End of Day report as a stacked-section CSV: the same tables the screen shows, in the
    /// same order, so a printed close and an exported close read identically. Sections are
    /// separated by a blank line, which is what spreadsheet users expect from a Z report.
    /// </summary>
    public static class EndOfDayCsvBuilder
    {
        public static byte[] Build(EndOfDayReportResponse r, string tenantDisplayName)
        {
            var csv = new CsvWriter();

            csv.Title("RidePass End of Day");
            csv.Row("Venue", tenantDisplayName);
            csv.Row("Business date", r.BusinessDate);
            csv.Row("Timezone", r.Timezone);
            csv.Row("Generated (UTC)", r.GeneratedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"));

            csv.Blank();
            csv.Title("Revenue by category");
            csv.Row("Category", "Sales", "Refunds", "Gross", "Refunded", "Net gross", "Tax", "Tips", "Net revenue");
            foreach (var line in r.Revenue)
            {
                csv.Row(line.Label, line.SaleCount, line.RefundCount,
                    CsvWriter.Money(line.GrossCents), CsvWriter.Money(line.RefundCents),
                    CsvWriter.Money(line.NetGrossCents), CsvWriter.Money(line.TaxCents),
                    CsvWriter.Money(line.TipCents), CsvWriter.Money(line.NetRevenueCents));
            }
            csv.Row("Total", r.Totals.TransactionCount, r.Totals.RefundCount,
                CsvWriter.Money(r.Totals.GrossSalesCents), CsvWriter.Money(r.Totals.RefundsCents),
                CsvWriter.Money(r.Totals.NetSalesCents), CsvWriter.Money(r.Totals.TaxCents),
                CsvWriter.Money(r.Totals.TipsCents), CsvWriter.Money(r.Totals.NetRevenueCents));

            csv.Blank();
            csv.Title("Totals");
            csv.Row("Gross sales", CsvWriter.Money(r.Totals.GrossSalesCents));
            csv.Row("Refunds", CsvWriter.Money(r.Totals.RefundsCents));
            csv.Row("Net sales", CsvWriter.Money(r.Totals.NetSalesCents));
            csv.Row("Tax collected", CsvWriter.Money(r.Totals.TaxCents));
            csv.Row("Tips collected", CsvWriter.Money(r.Totals.TipsCents));
            csv.Row("Net revenue", CsvWriter.Money(r.Totals.NetRevenueCents));
            csv.Row("Gift cards sold", CsvWriter.Money(r.Totals.GiftCardsSoldCents));
            csv.Row("Gift cards redeemed", CsvWriter.Money(r.Totals.GiftCardsRedeemedCents));
            csv.Row("Deposits collected", CsvWriter.Money(r.Totals.DepositsCollectedCents));
            csv.Row("Deposits released", CsvWriter.Money(r.Totals.DepositsReleasedCents));
            csv.Row("Chargebacks lost", CsvWriter.Money(r.Totals.DisputeLossCents));
            csv.Row("Chargeback fees", CsvWriter.Money(r.Totals.DisputeFeeCents));
            csv.Row("RidePass messaging charges", CsvWriter.Money(r.Totals.PlatformChargesCents));
            csv.Row("Processing fees", CsvWriter.Money(r.Totals.StripeFeesCents));
            csv.Row("RidePass service fees", CsvWriter.Money(r.Totals.RidepassFeesCents));
            csv.Row("Net to you", CsvWriter.Money(r.Totals.NetToTenantCents));
            csv.Row("Transactions", r.Totals.TransactionCount);
            csv.Row("Refund count", r.Totals.RefundCount);

            csv.Blank();
            csv.Title("Tenders");
            csv.Row("Tender", "Count", "Amount");
            foreach (var t in r.Tenders) csv.Row(t.Label, t.Count, CsvWriter.Money(t.AmountCents));

            csv.Blank();
            csv.Title("Staff");
            csv.Row("Name", "Sales", "Refunds", "Gross", "Cash");
            if (r.Staff.Count == 0) csv.Row("No attributed sales");
            foreach (var s in r.Staff)
            {
                csv.Row(s.Name, s.SaleCount, s.RefundCount, CsvWriter.Money(s.GrossCents), CsvWriter.Money(s.CashCents));
            }

            csv.Blank();
            csv.Title("Cash");
            csv.Row("Cash sales (from the ledger)", CsvWriter.Money(r.Cash.CashSalesCents));
            csv.Row("Opening floats", CsvWriter.Money(r.Cash.OpeningFloatCents));
            csv.Row("Worker counted", CsvWriter.Money(r.Cash.WorkerCountedCents));
            csv.Row("Manager counted", CsvWriter.Money(r.Cash.ManagerCountedCents));
            if (r.Cash.Sessions.Count > 0)
            {
                csv.Blank();
                csv.Row("Sessions");
                csv.Row("Worker", "Event", "Device", "Opening float", "Status", "Opened (UTC)", "Closed (UTC)");
                foreach (var s in r.Cash.Sessions)
                {
                    csv.Row(s.UserName, s.EventTitle, s.DeviceId, CsvWriter.Money(s.OpeningFloatCents), s.Status,
                        s.OpenedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                        s.ClosedAtUtc.HasValue ? s.ClosedAtUtc.Value.ToString("yyyy-MM-dd HH:mm:ss") : "");
                }
            }
            if (r.Cash.TurnIns.Count > 0)
            {
                csv.Blank();
                csv.Row("Turn-ins");
                csv.Row("Worker", "Manager", "Expected", "Worker counted", "Manager counted", "Variance", "Status", "Submitted (UTC)", "Confirmed (UTC)", "Note");
                foreach (var t in r.Cash.TurnIns)
                {
                    csv.Row(t.WorkerName, t.ManagerName,
                        t.ExpectedCents.HasValue ? CsvWriter.Money(t.ExpectedCents.Value) : "",
                        CsvWriter.Money(t.WorkerCountedCents),
                        t.ManagerCountedCents.HasValue ? CsvWriter.Money(t.ManagerCountedCents.Value) : "",
                        t.VarianceCents.HasValue ? CsvWriter.Money(t.VarianceCents.Value) : "",
                        t.Status,
                        t.SubmittedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                        t.ConfirmedAtUtc.HasValue ? t.ConfirmedAtUtc.Value.ToString("yyyy-MM-dd HH:mm:ss") : "",
                        t.Note);
                }
            }

            csv.Blank();
            csv.Title("QuickBooks");
            csv.Row("Status", r.QuickBooks.Status);
            csv.Row("Document number", r.QuickBooks.DocNumber);
            csv.Row("Journal entry id", r.QuickBooks.JournalEntryId);
            csv.Row("Posted (UTC)", r.QuickBooks.SyncedAtUtc.HasValue ? r.QuickBooks.SyncedAtUtc.Value.ToString("yyyy-MM-dd HH:mm:ss") : "");
            csv.Row("Last error", r.QuickBooks.LastError);

            return csv.ToBytes();
        }

        public static string FilenameFor(string businessDate) => $"end-of-day-{businessDate}.csv";
    }
}
