import axios from 'axios'

export interface TenantSummary {
    id: string
    subdomain: string
    displayName: string
    status: string
    timezone: string
    serviceChargeBps: number
    monthlyServiceChargeCapCents: number | null
    isPublished: boolean
    giftCardsEnabled: boolean
    rentalsEnabled: boolean
    extrasEnabled: boolean
    seasonPassesEnabled: boolean
    concessionsEnabled: boolean
    blogEnabled: boolean
    membershipEnabled: boolean
    waitlistEnabled: boolean
    allowSelfCancel: boolean
    addressLine: string | null
    city: string | null
    region: string | null
    postalCode: string | null
    country: string | null
    latitude: number | null
    longitude: number | null
    contactEmail: string | null
    phone: string | null
    loampassMxDestinationId: string | null
    clientType: 'hosted' | 'custom_domain' | 'embedded'
    customDomain: string | null
    customDomainVerified: boolean
    embedEnabled: boolean
    embedAllowedOrigins: string[] | null
    externalHomeUrl: string | null
    externalEventsUrl: string | null
    embedEventTarget: 'external' | 'ridepass'
    createdAtUtc: string
}

export interface UpdateTenantPayload {
    displayName: string
    status: string
    timezone: string
    isPublished: boolean
    serviceChargeBps: number
    monthlyServiceChargeCapCents: number | null
    addressLine: string | null
    city: string | null
    region: string | null
    postalCode: string | null
    country: string | null
    latitude: number | null
    longitude: number | null
    contactEmail: string | null
    phone: string | null
    loampassMxDestinationId: string | null
    clientType: 'hosted' | 'custom_domain' | 'embedded'
    customDomain: string | null
    customDomainVerified: boolean
    embedEnabled: boolean
    embedAllowedOrigins: string[] | null
    externalHomeUrl: string | null
    externalEventsUrl: string | null
    embedEventTarget: 'external' | 'ridepass'
    giftCardsEnabled: boolean
    rentalsEnabled: boolean
    extrasEnabled: boolean
    seasonPassesEnabled: boolean
    concessionsEnabled: boolean
    blogEnabled: boolean
    membershipEnabled: boolean
    waitlistEnabled: boolean
    allowSelfCancel: boolean
}

export interface CreateTenantPayload {
    subdomain: string
    displayName: string
    tenantType: 'motocross' | 'mountain_bike'
    timezone: string
    adminEmail?: string | null
    adminFirstName?: string | null
    adminLastName?: string | null
    clientType: 'hosted' | 'custom_domain' | 'embedded'
    customDomain: string | null
    customDomainVerified: boolean
    embedEnabled: boolean
    embedAllowedOrigins: string[] | null
    externalHomeUrl: string | null
    externalEventsUrl: string | null
    embedEventTarget: 'external' | 'ridepass'
    giftCardsEnabled: boolean
    rentalsEnabled: boolean
    extrasEnabled: boolean
    seasonPassesEnabled: boolean
    concessionsEnabled: boolean
    blogEnabled: boolean
    membershipEnabled: boolean
    waitlistEnabled: boolean
    allowSelfCancel: boolean
}

export interface CreateTenantResult {
    tenantId: string
    subdomain: string
    displayName: string
    tenantType: 'motocross' | 'mountain_bike'
    timezone: string
    adminUserId?: string | null
    adminEmail?: string | null
    adminTemporaryPassword?: string | null
}

export interface SuperAdminUser {
    id: string
    tenantId: string | null
    tenantSubdomain: string | null
    email: string
    firstName: string
    lastName: string
    role: string
    status: string
    phone: string | null
}

export interface SuperAdminUserDetail extends SuperAdminUser {
    birthdate: string | null
    emergencyContactName: string | null
    emergencyContactPhone: string | null
    addressLine: string | null
    addressLine2: string | null
    city: string | null
    state: string | null
    postalCode: string | null
    country: string | null
    bike: string | null
    raceNumber: string | null
    emailVerified: boolean
    createdAtUtc: string
}

export interface UpdateUserPayload {
    email: string
    firstName: string
    lastName: string
    role: string
    status: string
    phone: string | null
    birthdate: string | null
    emergencyContactName: string | null
    emergencyContactPhone: string | null
    addressLine: string | null
    addressLine2: string | null
    city: string | null
    state: string | null
    postalCode: string | null
    country: string | null
    bike: string | null
    raceNumber: string | null
    emailVerified: boolean
}

export interface ImpersonationResult {
    token: string
    userId: string
    email: string
    firstName: string
    lastName: string
    role: string
    tenantId: string | null
    tenantSubdomain: string | null
}

export interface MiscSettings {
    globalEmbedAllowedOrigins: string[]
}

