namespace webapi.AuthPolicies
{
    /// <summary>
    /// Central catalog of per-tenant capability keys + their role mapping. Every protected
    /// tenant endpoint references one of these. Frontend mirrors this list in authHelper
    /// to decide what to render — always re-checked server-side.
    /// </summary>
    public static class TenantPermissions
    {
        public const string UsersManage    = "users.manage";
        public const string SettingsManage = "settings.manage";
        public const string CatalogManage  = "catalog.manage";
        public const string SalesCounter   = "sales.counter";
        // Food & Beverage counter: the F&B POS, kitchen screen, and F&B order handling.
        // Split out of sales.counter so a gate cashier, an F&B cashier, and a bike shop cashier
        // are three distinct hires; someone who works two of them holds two roles.
        public const string ConcessionsCounter = "concessions.counter";
        // Bike shop counter: register, rentals, work orders, shop sale history. Deliberately
        // separate from sales.counter so a gate/F&B cashier and a bike shop cashier are distinct
        // hires; a staffer who works both counters holds both roles (permissions union).
        public const string ShopCounter    = "shop.counter";
        public const string SalesRedeem    = "sales.redeem";
        public const string SalesView      = "sales.view";
        public const string SalesCancel    = "sales.cancel";
        public const string ReportsView    = "reports.view";
        public const string DisputesView   = "disputes.view";
        public const string CampaignsManage = "campaigns.manage";
        public const string CustomersView  = "customers.view";
        public const string BlogManage     = "blog.manage";
        public const string SalesRefund    = "sales.refund";
        // Elevated: refund a purchase even after it's been checked in / used, and refund a whole
        // order at once. Held by tenant_admin + tenant_manager only.
        public const string SalesRefundOverride = "sales.refund.override";
        // Worker-side cash handling: open your own cash session and submit a blind-count
        // turn-in. Split out from sales.counter because a BIKE SHOP cashier takes cash too but
        // must not get the gate/F&B counter (the two are deliberately separate hires). Pairs
        // with cash.reconcile, which is the manager side and is never held by a cashier.
        public const string CashTurnIn = "cash.turnin";
        // Manager-side cash reconciliation: confirm a worker's blind-count cash turn-in and
        // view the reconciliation report. Deliberately NOT in the cashier set, so a worker
        // can never confirm their own turn-in. Held by admin + manager + accountant.
        public const string CashReconcile = "cash.reconcile";
        // Connect/disconnect the QuickBooks link, map the chart of accounts, and re-run a day's
        // sync. Held by admin + accountant: the accountant is exactly who owns the books, and it
        // deliberately stays out of the manager set — mapping revenue to the wrong account is an
        // accounting decision, not an operational one.
        public const string AccountingManage = "accounting.manage";
        // Read the staff activity log: who did what, when, and from where. Held by admin only,
        // deliberately NOT by manager: it exposes every colleague's actions including the owner's,
        // so it is oversight rather than an operational tool. Widening it to the manager set later
        // is one line; narrowing it after people rely on it is not. Every staffer can always see
        // their OWN activity without this, which needs no permission at all.
        public const string AuditView = "audit.view";

        // Compile-time policy names for [Authorize(Policy = ...)] attributes.
        // Must match TenantPermissionRequirement.PolicyName(perm) format.
        public static class Policy
        {
            public const string UsersManage    = "TenantPerm:users.manage";
            public const string SettingsManage = "TenantPerm:settings.manage";
            public const string CatalogManage  = "TenantPerm:catalog.manage";
            public const string SalesCounter   = "TenantPerm:sales.counter";
            public const string ConcessionsCounter = "TenantPerm:concessions.counter";
            public const string ShopCounter    = "TenantPerm:shop.counter";
            public const string SalesRedeem    = "TenantPerm:sales.redeem";
            public const string SalesView      = "TenantPerm:sales.view";
            public const string SalesCancel    = "TenantPerm:sales.cancel";
            public const string ReportsView    = "TenantPerm:reports.view";
            public const string DisputesView   = "TenantPerm:disputes.view";
            public const string CampaignsManage = "TenantPerm:campaigns.manage";
            public const string CustomersView  = "TenantPerm:customers.view";
            public const string BlogManage     = "TenantPerm:blog.manage";
            public const string SalesRefund    = "TenantPerm:sales.refund";
            public const string SalesRefundOverride = "TenantPerm:sales.refund.override";
            public const string CashTurnIn = "TenantPerm:cash.turnin";
            public const string CashReconcile = "TenantPerm:cash.reconcile";
            public const string AccountingManage = "TenantPerm:accounting.manage";
            public const string AuditView      = "TenantPerm:audit.view";

