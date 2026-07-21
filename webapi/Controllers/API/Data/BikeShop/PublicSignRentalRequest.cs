using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>Renter signing the rental agreement from the emailed link. The rental is
    /// identified by the URL token, never by anything in this body.</summary>
    public class PublicSignRentalRequest
    {
        [Required, MaxLength(160)] public string SignerName { get; set; } = null!;
        [MaxLength(200)] public string? SignerEmail { get; set; }
        [Required] public string SignatureDataUrl { get; set; } = null!;
    }

    /// <summary>Renter signing the track's liability waiver from the same link.</summary>
    public class PublicSignRentalWaiverRequest
    {
        [Required, MaxLength(80)] public string FirstName { get; set; } = null!;
        [Required, MaxLength(80)] public string LastName { get; set; } = null!;
        [MaxLength(200)] public string? Email { get; set; }
        public DateTime? Birthdate { get; set; }
        [Required] public string SignatureDataUrl { get; set; } = null!;
        public bool SignedByParent { get; set; }
        [MaxLength(160)] public string? ParentName { get; set; }
        [MaxLength(40)] public string? ParentPhone { get; set; }
    }
}
