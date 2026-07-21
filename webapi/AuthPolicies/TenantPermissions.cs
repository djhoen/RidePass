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
        }

        public static readonly string[] All =
        {
            UsersManage, SettingsManage, CatalogManage,
            SalesCounter, ConcessionsCounter, ShopCounter, SalesRedeem, SalesView, SalesCancel,
            ReportsView, DisputesView, CampaignsManage, CustomersView,
            BlogManage, SalesRefund, SalesRefundOverride, CashTurnIn, CashReconcile, AccountingManage,
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
