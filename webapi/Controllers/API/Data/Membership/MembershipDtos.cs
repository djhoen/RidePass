using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Membership
{
    // Combined "what does this tenant offer + what's my status" payload — used
    // by the rider /Membership page in one call.
    public class MembershipStatusResponse
    {
        // Tenant config snapshot (mirror of the relevant tenant.* columns).
        public bool Enabled { get; set; }
        public string Name { get; set; } = null!;
        public int PriceCents { get; set; }
        public string DurationKind { get; set; } = null!;       // 'one_time' | 'yearly'
        public bool RequiredForRiders { get; set; } = true;
        public bool RequiredForSpectators { get; set; }

        // Rider-specific. Null when not signed in or no purchases yet.
        public ActiveMembership? Active { get; set; }
        public List<MembershipHistoryItem> History { get; set; } = new();
    }

    public class ActiveMembership
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string DurationKind { get; set; } = null!;
        public DateTime ValidFromUtc { get; set; }
        public DateTime? ValidToUtc { get; set; }               // null = lifetime
        public int AmountCents { get; set; }
    }

    public class MembershipHistoryItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string DurationKind { get; set; } = null!;
        public DateTime ValidFromUtc { get; set; }
        public DateTime? ValidToUtc { get; set; }
        public int AmountCents { get; set; }
        public string Status { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; }
    }

    public class BuyMembershipResponse
    {
        public Guid PurchaseId { get; set; }
        public string ClientSecret { get; set; } = null!;
        public int AmountCents { get; set; }
        public int RiderServiceChargeCents { get; set; }
    }

    public class UpdateMembershipSettingsRequest
    {
        public bool Enabled { get; set; }
        [Required, MaxLength(120)]
        public string Name { get; set; } = "Track Membership";
        [Range(0, 10_000_000)]
        public int PriceCents { get; set; }
        [Required, RegularExpression("^(one_time|yearly)$")]
        public string DurationKind { get; set; } = "yearly";
        public bool RequiredForRiders { get; set; } = true;
        public bool RequiredForSpectators { get; set; }
    }
}
