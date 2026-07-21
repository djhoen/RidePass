using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>Capturing a customer's signature on the shop's device. Exactly one of
    /// WorkOrderId / RentalId identifies what is being signed for.</summary>
    public class SignShopAgreementRequest
    {
        public Guid? WorkOrderId { get; set; }
        public Guid? RentalId { get; set; }

        [Required, MaxLength(160)] public string SignerName { get; set; } = null!;
        [MaxLength(200)] public string? SignerEmail { get; set; }

        /// <summary>data: URL from the signature pad.</summary>
        [Required] public string SignatureDataUrl { get; set; } = null!;
    }
}
