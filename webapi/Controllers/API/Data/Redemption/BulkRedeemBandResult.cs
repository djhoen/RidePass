namespace webapi.Controllers.API.Data.Redemption
{
    /// <summary>What became of one person's band in an "Admit and band" request (Script0313).</summary>
    public class BulkRedeemBandResult
    {
        public string? AttendeeKey { get; set; }
        public string Code { get; set; } = string.Empty;
        /// <summary>The band is on (issued, or the one they already wear) and their admitted tickets are tied to it.</summary>
        public bool Banded { get; set; }
        /// <summary>Why not; the screen keeps that person's typed code.</summary>
        public string? Error { get; set; }
    }
}
