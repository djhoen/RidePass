namespace webapi.Controllers.API.Data.Wristband
{
    /// <summary>Band (or re-band) a pass holder already admitted today (Script0312).</summary>
    public class BandPassHolderRequest
    {
        /// <summary>The scanned QR payload or typed band number.</summary>
        public string Code { get; set; } = string.Empty;
        /// <summary>Replace band: why the old one is voided (lost, broken, other). Null to band someone without one.</summary>
        public string? ReplaceReason { get; set; }
    }
}
