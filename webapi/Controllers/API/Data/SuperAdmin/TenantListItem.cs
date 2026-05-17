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
        public DateTime CreatedAtUtc { get; set; }
    }
}
