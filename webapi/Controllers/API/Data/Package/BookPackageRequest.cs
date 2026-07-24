namespace webapi.Controllers.API.Data.Package
{
    /// <summary>Book a package as the signed-in rider. Identity is from the token; the
    /// server re-prices from the tier and composes the ticket + rental + session.</summary>
    public class BookPackageRequest
    {
        public Guid PackageId { get; set; }
        public Guid TierId { get; set; }
        public DateTime RideDate { get; set; }
        /// <summary>Chosen coached session slot; required when the package includes coaching.</summary>
        public Guid? SlotId { get; set; }
        /// <summary>Chosen bike size: a rentable sibling variant of a bike item's product. When set,
        /// it replaces the bike item's default variant. Ignored if it isn't a valid sibling.</summary>
        public Guid? BikeVariantId { get; set; }
        /// <summary>Opt into the tenant's damage waiver: waives the refundable deposit hold.</summary>
        public bool Insurance { get; set; }
    }
}
