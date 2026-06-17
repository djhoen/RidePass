namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class TenantListItem
    {
        public Guid Id { get; set; }
        public string Subdomain { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string Timezone { get; set; } = null!;
        public int ServiceChargeBps { get; set; }
        public int? MonthlyServiceChargeCapCents { get; set; }
        public bool IsPublished { get; set; }
        public bool ConcessionsEnabled { get; set; }
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? ContactEmail { get; set; }
        public string? Phone { get; set; }
        public string? LoampassMxDestinationId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
