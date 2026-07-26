namespace Services.Repositories.Data.BikeShopData
{
    /// <summary>
    /// One tenant's login for one distributor. Per-tenant because catalog content feeds are
    /// licensed per dealer: QBP issues a Content Licensing key to a dealer account, and RidePass
    /// cannot hold one key and serve every shop from it. Every integrator in this space works the
    /// same way, asking the dealer for their own credentials.
    ///
    /// Secrets are ciphertext at rest (EncryptionHelper), the same treatment as
    /// tenant.twilio_auth_token_encrypted. Nothing here is ever projected into an API response;
    /// see DistributorConnectionStatus for what the settings screen is allowed to see.
    /// </summary>
    public class TenantDistributorCredential
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        /// <summary>Distributor slug, e.g. "qbp". Resolves to an IDistributorCatalogSource.</summary>
        public string Distributor { get; set; } = null!;

        /// <summary>The dealer's account number. Not a secret; shown so an admin can confirm which
        /// account is connected.</summary>
        public string? AccountNumber { get; set; }
        public string? Username { get; set; }

        public string? PasswordEncrypted { get; set; }
        public string? ApiKeyEncrypted { get; set; }

        public bool IsEnabled { get; set; } = true;

        public DateTime? LastSyncAt { get; set; }
        /// <summary>'ok' | 'error' | 'running'. Surfaced so a shop can see a failing sync without
        /// anyone reading logs.</summary>
        public string? LastStatus { get; set; }
        public string? LastError { get; set; }
        public int LastProductsSeen { get; set; }
        public int LastVariantsUpdated { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>What the admin UI may see about a connection. Deliberately carries no secret and no
    /// ciphertext: an admin needs to know it works, not what the key is.</summary>
    public class DistributorConnectionStatus
    {
        public string Distributor { get; set; } = null!;
        public string? AccountNumber { get; set; }
        public string? Username { get; set; }
        public bool IsEnabled { get; set; }
        public bool HasApiKey { get; set; }
        public bool HasPassword { get; set; }
        public DateTime? LastSyncAt { get; set; }
        public string? LastStatus { get; set; }
        public string? LastError { get; set; }
        public int LastProductsSeen { get; set; }
        public int LastVariantsUpdated { get; set; }
    }
}
