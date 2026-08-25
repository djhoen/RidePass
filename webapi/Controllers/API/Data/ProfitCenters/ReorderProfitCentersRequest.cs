namespace webapi.Controllers.API.Data.ProfitCenters
{
    public class ReorderProfitCentersRequest
    {
        public List<ReorderProfitCenterItem> Items { get; set; } = new();
    }
}
