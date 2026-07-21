namespace Services.Repositories.Data.RewardData
{
    public class RewardProgram
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string EnrollmentMode { get; set; } = "auto";       // auto | opt_in
        public string RequirementKind { get; set; } = "any";       // pass | event_ticket | any
        public int RequirementCount { get; set; }
        public int RewardPercentOff { get; set; }
        // percent_off = the classic voucher after N purchases; credit_rate = store credit back
        // as a rate on every qualifying spend (Script0196).
        public string RewardKind { get; set; } = "percent_off";    // percent_off | credit_rate
        public int? CreditRateBps { get; set; }                    // 500 = 5% back
        public string CreditQualifyingKind { get; set; } = "any";  // any | event_ticket | concession | shop_sale
        public int? ProximityEmailThreshold { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class RewardEnrollment
    {
        public Guid Id { get; set; }
        public Guid ProgramId { get; set; }
        public Guid UserId { get; set; }
        public DateTime EnrolledAt { get; set; }
        public int? LastProximityEmailedAtCount { get; set; }
    }

    public class RewardRedemption
    {
        public Guid Id { get; set; }
        public Guid ProgramId { get; set; }
        public Guid UserId { get; set; }
        public DateTime EarnedAt { get; set; }
        public DateTime? RedeemedAt { get; set; }
        public string? RedeemedOnKind { get; set; }
        public Guid? RedeemedOnId { get; set; }
    }
}
