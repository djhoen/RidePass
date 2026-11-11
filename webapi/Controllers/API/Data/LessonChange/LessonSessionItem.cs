namespace webapi.Controllers.API.Data.LessonChange
{
    /// <summary>A lesson session on the staff Lessons page.</summary>
    public class LessonSessionItem
    {
        public Guid EventId { get; set; }
        public string Title { get; set; } = "";
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public string Status { get; set; } = "";
        public DateTime? WeatherCancelledAt { get; set; }
        public int Booked { get; set; }
    }
}
