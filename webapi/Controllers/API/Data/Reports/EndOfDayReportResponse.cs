namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>
    /// The single-day close (Z report) for one tenant-local business date.
    ///
    /// It reads v_accounting_entries, the same read model QuickBooksSyncService posts the day's
    /// journal entry from, and buckets its revenue with the same QboAccountKeys function the
    /// journal builder uses. That is the contract: what an admin closes the day on and what their
    /// accountant sees in QuickBooks are the same numbers, line for line.
    /// </summary>
    public class EndOfDayReportResponse
    {
        /// <summary>The tenant-local calendar date this report closes, yyyy-MM-dd.</summary>
        public string BusinessDate { get; set; } = null!;
        /// <summary>The IANA zone the business date is measured in.</summary>
        public string Timezone { get; set; } = null!;
        public DateTime GeneratedAtUtc { get; set; }

        /// <summary>Revenue by QuickBooks account slot, in QboAccountKeys.All order.</summary>
        public List<EndOfDayRevenueLine> Revenue { get; set; } = new();
        public EndOfDayTotals Totals { get; set; } = new();
        public List<EndOfDayTenderLine> Tenders { get; set; } = new();
        public List<EndOfDayStaffLine> Staff { get; set; } = new();
        public EndOfDayCashSection Cash { get; set; } = new();
        /// <summary>Tips earned, paid out and still owed to staff. See <see cref="EndOfDayTipsSection"/>.</summary>
        public EndOfDayTipsSection Tips { get; set; } = new();
        public EndOfDayQuickBooksStatus QuickBooks { get; set; } = new();

        /// <summary>
        /// F&amp;B tabs still open right now (count and running total). Not in the ledger yet — an
        /// open tab has taken no money — so they sit outside Totals as a "still to settle" note.
        /// Only populated when the report is for today at the track; a past day's close has none.
        /// </summary>
        public int OpenTabsCount { get; set; }
        public long OpenTabsTotalCents { get; set; }
    }
}
