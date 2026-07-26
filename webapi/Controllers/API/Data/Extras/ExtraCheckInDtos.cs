namespace webapi.Controllers.API.Data.Extras
{
    /// <summary>One add-on on the check-in list.</summary>
    public class ExtraCheckInItem
    {
        public Guid PurchaseId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ProductKind { get; set; } = string.Empty;
        public string PurchaserName { get; set; } = string.Empty;
        public string PurchaserEmail { get; set; } = string.Empty;
        public int Quantity { get; set; }
        /// <summary>Size/colour/gender where the add-on has variants, e.g. a race t-shirt.</summary>
        public string? VariantLabel { get; set; }
        public int AmountCents { get; set; }
        /// <summary>'paid' (not arrived) or 'redeemed' (arrived).</summary>
        public string Status { get; set; } = string.Empty;
        public bool Arrived { get; set; }
        public DateTime? ArrivedAtUtc { get; set; }
        public string? ArrivedByName { get; set; }
        /// <summary>Null for an add-on bought at the counter with no event attached.</summary>
        public Guid? EventId { get; set; }
        public string? EventTitle { get; set; }
        public DateTime? EventStartsAtUtc { get; set; }
        public DateTime PurchasedAtUtc { get; set; }
    }

    /// <summary>The list plus the counts the header needs, so the page doesn't total client-side
    /// off a truncated page and report a number that isn't the truth.</summary>
    public class ExtraCheckInResponse
    {
        public List<ExtraCheckInItem> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int ArrivedCount { get; set; }
        /// <summary>True when the result hit the row cap, so the page can say so rather than
        /// implying the list is everyone.</summary>
        public bool Truncated { get; set; }
    }

    /// <summary>Add-on kinds this tenant actually sells, for the filter.</summary>
    public class ExtraCheckInFilters
    {
        public List<ExtraCheckInProductOption> Products { get; set; } = new();
    }

    public class ExtraCheckInProductOption
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public class SetExtraCheckInRequest
    {
        public bool CheckedIn { get; set; }
    }
}
