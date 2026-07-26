using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>One cell of the admin upgrade matrix.</summary>
    public class UpsertUpgradePathRequest
    {
        [Required] public Guid FromProductId { get; set; }
        [Required] public Guid ToProductId { get; set; }

        /// <summary>Flat price to move up. Zero is allowed: a free upgrade is a goodwill gesture,
        /// and unlike a free product it is only reachable by an existing holder.</summary>
        [Range(0, 10_000_000)]
        public int PriceCents { get; set; }

        public bool IsActive { get; set; } = true;
    }

    /// <summary>Admin view: every offer, the products they connect, and who could take them.</summary>
    public class UpgradePathsResponse
    {
        public List<UpgradePathItem> Paths { get; set; } = new();
        /// <summary>Both axes of the matrix. Employee products are excluded: they are grants,
        /// not purchases, so there is nothing to upgrade from or to.</summary>
        public List<UpgradeProductOption> Products { get; set; } = new();
    }

    public class UpgradePathItem
    {
        public Guid Id { get; set; }
        public Guid FromProductId { get; set; }
        public Guid ToProductId { get; set; }
        public string? FromProductName { get; set; }
        public string? ToProductName { get; set; }
        public int PriceCents { get; set; }
        public bool IsActive { get; set; }
        /// <summary>Holders who could take this offer today. What makes the price concrete.</summary>
        public int EligibleHolders { get; set; }
    }

    public class UpgradeProductOption
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public int PriceCents { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>An upgrade the signed-in rider can take on a specific pass.</summary>
    public class UpgradeOfferItem
    {
        public Guid PathId { get; set; }
        public Guid PassPurchaseId { get; set; }
        public string FromProductName { get; set; } = string.Empty;
        public Guid ToProductId { get; set; }
        public string ToProductName { get; set; } = string.Empty;
        public string? ToProductDescription { get; set; }
        public string ToProductKind { get; set; } = string.Empty;
        public int? ToProductTotalCredits { get; set; }
        public DateTime ToValidFromDate { get; set; }
        public DateTime ToValidToDate { get; set; }
        public int PriceCents { get; set; }
    }

    /// <summary>Take an upgrade. The price is re-resolved server-side from the path, never taken
    /// from the client.</summary>
    public class BuyUpgradeRequest
    {
        [Required] public Guid PassPurchaseId { get; set; }
        [Required] public Guid PathId { get; set; }
    }
}
