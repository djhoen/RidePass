namespace Services.Repositories.Data.BikeShopData
{
    // Repair work orders: labor + parts accrued on the bench, billed out through a shop_sale at
    // pickup. Parts consume stock when added to a committed (non-estimate) job so on-hand reflects
    // the bench; the bill-out sale carries work_order_id so DepleteForSale skips it.

    public class ShopWorkOrder
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? CustomerUserId { get; set; }
        public string CustomerName { get; set; } = null!;
        public string? CustomerPhone { get; set; }
        public string? CustomerEmail { get; set; }
        public Guid? SubjectItemId { get; set; }        // shop's own serialized unit (fleet service)
        public string? CustomerBikeDesc { get; set; }   // legacy/fallback free text
        /// <summary>The customer's bike as a record. Preferred over CustomerBikeDesc; carries history.</summary>
        public Guid? CustomerBikeId { get; set; }
        public string Status { get; set; } = "intake";  // estimate|intake|awaiting_parts|in_progress|ready|picked_up|cancelled
        public Guid? AssignedTechUserId { get; set; }
        /// <summary>Ties work orders dropped off together (one per bike) into one customer visit.
        /// Null = a solo ticket. A shared key, not a foreign key to a group table.</summary>
        public Guid? GroupId { get; set; }
        /// <summary>What the customer reported at drop-off. Internal.</summary>
        public string? IntakeNotes { get; set; }
        /// <summary>The one note shown to the customer: printed on the claim tag and the bill.
        /// Distinct from intake symptoms and the internal thread, which never leave the shop.</summary>
        public string? CustomerNotes { get; set; }
        public DateTime? PromisedAt { get; set; }
        /// <summary>QC sign-off: the second reviewer who checked the finished job, and when. Both
        /// null until it is checked. Ideally not the assigning tech, but not enforced.</summary>
        public Guid? CheckedByUserId { get; set; }
        public DateTime? CheckedAt { get; set; }
        /// <summary>Accumulated worked minutes (from the timer or a manual set).</summary>
        public int ActualMinutes { get; set; }
        /// <summary>When the running timer segment started; null = the timer is stopped.</summary>
        public DateTime? TimerStartedAt { get; set; }
        public Guid? SaleId { get; set; }

        // Repair deposit: staff set an amount, then either record cash at the counter or email
        // the customer a payment link (DepositRequestToken resolves the order publicly). A paid
        // deposit is credited against the bill-out sale (shop_sale.deposit_applied_cents).
        public int DepositCents { get; set; }
        public string? DepositPiId { get; set; }
        public DateTime? DepositPaidAt { get; set; }
        public string? DepositPaymentMethod { get; set; }      // stripe | stripe_direct | cash
        public string? DepositStripeAccountId { get; set; }    // direct-charge tenants: refunds go back through this account
        public Guid DepositRequestToken { get; set; }
        public DateTime? DepositRequestSentAt { get; set; }
        // Deposit value returned to the customer so far, by partial refund OR by conversion to
        // store credit (both consume the deposit). DepositRefundedAt stamps only when the whole
        // deposit has been returned.
        public int DepositRefundedCents { get; set; }
        public DateTime? DepositRefundedAt { get; set; }

        /// <summary>Stamped the first time the order reaches 'ready', so the customer is told
        /// once even if staff bounce the status around.</summary>
        public DateTime? ReadyNotifiedAt { get; set; }
        /// <summary>When a follow-up service reminder is due, set at pickup from the tenant's
        /// interval. Null means never remind.</summary>
        public DateTime? ServiceReminderAt { get; set; }
        public DateTime? ReminderSentAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ShopWorkOrderLine
    {
        public Guid Id { get; set; }
        public Guid WorkOrderId { get; set; }
        public string LineKind { get; set; } = "labor";   // labor | part
        public string? Description { get; set; }
        public Guid? VariantId { get; set; }
        public int Quantity { get; set; } = 1;
        public int UnitPriceCents { get; set; }
        // Labor entered by time: hours and the $/hour rate applied. Set together, and only for a
        // labor line priced from hours; UnitPriceCents = round(LaborHours * LaborRateCents). Null on
        // a flat-priced labor line, on every part line, and on all lines that predate the rate.
        public decimal? LaborHours { get; set; }
        public int? LaborRateCents { get; set; }
        /// <summary>Estimated time for this labor line, summed into the job estimate. Null on parts.</summary>
        public int? EstimatedMinutes { get; set; }
        public bool Consumed { get; set; }
        /// <summary>Customer decision on this line: pending | approved | declined. A declined line is
        /// never consumed from stock nor billed. Pending (the default) behaves as before.</summary>
        public string ApprovalStatus { get; set; } = "pending";
        public DateTime? ApprovalAt { get; set; }
        public Guid? ApprovalByUserId { get; set; }
        // Special order: the supplier PO line this part is riding on, stamped when it lands.
        public Guid? PoLineId { get; set; }
        public DateTime? ArrivedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>One entry in a work order's internal notes thread: append-only, stamped with who
    /// wrote it and when. Never shown to the customer.</summary>
    public class ShopWorkOrderNote
    {
        public Guid Id { get; set; }
        public Guid WorkOrderId { get; set; }
        public string Body { get; set; } = null!;
        public Guid? CreatedByUserId { get; set; }
        /// <summary>Author's display name, resolved via join; null if the account is gone.</summary>
        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>A sibling ticket in the same customer visit: enough to list and jump to it.</summary>
    public class ShopWorkOrderGroupMember
    {
        public Guid Id { get; set; }
        public string? BikeLabel { get; set; }
        public string Status { get; set; } = "";
        public int TotalCents { get; set; }
    }

    public class ShopWorkOrderWithLines : ShopWorkOrder
    {
        public List<ShopWorkOrderLine> Lines { get; set; } = new();
        /// <summary>Internal notes thread, newest first. Populated on the detail load only.</summary>
        public List<ShopWorkOrderNote> Notes { get; set; } = new();
        /// <summary>Other bikes in the same visit (empty when solo). Detail load only.</summary>
        public List<ShopWorkOrderGroupMember> GroupMembers { get; set; } = new();
    }

    // One customer notification per work order touched by a PO receipt: which order, who to
    // tell, and where the receipt moved it ('ready' for a pure special order, 'in_progress'
    // when there's bench work left; null when the status didn't change).
    public class ShopWoArrival
    {
        public Guid WorkOrderId { get; set; }
        public string CustomerName { get; set; } = "";
        public string? CustomerEmail { get; set; }
        public string? CustomerBikeDesc { get; set; }
        public Guid? CustomerBikeId { get; set; }
        public string? NewStatus { get; set; }
    }
}
