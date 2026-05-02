namespace Services.Repositories.Data.PaymentData
{
    public class TenantFeeSchedule
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public DateTime EffectiveFromUtc { get; set; }
        public DateTime? EffectiveToUtc { get; set; }
        public int? MonthlyCapCents { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TenantFeeTier
    {
        public Guid Id { get; set; }
        public Guid ScheduleId { get; set; }
        public long MinVolumeCents { get; set; }
        public long? MaxVolumeCents { get; set; }
        public int RateBps { get; set; }
        public int SortOrder { get; set; }
    }

    public class TenantFeeScheduleWithTiers
    {
        public TenantFeeSchedule Schedule { get; set; } = null!;
        public List<TenantFeeTier> Tiers { get; set; } = new();
    }
}
