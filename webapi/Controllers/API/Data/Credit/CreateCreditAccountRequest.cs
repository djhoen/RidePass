using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Credit
{
    public class CreateCreditAccountRequest
    {
        [MaxLength(200)] public string? Email { get; set; }
        [MaxLength(40)] public string? Phone { get; set; }
        [MaxLength(160)] public string? DisplayName { get; set; }
        public Guid? UserId { get; set; }
    }
}
