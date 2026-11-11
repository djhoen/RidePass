namespace webapi.Controllers.API.Data.RiderMeasurement
{
    /// <summary>New height (whole inches, 24-96) and weight (whole pounds, 20-450).</summary>
    public class UpdateMeasurementRequest
    {
        public int? HeightIn { get; set; }
        public int? WeightLb { get; set; }
    }
}
