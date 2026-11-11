namespace Services.Repositories.Data.RiderData
{
    /// <summary>A rider's height (whole inches) and weight (whole pounds), Script0317. Null = not on file.</summary>
    public class RiderMeasurement
    {
        public int? HeightIn { get; set; }
        public int? WeightLb { get; set; }
        public DateTime? MeasuredAt { get; set; }
    }
}
