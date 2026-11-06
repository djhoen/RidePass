namespace webapi.Controllers.API.Data.Cash
{
    // The manager's Tips owed page: a balance row per worker and the payout history.
    public class TipsOwedResponse
    {
        public List<TipsOwedRowDto> Rows { get; set; } = new();
        // Newest first, capped (CashController.TipsOwedPayoutCap). PayoutsTruncated says the cap
        // cut the list; PaidByManagerLast90Cents is summed server-side over the whole window so the
        // headline stays exact either way.
        public List<TipPayoutDto> Payouts { get; set; } = new();
        public bool PayoutsTruncated { get; set; }
        public long PaidByManagerLast90Cents { get; set; }
    }
}
