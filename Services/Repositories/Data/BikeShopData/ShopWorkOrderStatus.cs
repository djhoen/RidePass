namespace Services.Repositories.Data.BikeShopData
{
    /// <summary>
    /// A tenant's work-order status definition. The <see cref="Code"/> is what lands in
    /// shop_work_order.status; <see cref="Behavior"/> maps it to the fixed system meaning that drives
    /// inventory and notifications. Built-ins (the seven canonical codes) can be renamed, recoloured,
    /// reordered and flagged for notification, but their code and behaviour are fixed. Tenants may add
    /// their own statuses, always with 'open' behaviour (an extra working stage).
    /// </summary>
    public class ShopWorkOrderStatus
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Color { get; set; } = "grey";
        /// <summary>estimate | open | ready | done | cancelled.</summary>
        public string Behavior { get; set; } = "open";
        /// <summary>Notify the customer when a work order enters this status.</summary>
        public bool NotifyCustomer { get; set; }
        public int SortOrder { get; set; } = 100;
        /// <summary>One of the seven seeded statuses: code and behaviour are locked.</summary>
        public bool IsBuiltin { get; set; }
        public bool IsActive { get; set; } = true;
        /// <summary>The status a new work order starts in. Exactly one per tenant.</summary>
        public bool IsDefault { get; set; }
    }
}
