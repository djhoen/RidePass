using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>The holder details for one pass on the order.</summary>
    public class SeasonPassRegistrationItem
    {
        [Required] public Guid PurchaseId { get; set; }

        [Required, MaxLength(120)] public string FirstName { get; set; } = null!;
        [Required, MaxLength(120)] public string LastName { get; set; } = null!;

        // Required whenever the pass needs a waiver: it decides whether a parent/guardian must
        // sign, so letting it through blank would sign a minor in as an adult.
        public DateTime? Birthdate { get; set; }

        /// <summary>Data-URL of the gate photo. Required — the gate matches the face against it.</summary>
        [Required] public string PhotoDataUrl { get; set; } = null!;

        /// <summary>Data-URL of the drawn signature; required when the product requires a waiver
        /// and the holder has no current signature already on file.</summary>
        public string? WaiverSignatureDataUrl { get; set; }

        [MaxLength(120)] public string? ParentGuardianName { get; set; }
        [MaxLength(40)] public string? ParentGuardianPhone { get; set; }
    }
}
