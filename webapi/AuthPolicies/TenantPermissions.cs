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
        public const string SalesRedeem    = "sales.redeem";
        public const string SalesView      = "sales.view";
        public const string SalesCancel    = "sales.cancel";
        public const string ReportsView    = "reports.view";
        public const string DisputesView   = "disputes.view";
        public const string CampaignsManage = "campaigns.manage";
        public const string CustomersView  = "customers.view";

        // Compile-time policy names for [Authorize(Policy = ...)] attributes.
        // Must match TenantPermissionRequirement.PolicyName(perm) format.
        public static class Policy
        {
            public const string UsersManage    = "TenantPerm:users.manage";
            public const string SettingsManage = "TenantPerm:settings.manage";
            public const string CatalogManage  = "TenantPerm:catalog.manage";
            public const string SalesCounter   = "TenantPerm:sales.counter";
            public const string SalesRedeem    = "TenantPerm:sales.redeem";
            public const string SalesView      = "TenantPerm:sales.view";
            public const string SalesCancel    = "TenantPerm:sales.cancel";
            public const string ReportsView    = "TenantPerm:reports.view";
            public const string DisputesView   = "TenantPerm:disputes.view";
            public const string CampaignsManage = "TenantPerm:campaigns.manage";
            public const string CustomersView  = "TenantPerm:customers.view";
        }

        public static readonly string[] All =
        {
            UsersManage, SettingsManage, CatalogManage,
            SalesCounter, SalesRedeem, SalesView, SalesCancel,
            ReportsView, DisputesView, CampaignsManage, CustomersView,
        };

        public static IReadOnlySet<string> ForRole(string role) =>
            role switch
            {
                "tenant_admin"      => AdminSet,
                "tenant_manager"    => ManagerSet,
                "tenant_cashier"    => CashierSet,
                "tenant_scanner"    => ScannerSet,
                "tenant_accountant" => AccountantSet,
                "tenant_staff"      => ScannerSet,     // legacy alias
                _                   => EmptySet,
            };

        private static readonly HashSet<string> EmptySet = new();

        private static readonly HashSet<string> AdminSet = new(All);

        private static readonly HashSet<string> ManagerSet = new()
        {
            CatalogManage, SalesCounter, SalesRedeem, SalesView, SalesCancel,
            ReportsView, DisputesView, CampaignsManage, CustomersView,
        };

        private static readonly HashSet<string> CashierSet = new()
        {
            SalesCounter, SalesRedeem, SalesView,
        };

        private static readonly HashSet<string> ScannerSet = new()
        {
            SalesRedeem,
        };

        private static readonly HashSet<string> AccountantSet = new()
        {
            SalesView, ReportsView, DisputesView, CustomersView,
        };
    }
}
