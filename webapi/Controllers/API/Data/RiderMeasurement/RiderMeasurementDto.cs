namespace webapi.Controllers.API.Data.RiderMeasurement
{
    /// <summary>A rider's height (whole inches) and weight (whole pounds); null when not on file.</summary>
    public class RiderMeasurementDto
    {
        public int? HeightIn { get; set; }
        public int? WeightLb { get; set; }
        public DateTime? MeasuredAt { get; set; }
    }
}
