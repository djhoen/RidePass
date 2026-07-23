namespace webapi.Controllers.API.Data.Waiver
{
    public class WaiverComplianceResponse
    {
        public List<WaiverComplianceItem> Items { get; set; } = new();
        public int TotalOnSite { get; set; }
        public int MissingCount { get; set; }
    }
}
