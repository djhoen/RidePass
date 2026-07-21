namespace webapi.Seeding
{
    /// <summary>
    /// Populates a tenant with realistic demo data (STAGE + LOCAL only). Idempotent: each section
    /// skips itself when its data already exists, so re-running fills in newly-added sections
    /// without duplicating anything. Stamps tenant.seed_data_populated_at at the end.
    /// </summary>
    public interface ITenantSeeder
    {
        Task<TenantSeedSummary> PopulateAsync(Guid tenantId, CancellationToken ct = default);
    }

    /// <summary>Counts of what the seeder created, returned to the caller + audit log.</summary>
    public class TenantSeedSummary
    {
        public int Riders { get; set; }
        public int Staff { get; set; }
        public int Events { get; set; }
        public int Tickets { get; set; }
        public int WaiverSignatures { get; set; }
        public int SeasonPasses { get; set; }
        public int Memberships { get; set; }
        public int ConcessionOrders { get; set; }
        public int GiftCards { get; set; }
        public int Coupons { get; set; }
        public int Disputes { get; set; }
        public int NewsletterSubscribers { get; set; }
        public int Campaigns { get; set; }
        public int Blackouts { get; set; }
        public int ShopProducts { get; set; }
        public int ShopSales { get; set; }
        public int ShopWorkOrders { get; set; }
        public int CustomerBikes { get; set; }
        public int Inspections { get; set; }
        public int Instructors { get; set; }
    }
}
