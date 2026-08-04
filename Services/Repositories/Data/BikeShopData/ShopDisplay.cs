namespace Services.Repositories.Data.BikeShopData
{
    /// <summary>A paired customer-facing display tablet for the bike shop counter. The staff
    /// device pushes the current view as an opaque JSON snapshot (StateJson: charges being rung,
    /// or a document to read and sign); the display polls it and writes back ResponseJson (the
    /// captured signature + signer details). Every state push clears ResponseJson so a stale
    /// signature can never attach to a newer request.</summary>
    public class ShopDisplay
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string PairCode { get; set; } = null!;
        public string? StateJson { get; set; }
        public string? ResponseJson { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
