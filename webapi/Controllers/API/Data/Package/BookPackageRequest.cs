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
    }
}
