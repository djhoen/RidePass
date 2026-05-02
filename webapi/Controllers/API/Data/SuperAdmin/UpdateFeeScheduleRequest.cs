using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class UpdateFeeScheduleRequest
    {
        /// <summary>
        /// Optional cap on RidePass take per UTC calendar month. Null = no cap.
        /// </summary>
        public int? MonthlyCapCents { get; set; }

        [Required, MinLength(1)]
        public List<UpdateFeeScheduleTier> Tiers { get; set; } = new();
    }

    public class UpdateFeeScheduleTier
    {
        [Range(0, long.MaxValue)]
        public long MinVolumeCents { get; set; }

        /// <summary>Null = open-ended top tier.</summary>
        public long? MaxVolumeCents { get; set; }

        [Range(0, 10000)]
        public int RateBps { get; set; }
    }
}
