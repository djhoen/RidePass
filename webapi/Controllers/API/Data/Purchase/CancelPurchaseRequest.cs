using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Purchase
{
    public class CancelPurchaseRequest
    {
        [MaxLength(500)]
        public string? Reason { get; set; }
    }
}