            /// <summary>
            /// READ the shop catalog: catalog.manage OR shop.counter. The bike shop counter cannot
            /// do its job blind — the register needs the product list to ring a sale, rentals need
            /// it to book gear, and work orders need it to add parts — but a cashier must not be
            /// able to EDIT the catalog. Reads carry this; every write stays on CatalogManage.
            /// </summary>
            public const string CatalogRead = "TenantPermAny:catalog.manage|shop.counter";
            // Any counter can read the discount list: a cashier has to see the buttons they're
            // allowed to press, and the list is configuration rather than customer data. Managing
            // discounts is SettingsManage, which is a different question entirely.
            public const string AnyCounter = "TenantPermAny:sales.counter|concessions.counter|shop.counter";
            // A person's shift review / shift close (Script0298): reached from the F&B register by
            // whoever punched in (concessions.counter) and by anyone who handles a drawer
            // (cash.turnin). The close only settles the caller's (or the punched-in person's) own
            // tips, so neither side needs the other's permission to do it.
            public const string ShiftClose = "TenantPermAny:concessions.counter|cash.turnin";
        }

        /// <summary>Any-of policies, as (policy name, permissions) so Program.cs registers exactly
        /// what the Policy constants above name. Keep the order matching the constant's string.</summary>
        public static readonly (string PolicyName, string[] Permissions)[] AnyOfPolicies =
        {
            (Policy.CatalogRead, new[] { CatalogManage, ShopCounter }),
            (Policy.AnyCounter, new[] { SalesCounter, ConcessionsCounter, ShopCounter }),
            (Policy.ShiftClose, new[] { ConcessionsCounter, CashTurnIn }),
        };

        /// <summary>Every tenant staff role ForRole knows (the legacy alias included).</summary>
        public static readonly string[] StaffRoles =
        {
            "tenant_admin", "tenant_manager", "tenant_cashier", "tenant_fnb_cashier",
            "tenant_shop_cashier", "tenant_scanner", "tenant_accountant", "tenant_staff",
        };

        /// <summary>The roles whose permission set includes <paramref name="permission"/>.</summary>
        public static string[] RolesGranting(string permission) =>
            StaffRoles.Where(r => ForRole(r).Contains(permission)).ToArray();

        /// <summary>
        /// The one gate for holding a POS code and taking a share of the tip jar (Script0298):
        /// works the F&B register, or is a manager/admin. Every caller that sets, verifies or
        /// lists codes goes through this (or CounterStaffRoles, which is this applied to each role).
        /// </summary>
        public static bool MayHoldPosCode(string? role, string[]? roles) =>
            IsCounterStaff(role, roles) || IsManagerOrAdmin(role, roles);

        /// <summary>
        /// MayHoldPosCode as a role list, for SQL that has to filter by role. Derived from the
        /// predicate so the two can never disagree. Lazy because the role sets are static fields
        /// declared further down, and a field initializer here would run before them.
        /// </summary>
        public static string[] CounterStaffRoles => _counterStaffRoles.Value;
        private static readonly Lazy<string[]> _counterStaffRoles = new(() =>
            StaffRoles.Where(r => MayHoldPosCode(r, null)).ToArray());
        private static readonly Lazy<string[]> _registerRoles = new(() => RolesGranting(ConcessionsCounter));

        // The one vocabulary for "is this person a manager" and "does this person work the F&B
        // register", read off a user's primary role and roles array (either may carry it: the
        // array is the full set, the primary is kept in lock-step for older rows). Every caller
        // that gates a POS code, a staff code, a comp approval or a jar share goes through these.
        public static bool IsManagerOrAdmin(string? role, string[]? roles) =>
            HasAny(role, roles, r => r is "tenant_admin" or "tenant_manager");

        /// <summary>Holds a role whose permission set grants concessions.counter (admins and managers included).</summary>
        public static bool IsCounterStaff(string? role, string[]? roles) =>
            HasAny(role, roles, r => _registerRoles.Value.Contains(r));

        private static bool HasAny(string? role, string[]? roles, Func<string, bool> test) =>
            (roles ?? System.Array.Empty<string>()).Any(test) || (role is not null && test(role));

        public static readonly string[] All =
        {
            UsersManage, SettingsManage, CatalogManage,
            SalesCounter, ConcessionsCounter, ShopCounter, SalesRedeem, SalesView, SalesCancel,
            ReportsView, DisputesView, CampaignsManage, CustomersView,
            BlogManage, SalesRefund, SalesRefundOverride, CashTurnIn, CashReconcile, AccountingManage,
            AuditView,
        };

        public static IReadOnlySet<string> ForRole(string role) =>
            role switch
            {
                "tenant_admin"      => AdminSet,
                "tenant_manager"    => ManagerSet,
                "tenant_cashier"    => CashierSet,
                "tenant_fnb_cashier" => FnbCashierSet,
                "tenant_shop_cashier" => ShopCashierSet,
                "tenant_scanner"    => ScannerSet,
                "tenant_accountant" => AccountantSet,
                "tenant_staff"      => ScannerSet,     // legacy alias
                _                   => EmptySet,
            };

