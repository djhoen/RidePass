using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Reward
{
    public class UpsertRewardProgramRequest
    {
        [Required, MaxLength(120)]
        public string Name { get; set; } = null!;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [Required, RegularExpression("^(auto|opt_in)$")]
        public string EnrollmentMode { get; set; } = "auto";

        [Required, RegularExpression("^(pass|event_ticket|any)$")]
        public string RequirementKind { get; set; } = "any";

        [Range(1, 1000)]
        public int RequirementCount { get; set; }

        [Range(1, 100)]
        public int RewardPercentOff { get; set; }

        // percent_off = voucher after N purchases (the fields above); credit_rate = store
        // credit back on every qualifying spend (the two fields below).
        [Required, RegularExpression("^(percent_off|credit_rate)$")]
        public string RewardKind { get; set; } = "percent_off";

        [Range(1, 10000)]
        public int? CreditRateBps { get; set; }

        [RegularExpression("^(any|event_ticket|concession|shop_sale)$")]
        public string CreditQualifyingKind { get; set; } = "any";

        [Range(1, 1000)]
        public int? ProximityEmailThreshold { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class RewardProgramResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string EnrollmentMode { get; set; } = null!;
        public string RequirementKind { get; set; } = null!;
        public int RequirementCount { get; set; }
        public int RewardPercentOff { get; set; }
        public string RewardKind { get; set; } = "percent_off";
        public int? CreditRateBps { get; set; }
        public string CreditQualifyingKind { get; set; } = "any";
        public int? ProximityEmailThreshold { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class RiderRewardProgramResponse
    {
        public Guid ProgramId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string EnrollmentMode { get; set; } = null!;
        public string RequirementKind { get; set; } = null!;
        public int RequirementCount { get; set; }
        public int RewardPercentOff { get; set; }
        public string RewardKind { get; set; } = "percent_off";
        public int? CreditRateBps { get; set; }
        public string CreditQualifyingKind { get; set; } = "any";
        public bool IsEnrolled { get; set; }
        public int Progress { get; set; }
        public int RemainingForReward { get; set; }
        public DateTime? EnrolledAtUtc { get; set; }
    }

    public class RiderRewardRedemption
    {
        public Guid Id { get; set; }
        public Guid ProgramId { get; set; }
        public string ProgramName { get; set; } = null!;
        public int RewardPercentOff { get; set; }
        public DateTime EarnedAtUtc { get; set; }
        public DateTime? RedeemedAtUtc { get; set; }
    }
}
