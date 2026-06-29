using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    // Deliver a receipt for a completed sale to the customer's phone (sms) or email.
    public class ConcessionReceiptRequest
    {
        [RegularExpression("^(sms|email)$")]
        public string Channel { get; set; } = "email";

        [Required, MaxLength(200)]
        public string Destination { get; set; } = null!;
    }
}
