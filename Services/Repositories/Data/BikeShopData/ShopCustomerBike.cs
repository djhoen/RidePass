namespace Services.Repositories.Data.BikeShopData
{
    /// <summary>
    /// A customer's own bike, as a record rather than a sentence. Keyed by serial where one exists,
    /// so a returning bike resolves to the same row and carries its repair history with it.
    /// Ownership is loose on purpose: an account when we have one, otherwise the walk-in name and
    /// phone off the ticket.
    /// </summary>
    public class ShopCustomerBike
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? CustomerUserId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerPhone { get; set; }
        public string? Serial { get; set; }
        public string? Brand { get; set; }
        public string? Model { get; set; }
        public int? ModelYear { get; set; }
        public string? Color { get; set; }
        public string? Size { get; set; }
        public string? Notes { get; set; }
        /// <summary>The serialized unit this bike left the shop as, when we sold it.</summary>
        public Guid? SoldItemId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>"2022 Trek Fuel EX 8, Black" from whatever parts are filled in.</summary>
        public string DisplayName =>
            string.Join(" ", new[]
            {
                ModelYear?.ToString(),
                Brand,
                Model,
                string.IsNullOrWhiteSpace(Color) ? null : $"({Color})",
            }.Where(s => !string.IsNullOrWhiteSpace(s)))
            is { Length: > 0 } s ? s : (Serial ?? "Bike");
    }

    /// <summary>One prior job on a bike, for the service-history panel.</summary>
    public class ShopBikeHistoryRow
    {
        public Guid WorkOrderId { get; set; }
        public string Status { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public DateTime? PromisedAt { get; set; }
        public string? IntakeNotes { get; set; }
        public int TotalCents { get; set; }
    }

    /// <summary>
    /// What we know about a serial from our OWN sales, used to prefill intake so staff don't retype
    /// a bike we sold them.
    /// </summary>
    public class ShopSoldUnitMatch
    {
        public Guid ItemId { get; set; }
        public string? Serial { get; set; }
        public string? Brand { get; set; }
        public string? Model { get; set; }
        public Guid? BuyerUserId { get; set; }
        public string? BuyerName { get; set; }
        public DateTime? SoldAt { get; set; }
    }
}
