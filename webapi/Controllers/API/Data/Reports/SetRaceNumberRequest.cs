using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Reports
{
    public class SetRaceNumberRequest
    {
        // Null/empty clears the race number on this purchase.
        [MaxLength(16)]
        public string? RaceNumber { get; set; }
    }
}
