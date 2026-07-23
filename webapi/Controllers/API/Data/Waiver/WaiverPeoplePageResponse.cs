namespace webapi.Controllers.API.Data.Waiver
{
    public class WaiverPeoplePageResponse
    {
        public List<WaiverPersonItem> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
