namespace Services.Repositories.Data.LessonData
{
    /// <summary>An upcoming lesson session for the staff Lessons page.</summary>
    public class LessonSessionRow
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
