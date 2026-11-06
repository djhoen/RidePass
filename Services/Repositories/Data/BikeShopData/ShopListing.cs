namespace Services.Repositories.Data.BikeShopData
{
    // Used & ex-rental listings (Script0302): one specific serialized unit on the sales floor at
    // its own asking price. A listing has no status column of its own; ACTIVE / RESERVED / SOLD /
    // UNLISTED are derived from unlisted_at, the unit's status, the reserving sale and the sale
    // line that sold the unit. Services.BikeShop.ListingRules is the one place that derives them.

    /// <summary>A shop_item_listing row as written.</summary>
    public class ShopItemListing
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid ItemId { get; set; }
        public string Title { get; set; } = null!;
        public int AskingPriceCents { get; set; }
        public string ConditionGrade { get; set; } = "good";   // excellent | good | fair
        public string? ConditionNotes { get; set; }
        /// <summary>false = counter only: rings at the register, never shown in the public store.</summary>
        public bool Published { get; set; } = true;
        public DateTime ListedAt { get; set; }
        public Guid? ListedByUserId { get; set; }
        /// <summary>The online checkout holding this unit, if any. Live-ness is a rule, not a column.</summary>
        public Guid? ReservedSaleId { get; set; }
        public DateTime? ReservedAt { get; set; }
        public DateTime? UnlistedAt { get; set; }
        /// <summary>The unit's status when it was listed (available | maintenance | retired); unlisting
        /// restores it. Null on rows written before the column existed, which reads as available.</summary>
        public string? PriorStatus { get; set; }
    }

    /// <summary>One of a listing's own photos (shop_listing_photo).</summary>
    public class ShopListingPhoto
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid ListingId { get; set; }
        public string ImageUrl { get; set; } = null!;
        public string? Caption { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// A listing joined to everything a screen needs to describe it: the unit, its variant and
    /// product, the category, who listed it, the reserving sale's status, and the sale that sold
    /// the unit (only looked up once the unit reads 'sold'). Photos are attached by the repository.
    /// </summary>
    public class ShopListingRow : ShopItemListing
    {
        public string UnitLabel { get; set; } = null!;
        public string? Serial { get; set; }
        /// <summary>The unit's current shop_item status (for_sale while the listing is on the floor).</summary>
        public string ItemStatus { get; set; } = null!;
        public int? AcquiredCostCents { get; set; }
        public Guid VariantId { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        public string? Gender { get; set; }
        public bool VariantIsActive { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = null!;
        public string? ProductImageUrl { get; set; }
        public string ProductCondition { get; set; } = "new";
        public bool ProductIsActive { get; set; }
        public string? CategoryName { get; set; }
        public string? ListedByName { get; set; }
        /// <summary>Status of the sale in ReservedSaleId; null when there is none (or it was deleted).</summary>
        public string? ReservedSaleStatus { get; set; }
        public Guid? ReservedBuyerUserId { get; set; }
        public Guid? SoldSaleId { get; set; }
        public int? SoldOrderNumber { get; set; }
        public DateTime? SoldAt { get; set; }
        public List<ShopListingPhoto> Photos { get; set; } = new();
    }

    /// <summary>What a unit is, for defaulting a new listing's title and checking it may be listed.</summary>
    public class ShopListingItemContext
    {
        public Guid ItemId { get; set; }
        public string ItemStatus { get; set; } = null!;
        public string UnitLabel { get; set; } = null!;
        public int? AcquiredCostCents { get; set; }
        public Guid VariantId { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        public string? Gender { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = null!;
    }

    public enum ListingWriteOutcome
    {
        Ok,
        /// <summary>No such listing / unit at this tenant.</summary>
        NotFound,
        /// <summary>The row exists but its state refuses the change (409): sold, rented out, reserved, already listed.</summary>
        Conflict,
    }

    /// <summary>Result of a listing write that can be refused by state rather than by validation.</summary>
    public class ListingWriteResult
    {
        public ListingWriteOutcome Outcome { get; set; }
        public Guid? Id { get; set; }
        public string? Error { get; set; }
        /// <summary>Set when a Create was refused because a rental is booked on the unit: the earliest
        /// such booking's start, so the caller can name the date in the tenant's own zone.</summary>
        public DateTime? BlockingRentalStartsAt { get; set; }

        public static ListingWriteResult Ok(Guid id) => new() { Outcome = ListingWriteOutcome.Ok, Id = id };
        public static ListingWriteResult NotFound(string error) => new() { Outcome = ListingWriteOutcome.NotFound, Error = error };
        public static ListingWriteResult Conflict(string error) => new() { Outcome = ListingWriteOutcome.Conflict, Error = error };
        public static ListingWriteResult RentalBooked(DateTime startsAt) =>
            new() { Outcome = ListingWriteOutcome.Conflict, BlockingRentalStartsAt = startsAt };
    }
}
