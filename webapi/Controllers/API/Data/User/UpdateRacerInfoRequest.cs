using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.User
{
    public class UpdateRacerInfoRequest
    {
        [MaxLength(100)]
        public string? Bike { get; set; }

        [MaxLength(16)]
        public string? RaceNumber { get; set; }
    }
}
