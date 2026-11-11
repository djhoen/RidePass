namespace Services.Repositories.Data.RiderData
{
    /// <summary>One of a guardian's dependents (rider_dependent) with their height and weight on file.</summary>
    public class DependentMeasurement
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime? Birthdate { get; set; }
        public int? HeightIn { get; set; }
        public int? WeightLb { get; set; }
        public DateTime? MeasuredAt { get; set; }
    }
}
