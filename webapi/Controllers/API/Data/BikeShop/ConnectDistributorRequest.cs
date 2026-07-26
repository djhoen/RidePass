using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>
    /// Connect (or re-connect) a distributor account so its catalog syncs automatically.
    ///
    /// The credentials are the shop's OWN, obtained from the distributor: content feeds are
    /// licensed per dealer, so RidePass cannot hold one key covering every shop. For QBP that means
    /// their qbp.com login, the EFTP password QBP issued, and their Content Licensing (CLS) API key.
    /// </summary>
    public class ConnectDistributorRequest
    {
        [Required, MaxLength(40), RegularExpression("^[a-z0-9_-]+$")]
        public string Distributor { get; set; } = null!;

        [MaxLength(60)] public string? AccountNumber { get; set; }
        [MaxLength(200)] public string? Username { get; set; }

        /// <summary>
        /// Leave null to KEEP the stored secret. The UI never shows an existing password, so
        /// requiring it on every edit would mean re-keying a credential just to fix a typo in the
        /// account number. Disconnecting is how a secret is actually cleared.
        /// </summary>
        [MaxLength(400)] public string? Password { get; set; }

        /// <summary>Same "null keeps the stored value" rule as Password.</summary>
        [MaxLength(400)] public string? ApiKey { get; set; }

        public bool IsEnabled { get; set; } = true;
    }
}
