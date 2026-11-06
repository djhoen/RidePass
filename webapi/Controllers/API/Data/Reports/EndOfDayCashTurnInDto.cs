namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>
    /// A blind-count cash hand-off. Expected, manager count and variance stay null until a manager
    /// confirms receipt, so a submitted-but-unconfirmed turn-in renders as the worker's count only.
    /// </summary>
    public class EndOfDayCashTurnInDto
    {
        public Guid Id { get; set; }
        public string WorkerName { get; set; } = null!;
        public string? ManagerName { get; set; }
        public long? ExpectedCents { get; set; }
        public long WorkerCountedCents { get; set; }
        public long? ManagerCountedCents { get; set; }
        /// <summary>Manager count minus expected. Negative is short.</summary>
        public long? VarianceCents { get; set; }
        /// <summary>submitted | confirmed | disputed</summary>
        public string Status { get; set; } = null!;
        public string? Note { get; set; }
        public DateTime SubmittedAtUtc { get; set; }
        public DateTime? ConfirmedAtUtc { get; set; }
        /// <summary>Tips earned over the shift, as snapshotted at close.</summary>
        public long TipsEarnedCents { get; set; }
        /// <summary>Tips the worker took out of the drawer in cash at close.</summary>
        public long TipsFromDrawerCents { get; set; }
        /// <summary>TipsEarned minus TipsFromDrawer: what the house still owed them for this shift.</summary>
        public long HouseOwesCents { get; set; }
    }
}
