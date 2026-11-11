namespace Services.Repositories.Data.LessonData
{
    /// <summary>A bike rented for a lesson session, with the size it was set up for (Script0317 snapshot).</summary>
    public class LessonBikeRow
    {
        public Guid RentalId { get; set; }
        public string Status { get; set; } = "";
        public string RenterName { get; set; } = "";
        public string Items { get; set; } = "";
        public int? RiderHeightIn { get; set; }
        public int? RiderWeightLb { get; set; }
    }
}
