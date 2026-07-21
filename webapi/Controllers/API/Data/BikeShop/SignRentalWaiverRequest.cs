using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>Capturing the track's liability waiver at the rental counter, on the shop's
    /// device. Walk-in renters usually have no account, so this records the signature against
    /// the attendee's details rather than a user id.</summary>
    public class SignRentalWaiverRequest
    {
        [Required, MaxLength(80)] public string FirstName { get; set; } = null!;
        [Required, MaxLength(80)] public string LastName { get; set; } = null!;
        [MaxLength(200)] public string? Email { get; set; }
        public DateTime? Birthdate { get; set; }

        [Required] public string SignatureDataUrl { get; set; } = null!;

        // A minor can't sign for themselves; the counter captures the guardian instead.
        public bool SignedByParent { get; set; }
        [MaxLength(160)] public string? ParentName { get; set; }
        [MaxLength(40)] public string? ParentPhone { get; set; }
    }
}
