namespace webapi.Controllers.API.Data.LessonChange
{
    /// <summary>A bike rented for a lesson session and the rider size it was set up for.</summary>
    public class LessonBikeItem
    {
        public Guid RentalId { get; set; }
        public string Status { get; set; } = "";
        public string RenterName { get; set; } = "";
        public string Items { get; set; } = "";
        public int? RiderHeightIn { get; set; }
        public int? RiderWeightLb { get; set; }
    }
}
