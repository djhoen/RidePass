using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Counter
{
    /// <summary>What the cashier typed: an email, a name, or a phone number.</summary>
    public class RiderSearchRequest
    {
        [Required, MinLength(2)]
        public string Query { get; set; } = null!;
    }
}
