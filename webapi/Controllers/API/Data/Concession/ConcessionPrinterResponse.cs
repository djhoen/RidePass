namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionPrinterResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string Url { get; set; } = null!;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }

        // Empty = this printer prints the whole order.
        public List<Guid> StationIds { get; set; } = new();
    }
}
