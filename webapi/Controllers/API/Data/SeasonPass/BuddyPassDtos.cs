using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>
    /// Spend one of a pass holder's buddy admissions on a specific guest, at the counter, with
    /// the holder present. The holder's presence is established by scanning their pass to get
    /// <see cref="PassPurchaseId"/>; there is deliberately no path that lets a guest redeem alone.
    /// </summary>
    public class RedeemBuddyPassRequest
    {
        [Required]
        public Guid PassPurchaseId { get; set; }

        /// <summary>The guest's account. Buddies must have one; the counter can create it.</summary>
        [Required]
        public Guid BuddyUserId { get; set; }

        /// <summary>The ticket tier admitting the buddy. Its event's type must be in the
        /// entitlement's scope set.</summary>
        [Required]
        public Guid TierId { get; set; }
    }

    /// <summary>
    /// Hand a spent buddy credit back to the holder. Entitlement only: no money moves, and the
    /// buddy's admission is NOT cancelled (that is a separate action with its own permission).
    /// </summary>
    public class ReturnBuddyCreditRequest
    {
        [Required, MaxLength(300)]
        public string Reason { get; set; } = null!;
    }

    /// <summary>What a scanned pass's buddy entitlement looks like to the counter.</summary>
    public class BuddyEntitlementResponse
    {
        public int Total { get; set; }
        public int Used { get; set; }
        public int Remaining { get; set; }
        /// <summary>True when the perk covers admission outright.</summary>
        public bool IsFree { get; set; }
        public string DiscountKind { get; set; } = "percent";
        public int DiscountValue { get; set; }
        /// <summary>Human-readable list of what it is good for ("Lift Day", "Clinic",
        /// "Days with no event"). Empty means the perk admits nobody and is misconfigured.</summary>
        public List<string> GoodFor { get; set; } = new();
        /// <summary>Event-type ids it covers, so the counter can filter the tier picker.</summary>
        public List<Guid> EventTypeIds { get; set; } = new();
        public bool CoversWalkUpDays { get; set; }
    }

    public class BuddyRedemptionItem
    {
        public Guid Id { get; set; }
        public string? HolderName { get; set; }
        public string? BuddyName { get; set; }
        public string? BuddyEmail { get; set; }
        public string? EventTitle { get; set; }
        public DateTime RedeemedAtUtc { get; set; }
        public string? RedeemedByName { get; set; }
        public bool CreditReturned { get; set; }
        public DateTime? CreditReturnedAtUtc { get; set; }
        public string? CreditReturnedByName { get; set; }
        public string? CreditReturnReason { get; set; }
    }
}