export class SuperAdminService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    bootstrap(body: { email: string; password: string; firstName: string; lastName: string }) {
        return axios.post(`${this.apiUrl}/SuperAdmin/Bootstrap`, body)
    }

    createSuperAdmin(body: { email: string; password: string; firstName: string; lastName: string }) {
        return axios.post(`${this.apiUrl}/SuperAdmin/SuperAdmins`, body)
    }

    listTenants() {
        return axios.get<{ data: TenantSummary[] }>(`${this.apiUrl}/SuperAdmin/Tenants`)
    }

    getMiscSettings() {
        return axios.get<{ data: MiscSettings }>(`${this.apiUrl}/SuperAdmin/Settings/Misc`)
    }

    updateMiscSettings(body: MiscSettings) {
        return axios.put<{ data: MiscSettings }>(`${this.apiUrl}/SuperAdmin/Settings/Misc`, body)
    }

    createTenant(body: CreateTenantPayload) {
        return axios.post<{ data: CreateTenantResult }>(`${this.apiUrl}/SuperAdmin/Tenants`, body)
    }

    listUsers(q?: string) {
        return axios.get<{ data: SuperAdminUser[] }>(`${this.apiUrl}/SuperAdmin/Users`, { params: { q } })
    }

    getUser(id: string) {
        return axios.get<{ data: SuperAdminUserDetail }>(`${this.apiUrl}/SuperAdmin/Users/${id}`)
    }

    updateUser(id: string, body: UpdateUserPayload) {
        return axios.put<{ data: SuperAdminUserDetail }>(`${this.apiUrl}/SuperAdmin/Users/${id}`, body)
    }

    impersonate(userId: string) {
        return axios.post<{ data: ImpersonationResult }>(`${this.apiUrl}/SuperAdmin/Impersonate/${userId}`)
    }

    listRefunds() {
        return axios.get<{ data: RefundListItem[] }>(`${this.apiUrl}/SuperAdmin/Refunds`)
    }

    processPassRefund(id: string) {
        return axios.post(`${this.apiUrl}/SuperAdmin/Refunds/Pass/${id}/Process`)
    }

    processTicketRefund(id: string) {
        return axios.post(`${this.apiUrl}/SuperAdmin/Refunds/Ticket/${id}/Process`)
    }

    listDisputes() {
        return axios.get<{ data: DisputeListItem[] }>(`${this.apiUrl}/SuperAdmin/Disputes`)
    }

    // Payouts / balances
    listBalances() {
        return axios.get<{ data: TenantBalanceSummary[] }>(`${this.apiUrl}/SuperAdmin/Balances`)
    }

    listLedger(tenantId: string, fromUtc?: string, toUtc?: string, take = 200) {
        return axios.get<{ data: LedgerEntry[] }>(`${this.apiUrl}/SuperAdmin/Tenants/${tenantId}/Ledger`, {
            params: { fromUtc, toUtc, take },
        })
    }

    listPayouts(tenantId: string) {
        return axios.get<{ data: PayoutSummary[] }>(`${this.apiUrl}/SuperAdmin/Tenants/${tenantId}/Payouts`)
    }

    getPayout(tenantId: string, payoutId: string) {
        return axios.get<{ data: { payout: PayoutSummary; entries: LedgerEntry[] } }>(
            `${this.apiUrl}/SuperAdmin/Tenants/${tenantId}/Payouts/${payoutId}`)
    }

    createPayout(tenantId: string, body: { periodStartUtc: string; periodEndUtc: string; memo: string | null }) {
        return axios.post<{ data: { payout: PayoutSummary; attachedCount: number } }>(
            `${this.apiUrl}/SuperAdmin/Tenants/${tenantId}/Payouts`, body)
    }

    updatePayoutStatus(tenantId: string, payoutId: string,
        body: { status: string; payoutDateUtc?: string | null; externalReference?: string | null; memo?: string | null }) {
        return axios.put<{ data: PayoutSummary }>(
            `${this.apiUrl}/SuperAdmin/Tenants/${tenantId}/Payouts/${payoutId}/Status`, body)
    }

    voidPayout(tenantId: string, payoutId: string) {
        return axios.delete(`${this.apiUrl}/SuperAdmin/Tenants/${tenantId}/Payouts/${payoutId}`)
    }

    sendPayoutViaStripe(tenantId: string, payoutId: string) {
        return axios.post<{ data: { payout: PayoutSummary; transferId: string } }>(
            `${this.apiUrl}/SuperAdmin/Tenants/${tenantId}/Payouts/${payoutId}/SendViaStripe`)
    }

    listCouponShares(tenantId?: string) {
        return axios.get<{ data: CouponShareRow[] }>(`${this.apiUrl}/SuperAdmin/Marketing/CouponShares`,
            { params: tenantId ? { tenantId } : undefined })
    }

    listAuditLog(params: { action?: string; actorUserId?: string; targetKind?: string; targetId?: string; tenantId?: string; fromUtc?: string; toUtc?: string; take?: number } = {}) {
        return axios.get<{ data: AuditLogEntry[] }>(`${this.apiUrl}/SuperAdmin/AuditLog`, { params })
    }

    updateTenantServiceCharge(tenantId: string, body: { serviceChargeBps: number; monthlyServiceChargeCapCents: number | null }) {
        return axios.put(`${this.apiUrl}/SuperAdmin/Tenants/${tenantId}/ServiceCharge`, body)
    }

    updateTenant(tenantId: string, body: UpdateTenantPayload) {
        return axios.put(`${this.apiUrl}/SuperAdmin/Tenants/${tenantId}`, body)
    }

    updateTenantConcessionsEnabled(tenantId: string, enabled: boolean) {
        return axios.put(`${this.apiUrl}/SuperAdmin/Tenants/${tenantId}/ConcessionsEnabled`, { enabled })
    }

    getReconciliation(fromUtc: string, toUtc: string) {
        return axios.get<{ data: ReconciliationResult }>(`${this.apiUrl}/SuperAdmin/Reconciliation`, {
            params: { fromUtc, toUtc },
        })
    }

    async downloadPayoutCsv(tenantId: string, payoutId: string) {
        const r = await axios.get(`${this.apiUrl}/SuperAdmin/Tenants/${tenantId}/Payouts/${payoutId}/Csv`, { responseType: 'blob' })
        const cd = (r.headers['content-disposition'] as string | undefined) ?? ''
        const filename = cd.match(/filename="?([^";]+)"?/)?.[1] ?? `payout-${payoutId}.csv`
        triggerBlobDownload(r.data, filename)
    }
}

