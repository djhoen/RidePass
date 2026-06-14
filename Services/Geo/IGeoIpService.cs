namespace Services.Geo
{
    // Resolved approximate location for a request IP. CountryCode is ISO 3166-1
    // alpha-2 ("US"); null when the country could not be resolved (private/loopback
    // IP, provider error, etc.). Latitude/Longitude let the client seed the radius
    // filter without prompting for the browser geolocation permission.
    public class GeoLocation
    {
        public string? CountryCode { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    public interface IGeoIpService
    {
        Task<GeoLocation?> Locate(string? ip, CancellationToken ct = default);
    }
}
