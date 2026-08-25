namespace webapi.Controllers.API.Data.ProfitCenters
{
    public class ProfitCenterAssignmentItem
    {
        public string RevenueKey { get; set; } = null!;
        /// <summary>Null clears the assignment (the stream falls back to its built-in department).</summary>
        public Guid? ProfitCenterId { get; set; }
    }
}
