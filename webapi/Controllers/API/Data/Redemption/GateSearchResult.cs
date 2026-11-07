namespace webapi.Controllers.API.Data.Redemption
{
    /// <summary>
    /// A match on the gate's name/email lookup. Two kinds share the list:
    /// - "ticket": one purchaser's order for one of today's events. AnchorToken is fed straight back
    ///   into Order/{token}, so selecting it lands on exactly the check-in screen a QR scan would.
    /// - "season_pass": a pass valid today. AnchorToken is the pass's redemption token, which the
    ///   gate opens through SeasonPass/Pass/{token}, the same as a scanned pass QR.
    /// </summary>
    public class GateSearchResult
    {
        public const string KindTicket = "ticket";
        public const string KindSeasonPass = "season_pass";

        /// <summary>"ticket" or "season_pass".</summary>
        public string Kind { get; set; } = KindTicket;
        /// <summary>Who is standing at the gate: the rider(s) on a ticket order, else the buyer; the
        /// named holder on a pass, else the buyer.</summary>
        public string DisplayName { get; set; } = null!;
        /// <summary>What they hold: the event title for a ticket, the pass product name for a pass.</summary>
        public string Holds { get; set; } = null!;
        /// <summary>Ticket rows only.</summary>
        public Guid? EventId { get; set; }
        /// <summary>Ticket rows only.</summary>
        public string? EventTitle { get; set; }
        /// <summary>Ticket rows only.</summary>
        public DateTime? EventStartsAtUtc { get; set; }
        public string PurchaserName { get; set; } = null!;
        public string PurchaserEmail { get; set; } = null!;
        /// <summary>Season pass rows: the linked account's email when it differs from the checkout email.</summary>
        public string? AccountEmail { get; set; }
        /// <summary>The redemption token to open: any ticket of the order, or the pass's own token.</summary>
        public Guid AnchorToken { get; set; }
        public int ItemCount { get; set; }
        /// <summary>Tickets: how many are checked in. Passes: 1 when already admitted today.</summary>
        public int RedeemedCount { get; set; }
        /// <summary>Riders named on the order, when registration captured them.</summary>
        public string? RiderNames { get; set; }
        /// <summary>Season pass rows: credits / days_of_week / unlimited.</summary>
        public string? ProductKind { get; set; }
        /// <summary>Season pass rows: rides left on a credits pass.</summary>
        public int? CreditsRemaining { get; set; }
        /// <summary>Season pass rows: last valid day.</summary>
        public DateOnly? ValidToDate { get; set; }
    }
}
