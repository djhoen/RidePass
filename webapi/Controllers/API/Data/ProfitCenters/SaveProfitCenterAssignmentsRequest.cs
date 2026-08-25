namespace webapi.Controllers.API.Data.ProfitCenters
{
    public class SaveProfitCenterAssignmentsRequest
    {
        public List<ProfitCenterAssignmentItem> Assignments { get; set; } = new();
    }
}
