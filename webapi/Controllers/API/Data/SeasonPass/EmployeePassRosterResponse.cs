namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>
    /// One row of the admin Employee Passes roster. Eligibility (an active account on the tenant)
    /// is automatic and grants nothing; approval is what creates a pass, so most rows carry no
    /// pass at all.
    /// </summary>
    public class EmployeePassRosterItem
    {
        public Guid UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Role { get; set; }

        /// <summary>True while the account is active on this tenant. When false, any pass this
        /// person holds stops admitting immediately.</summary>
        public bool IsActiveEmployee { get; set; }

        public Guid? PassPurchaseId { get; set; }
        public string? ProductName { get; set; }
        public int? AmountCents { get; set; }
        public DateTime? ValidFromDate { get; set; }
        public DateTime? ValidToDate { get; set; }
        public DateTime? IssuedAtUtc { get; set; }
        public string? IssuedByName { get; set; }

        /// <summary>
        /// Single label the admin page renders, computed server-side so the page and the gate can
        /// never disagree about why someone is or isn't getting in. One of:
        /// none | pending_payment | not_registered | active | inactive_employee.
        /// </summary>
        public string PassState { get; set; } = "none";
    }

    public class EmployeePassRosterResponse
    {
        public List<EmployeePassRosterItem> Rows { get; set; } = new();

        /// <summary>The tenant's employee pass products, for the issue dialog's picker and the
        /// Discounts tab. Empty means none has been configured yet and nothing can be issued.</summary>
        public List<EmployeePassProductOption> Products { get; set; } = new();

        /// <summary>The tenant's event types, so the Discounts tab can offer a per-type row using
        /// the track's own names ("Lift Day", "Clinic") rather than internal codes.</summary>
        public List<EmployeeEventTypeOption> EventTypes { get; set; } = new();

        /// <summary>
        /// Which benefit surfaces are actually honoured at a till today. The Discounts tab uses
        /// this to disable a control rather than let a tenant configure a discount that silently
        /// never applies. Keys: event | retail | rental | concession.
        /// </summary>
        public Dictionary<string, bool> SurfaceLive { get; set; } = new();
    }

    public class EmployeePassProductOption
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int PriceCents { get; set; }
        public DateTime ValidFromDate { get; set; }
        public DateTime ValidToDate { get; set; }
        public bool IsActive { get; set; }

        /// <summary>What this employee pass grants. Round-tripped by the Discounts tab.</summary>
        public List<SeasonPassBenefitInput> Benefits { get; set; } = new();
    }

    public class EmployeeEventTypeOption
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Code { get; set; }
    }
}