function triggerBlobDownload(blob: Blob, filename: string) {
    const url = window.URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = filename
    document.body.appendChild(a)
    a.click()
    a.remove()
    setTimeout(() => window.URL.revokeObjectURL(url), 0)
}

export { triggerBlobDownload }

export interface TenantBalanceSummary {
    tenantId: string
    tenantSubdomain: string
    tenantDisplayName: string
    availableBalanceCents: number
    lifetimeGrossCents: number
    lifetimeStripeFeeCents: number
    lifetimeRidepassCutCents: number
    lifetimePaidOutCents: number
    currentMonthGrossCents: number
}

export interface LedgerEntry {
    id: string
    tenantId: string
    entryKind: string
    sourceKind: string | null
    sourceId: string | null
    occurredAtUtc: string
    grossCents: number
    stripeFeeCents: number
    ridepassCutCents: number
    netToTenantCents: number
    appliedTierId: string | null
    cumulativeMonthlyVolumeAtSaleCents: number | null
    stripePaymentIntentId: string | null
    payoutId: string | null
    memo: string | null
    createdAt: string
}

export interface PayoutSummary {
    id: string
    tenantId: string
    status: string
    periodStartUtc: string
    periodEndUtc: string
    payoutDateUtc: string | null
    totalGrossCents: number
    totalStripeFeeCents: number
    totalRidepassCutCents: number
    totalAdjustmentCents: number
    netPaidCents: number
    externalReference: string | null
    memo: string | null
    createdAt: string
}

export interface AuditLogEntry {
    id: string
    actorUserId: string | null
    actorEmail: string | null
    actorRole: string | null
    action: string
    targetKind: string | null
    targetId: string | null
    summary: string
    metadata: string | null
    ipAddress: string | null
    tenantId: string | null
    createdAt: string
}

export interface DisputeListItem {
    id: string
    tenantId: string
    tenantSubdomain: string
    kind: 'pass' | 'event_ticket' | 'unlinked'
    purchaseId: string | null
    itemName: string | null
    purchaserName: string | null
    purchaserEmail: string | null
    stripeDisputeId: string
    stripePaymentIntentId: string
    stripeChargeId: string | null
    amountCents: number
    currency: string
    reason: string | null
    status: string
    evidenceDueByUtc: string | null
    stripeCreatedAtUtc: string
    updatedAtUtc: string
}

export interface ReconciliationResult {
    fromUtc: string
    toUtc: string
    stripe: { count: number; grossCents: number; feeCents: number; netCents: number } | null
    ledger: { count: number; grossCents: number; stripeFeeCents: number; ridepassCutCents: number; netToTenantCents: number }
    gaps: { grossGap: number; feeGap: number; netGap: number; expectedStripeNet: number }
    stripeConfigured: boolean
}

export interface RefundListItem {
    kind: 'pass' | 'event_ticket'
    id: string
    tenantId: string
    tenantSubdomain: string
    itemName: string
    purchaserName: string
    purchaserEmail: string
    amountCents: number
    cancellationReason: string | null
    cancelledAtUtc: string | null
    createdAtUtc: string
    stripePaymentIntentId: string | null
}

export interface CouponShareRow {
    tenantSubdomain: string
    tenantDisplayName: string
    recipientEmail: string
    recipientName: string | null
    sentAtUtc: string
    redeemedAtUtc: string | null
}
