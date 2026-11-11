namespace Services.Repositories.Data.LessonData
{
    /// <summary>A group in another session a booking could move to.</summary>
    public class LessonRebookOption
    {
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = "";
        public Guid TierId { get; set; }
        public string TierName { get; set; } = "";
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        /// <summary>Spots left in the group (and the event), or null when unlimited.</summary>
        public int? SpotsLeft { get; set; }
    }
}
