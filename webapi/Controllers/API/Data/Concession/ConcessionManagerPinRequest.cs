using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    // Set (or clear) the calling staff member's own POS manager PIN. Empty/blank clears it. Only a user
    // who holds a manager/admin role can have an authorizing PIN; the controller enforces that.
    public class ConcessionManagerPinRequest
    {
        // 4-8 digits, or blank to clear. Stored as a salted hash, never the raw value.
        [MaxLength(8)] public string? Pin { get; set; }
    }
}
