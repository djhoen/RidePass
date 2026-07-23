namespace Services.Repositories.Data.WaiverData
{
    /// <summary>A roster member eligible for a bulk signature request (no current waiver,
    /// no open request already outstanding).</summary>
    public class WaiverRequestCandidate
    {
        public string Email { get; set; } = string.Empty;
        public string? Name { get; set; }
    }
}
