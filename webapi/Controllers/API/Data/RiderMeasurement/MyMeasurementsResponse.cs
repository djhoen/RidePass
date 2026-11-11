namespace webapi.Controllers.API.Data.RiderMeasurement
{
    /// <summary>The signed-in rider's own height and weight, and their dependents' at this venue.</summary>
    public class MyMeasurementsResponse
    {
        public RiderMeasurementDto Self { get; set; } = new();
        public List<DependentMeasurementDto> Dependents { get; set; } = new();
    }
}
