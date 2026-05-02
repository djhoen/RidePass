using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Counter
{
    public class CounterSaleRequest
    {
        [Required]
        public Guid RiderId { get; set; }

        [Required, MinLength(1)]
        public List<CounterCartItem> Items { get; set; } = new();

        // Set true if the rider is signing the active waiver as part of this sale.
        public bool SignWaiver { get; set; }
    }
}
