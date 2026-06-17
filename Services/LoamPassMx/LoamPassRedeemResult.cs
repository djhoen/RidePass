namespace Services.LoamPassMx
{
    public class LoamPassRedeemResult
    {
        public bool Redeemed { get; set; }
        /// <summary>True when the idempotency key had already been redeemed (safe replay).</summary>
        public bool AlreadyProcessed { get; set; }
        public int Remaining { get; set; }
        public string? Error { get; set; }
    }
}
