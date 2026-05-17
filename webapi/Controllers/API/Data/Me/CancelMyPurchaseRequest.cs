using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Me
{
    public class CancelMyPurchaseRequest
    {
        [MaxLength(500)]
        public string? Reason { get; set; }
    }
}
