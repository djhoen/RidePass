namespace webapi.Controllers.API.Data.LessonChange
{
    /// <summary>A session and group a lesson booking can move to.</summary>
    public class LessonRebookOptionItem
    {
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = "";
        public Guid TierId { get; set; }
        public string TierName { get; set; } = "";
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public int? SpotsLeft { get; set; }
    }
}
