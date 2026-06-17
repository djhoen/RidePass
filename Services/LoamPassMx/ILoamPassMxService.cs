namespace Services.LoamPassMx
{
    /// <summary>
    /// Client for the LoamMx (LoamPassMx) partner API. Lets RidePass link a rider's LoamMx
    /// account (email + 6-digit code), read their redeemable credit balance at a destination,
    /// and redeem one credit when they pay for entry with their Loam Pass.
    /// </summary>
    public interface ILoamPassMxService
    {
        /// <summary>True when a base URL + API key are configured.</summary>
        bool IsConfigured { get; }

        /// <summary>Ask LoamMx to email the rider a verification code. True if the request was accepted.</summary>
        Task<bool> VerifyStartAsync(string email, CancellationToken ct = default);

        /// <summary>Confirm the code; returns the LoamMx account on success, null if invalid/expired.</summary>
        Task<LoamPassAccount?> VerifyConfirmAsync(string email, string code, CancellationToken ct = default);

        /// <summary>Redeemable credit count for the account at the destination (0 on any failure).</summary>
        Task<int> GetCreditsAsync(string accountId, string destinationId, CancellationToken ct = default);

        /// <summary>Redeem one credit. Idempotent on idempotencyKey.</summary>
        Task<LoamPassRedeemResult> RedeemAsync(string accountId, string destinationId, string idempotencyKey, CancellationToken ct = default);

        /// <summary>Reverse a redemption (give the credit back) by its idempotency key. Idempotent. True on success.</summary>
        Task<bool> RefundAsync(string idempotencyKey, CancellationToken ct = default);

        /// <summary>Resolve a scanned Loam Pass (userPass id) to its owning LoamMx account. Null if unknown.</summary>
        Task<LoamPassAccount?> GetPassOwnerAsync(string passId, CancellationToken ct = default);
    }
}
