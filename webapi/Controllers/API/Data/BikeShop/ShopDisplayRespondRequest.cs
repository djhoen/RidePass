using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class ShopDisplayRespondRequest
    {
        // The customer's answer to the outstanding request (signature data URL + signer details,
        // tagged with the requestId the staff device generated). The staff device validates the
        // requestId and submits the actual signature through the existing gated endpoints; this
        // payload is only a relay. Sized for a signature PNG data URL.
        [Required, MaxLength(262144)]
        public string ResponseJson { get; set; } = null!;
    }
}
