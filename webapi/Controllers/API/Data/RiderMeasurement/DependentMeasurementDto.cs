namespace webapi.Controllers.API.Data.RiderMeasurement
{
    /// <summary>One of the signed-in guardian's dependents (a minor rider) with their height and weight.</summary>
    public class DependentMeasurementDto
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int? HeightIn { get; set; }
        public int? WeightLb { get; set; }
        public DateTime? MeasuredAt { get; set; }
    }
}
