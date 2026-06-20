// Mirror of webapi/AuthPolicies/TenantPermissions.cs.
// Keep in lock-step. Server always re-checks — this is only for hiding/showing UI.

export const Perm = {
    UsersManage: 'users.manage',
    SettingsManage: 'settings.manage',
    CatalogManage: 'catalog.manage',
    SalesCounter: 'sales.counter',
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
} as const

export type Permission = typeof Perm[keyof typeof Perm]

const ADMIN: Permission[] = Object.values(Perm)

const MANAGER: Permission[] = [
    Perm.CatalogManage, Perm.SalesCounter, Perm.SalesRedeem, Perm.SalesView,
    Perm.SalesCancel, Perm.ReportsView, Perm.DisputesView, Perm.CampaignsManage,
    Perm.CustomersView, Perm.BlogManage, Perm.SalesRefund, Perm.SalesRefundOverride,
]

const CASHIER: Permission[] = [Perm.SalesCounter, Perm.SalesRedeem, Perm.SalesView]
const SCANNER: Permission[] = [Perm.SalesRedeem]
const ACCOUNTANT: Permission[] = [Perm.SalesView, Perm.ReportsView, Perm.DisputesView, Perm.CustomersView]

export function permissionsForRole(role: string | null): ReadonlySet<Permission> {
    if (!role) return new Set()
    // Super admin has every permission on whatever tenant they're acting on.
    if (role === 'super_admin') return new Set(ADMIN)
    switch (role) {
        case 'tenant_admin': return new Set(ADMIN)
        case 'tenant_manager': return new Set(MANAGER)
        case 'tenant_cashier': return new Set(CASHIER)
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
    { value: 'tenant_cashier',    title: 'Cashier',    description: 'Counter sales and ticket redemption. No historical reports or refunds.' },
    { value: 'tenant_scanner',    title: 'Scanner',    description: 'Gate staff — redeem tickets only.' },
    { value: 'tenant_accountant', title: 'Accountant', description: 'Read-only access to purchases, reports, refunds, and disputes.' },
]
