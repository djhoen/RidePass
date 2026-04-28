using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Blackout
{
    public class UpsertBlackoutRequest
    {
        [Required]
        public DateTime StartsAtUtc { get; set; }

        [Required]
        public DateTime EndsAtUtc { get; set; }

        public bool AllDay { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }
    }
}
