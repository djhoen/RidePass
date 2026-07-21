// Mirror of webapi/AuthPolicies/TenantPermissions.cs.
// Keep in lock-step. Server always re-checks — this is only for hiding/showing UI.

export const Perm = {
    UsersManage: 'users.manage',
    SettingsManage: 'settings.manage',
    CatalogManage: 'catalog.manage',
    SalesCounter: 'sales.counter',
    // Food & Beverage counter (POS + kitchen screen). Split out of SalesCounter so a gate
    // cashier, an F&B cashier, and a bike shop cashier are three distinct hires.
    ConcessionsCounter: 'concessions.counter',
    // Bike shop counter (register/rentals/work orders/shop sales). Separate from SalesCounter
    // so gate/F&B cashiers and bike shop cashiers are distinct; hold both roles to work both.
    ShopCounter: 'shop.counter',
    SalesRedeem: 'sales.redeem',
    SalesView: 'sales.view',
    SalesCancel: 'sales.cancel',
    ReportsView: 'reports.view',
    DisputesView: 'disputes.view',
    CampaignsManage: 'campaigns.manage',
    CustomersView: 'customers.view',
    BlogManage: 'blog.manage',
    SalesRefund: 'sales.refund',
    SalesRefundOverride: 'sales.refund.override',
    // Worker side: open your own cash session and submit a blind-count turn-in. Separate from
    // SalesCounter so a bike shop cashier (who takes cash but has no gate/F&B counter) can
    // still hand their drawer in. CashReconcile is the manager side and never held by a cashier.
    CashTurnIn: 'cash.turnin',
    CashReconcile: 'cash.reconcile',
    AccountingManage: 'accounting.manage',
} as const

export type Permission = typeof Perm[keyof typeof Perm]

const ADMIN: Permission[] = Object.values(Perm)

const MANAGER: Permission[] = [
    Perm.CatalogManage, Perm.SalesCounter, Perm.ConcessionsCounter, Perm.ShopCounter,
    Perm.SalesRedeem, Perm.SalesView,
    Perm.SalesCancel, Perm.ReportsView, Perm.DisputesView, Perm.CampaignsManage,
    Perm.CustomersView, Perm.BlogManage, Perm.SalesRefund, Perm.SalesRefundOverride,
    Perm.CashTurnIn, Perm.CashReconcile,
]

// Gate counter only; F&B split out into its own role below.
const CASHIER: Permission[] = [Perm.SalesCounter, Perm.SalesRedeem, Perm.SalesView, Perm.CashTurnIn]
const FNB_CASHIER: Permission[] = [Perm.ConcessionsCounter, Perm.CashTurnIn]
const SHOP_CASHIER: Permission[] = [Perm.ShopCounter, Perm.CashTurnIn]
const SCANNER: Permission[] = [Perm.SalesRedeem]
const ACCOUNTANT: Permission[] = [
    Perm.SalesView, Perm.ReportsView, Perm.DisputesView, Perm.CustomersView,
    Perm.CashReconcile, Perm.AccountingManage,
]

export function permissionsForRole(role: string | null): ReadonlySet<Permission> {
    if (!role) return new Set()
    // Super admin has every permission on whatever tenant they're acting on.
    if (role === 'super_admin') return new Set(ADMIN)
    switch (role) {
        case 'tenant_admin': return new Set(ADMIN)
        case 'tenant_manager': return new Set(MANAGER)
        case 'tenant_cashier': return new Set(CASHIER)
        case 'tenant_fnb_cashier': return new Set(FNB_CASHIER)
        case 'tenant_shop_cashier': return new Set(SHOP_CASHIER)
        case 'tenant_scanner': return new Set(SCANNER)
        case 'tenant_staff': return new Set(SCANNER)    // legacy alias
        case 'tenant_accountant': return new Set(ACCOUNTANT)
        default: return new Set()
    }
}

// A multi-role staffer's effective permissions are the union of each role's set.
export function permissionsForRoles(roles: readonly string[] | null): ReadonlySet<Permission> {
    const union = new Set<Permission>()
    for (const r of roles ?? []) {
        for (const p of permissionsForRole(r)) union.add(p)
    }
    return union
}

export const ASSIGNABLE_ROLES: { value: string; title: string; description: string }[] = [
    { value: 'tenant_admin',      title: 'Admin',      description: 'Full access including user management and branding.' },
    { value: 'tenant_manager',    title: 'Manager',    description: 'Run day-to-day: catalog, sales, reports, refunds. No user or branding edits.' },
    { value: 'tenant_cashier',    title: 'Gate Cashier', description: 'Gate counter sales and ticket redemption, plus turning in their own cash. No F&B or bike shop.' },
    { value: 'tenant_fnb_cashier', title: 'F&B Cashier', description: 'Food & Beverage POS and kitchen screen, plus turning in their own cash. No gate or bike shop.' },
    { value: 'tenant_shop_cashier', title: 'Bike Shop Cashier', description: 'Bike shop register, rentals, and work orders, plus turning in their own cash. No gate or F&B counter.' },
    { value: 'tenant_scanner',    title: 'Scanner',    description: 'Gate staff — redeem tickets only.' },
    { value: 'tenant_accountant', title: 'Accountant', description: 'Purchases, reports, and disputes, plus cash reconciliation and the QuickBooks sync.' },
]
