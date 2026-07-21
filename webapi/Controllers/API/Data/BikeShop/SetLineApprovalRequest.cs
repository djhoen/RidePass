namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>Customer decision on a work-order line: approved | declined | pending (clears it).</summary>
    public class SetLineApprovalRequest
    {
        public string Status { get; set; } = "pending";
    }
}
