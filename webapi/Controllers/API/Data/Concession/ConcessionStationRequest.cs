using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    // Create/update a kitchen station (e.g. Fryer, Grill, Drinks) the cook screen can filter by.
    public class ConcessionStationRequest
    {
        [Required, MaxLength(80)]
        public string Name { get; set; } = null!;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
