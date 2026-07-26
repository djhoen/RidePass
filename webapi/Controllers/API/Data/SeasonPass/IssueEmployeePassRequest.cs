using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>Approve and issue an employee pass to one staff member.</summary>
    public class IssueEmployeePassRequest
    {
        [Required]
        public Guid UserId { get; set; }

        [Required]
        public Guid ProductId { get; set; }
    }
}
