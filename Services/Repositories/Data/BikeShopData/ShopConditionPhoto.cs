namespace Services.Repositories.Data.BikeShopData
{
    /// <summary>
    /// A condition photo attached to a work order or a rental. Exactly one owner is set.
    /// Rentals photograph both ends ('intake' when gear goes out, 'return' when it comes back)
    /// so a damage capture against the security deposit has evidence behind it; work orders
    /// photograph what arrived, and optionally what a tech found mid-repair ('progress').
    /// </summary>
    public class ShopConditionPhoto
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? WorkOrderId { get; set; }
        public Guid? RentalId { get; set; }
        public string Stage { get; set; } = "intake";   // intake | return | progress
        public string ImageUrl { get; set; } = null!;
        public string? Caption { get; set; }
        public Guid? UploadedByUserId { get; set; }
        public int SortOrder { get; set; } = 100;
        public DateTime CreatedAt { get; set; }
    }
}
