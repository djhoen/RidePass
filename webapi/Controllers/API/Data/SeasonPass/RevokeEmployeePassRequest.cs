using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>
    /// Withdraw an issued employee pass. Distinct from the employee simply becoming inactive,
    /// which invalidates the pass automatically without withdrawing the approval.
    /// </summary>
    public class RevokeEmployeePassRequest
    {
        [Required, MaxLength(300)]
        public string Reason { get; set; } = null!;
    }
}