        /// <summary>Union of the permission sets for every role the user holds.</summary>
        /// <summary>
        /// The permissions a tenant's staff access policy can restrict by location and hours
        /// (Script0239). These are the operations that move money or admit people: doing one of
        /// them from an employee's home at 2am is almost definitionally not legitimate work.
        ///
        /// What is deliberately NOT here matters as much as what is. settings.manage and
        /// users.manage are never restricted, so an owner who mis-configures their own policy can
        /// always sign in from anywhere and fix it: a lockout with no way back is an outage, not a
        /// control. The read-only and back-office permissions (reports.view, sales.view,
        /// catalog.manage, campaigns.manage, blog.manage, customers.view, accounting.manage,
        /// disputes.view, audit.view) are also excluded, because doing paperwork from the couch on
        /// a Tuesday night is normal and blocking it would only teach people to resent the rule.
        /// </summary>
        public static readonly IReadOnlySet<string> LocationRestrictable = new HashSet<string>
        {
            SalesCounter, ConcessionsCounter, ShopCounter, SalesRedeem,
            SalesRefund, SalesRefundOverride, SalesCancel,
            CashTurnIn, CashReconcile,
        };

        /// <summary>
        /// The refund permissions a venue can reserve for its Admins (tenant.refunds_admin_only,
        /// Script0314). Everything else a Manager holds is unchanged.
        /// </summary>
        public static readonly IReadOnlySet<string> RefundPermissions = new HashSet<string>
        {
            SalesRefund, SalesRefundOverride,
        };

        /// <summary>
        /// ForRoles at a specific venue: when the venue keeps refunds for Admins, a staffer who is
        /// not a tenant_admin loses sales.refund and sales.refund.override. Every permission check
        /// that reads a role's set for the current tenant must go through this, or a Manager could
        /// still refund through whichever path was missed. Super admins are handled by callers
        /// (they pass every check before reaching a set).
        /// </summary>
        public static IReadOnlySet<string> ForRolesAt(IEnumerable<string> roles, bool refundsAdminOnly)
        {
            var list = roles as ICollection<string> ?? roles.ToList();
            var held = ForRoles(list);
            if (!refundsAdminOnly || list.Contains("tenant_admin")) return held;
            var narrowed = new HashSet<string>(held);
            narrowed.ExceptWith(RefundPermissions);
            return narrowed;
        }

        public static IReadOnlySet<string> ForRoles(IEnumerable<string> roles)
        {
            var union = new HashSet<string>();
            foreach (var r in roles)
            {
                union.UnionWith(ForRole(r));
            }
            return union;
        }

        // Highest-privilege role wins as the primary (scope/identity/display). Lower index =
        // higher privilege. Anything unknown sorts last so a real role is always preferred.
        private static readonly string[] Precedence =
        {
            "super_admin", "tenant_admin", "tenant_manager", "tenant_accountant",
            "tenant_cashier", "tenant_fnb_cashier", "tenant_shop_cashier", "tenant_scanner",
            "tenant_staff", "rider",
        };

        /// <summary>Pick the canonical primary role from a set (for JWT identity / display).</summary>
        public static string PrimaryRole(IEnumerable<string> roles)
        {
            string? best = null;
            var bestRank = int.MaxValue;
            foreach (var r in roles)
            {
                var rank = System.Array.IndexOf(Precedence, r);
                if (rank < 0) rank = Precedence.Length;
                if (rank < bestRank) { bestRank = rank; best = r; }
            }
            return best ?? "";
        }

        private static readonly HashSet<string> EmptySet = new();

        private static readonly HashSet<string> AdminSet = new(All);

        private static readonly HashSet<string> ManagerSet = new()
        {
            CatalogManage, SalesCounter, ConcessionsCounter, ShopCounter, SalesRedeem, SalesView, SalesCancel,
            ReportsView, DisputesView, CampaignsManage, CustomersView,
            BlogManage, SalesRefund, SalesRefundOverride, CashTurnIn, CashReconcile,
        };

        // Gate counter only. Since concessions.counter split out, this no longer opens the F&B
        // POS; a staffer who works both the gate and the food window holds both roles.
        private static readonly HashSet<string> CashierSet = new()
        {
            SalesCounter, SalesRedeem, SalesView, CashTurnIn,
        };

        // Food & Beverage counter only: no gate counter, no bike shop, no ticket scanning.
        private static readonly HashSet<string> FnbCashierSet = new()
        {
            ConcessionsCounter, CashTurnIn,
        };

        // Bike shop counter only: no gate/F&B counter, no ticket scanning, no purchase history.
        private static readonly HashSet<string> ShopCashierSet = new()
        {
            ShopCounter,
            // Shop cash sales are attributed to the seller and already count toward this
            // worker's expected cash (TenantLedgerRepository.SumCashNetForWorker is
            // source-kind agnostic), so without this they would accrue cash they could
            // never turn in.
            CashTurnIn,
        };

        private static readonly HashSet<string> ScannerSet = new()
        {
            SalesRedeem,
        };

        private static readonly HashSet<string> AccountantSet = new()
        {
            SalesView, ReportsView, DisputesView, CustomersView, CashReconcile, AccountingManage,
        };
    }
}
