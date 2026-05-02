import axios from 'axios'

export interface TenantSummary {
    id: string
    subdomain: string
    displayName: string
    status: string
    timezone: string
    createdAtUtc: string
}

export interface CreateTenantPayload {
    subdomain: string
    displayName: string
    timezone: string
    adminEmail?: string | null
    adminFirstName?: string | null
    adminLastName?: string | null
}

export interface CreateTenantResult {
    tenantId: string
    subdomain: string
    displayName: string
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

    createTenant(body: CreateTenantPayload) {
        return axios.post<{ data: CreateTenantResult }>(`${this.apiUrl}/SuperAdmin/Tenants`, body)
    }

    listUsers(q?: string) {
        return axios.get<{ data: SuperAdminUser[] }>(`${this.apiUrl}/SuperAdmin/Users`, { params: { q } })
    }

    impersonate(userId: string) {
        return axios.post<{ data: ImpersonationResult }>(`${this.apiUrl}/SuperAdmin/Impersonate/${userId}`)
    }

    listRefunds() {
        return axios.get<{ data: RefundListItem[] }>(`${this.apiUrl}/SuperAdmin/Refunds`)
    }

    processDayPassRefund(id: string) {
        return axios.post(`${this.apiUrl}/SuperAdmin/Refunds/DayPass/${id}/Process`)
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

    getFeeSchedule(tenantId: string) {
        return axios.get<{ data: FeeScheduleWithTiers }>(`${this.apiUrl}/SuperAdmin/Tenants/${tenantId}/FeeSchedule`)
    }

    updateFeeSchedule(tenantId: string, body: { monthlyCapCents: number | null; tiers: FeeTierInput[] }) {
        return axios.put<{ data: FeeScheduleWithTiers }>(`${this.apiUrl}/SuperAdmin/Tenants/${tenantId}/FeeSchedule`, body)
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

    listAuditLog(params: { action?: string; actorUserId?: string; targetKind?: string; targetId?: string; tenantId?: string; fromUtc?: string; toUtc?: string; take?: number } = {}) {
        return axios.get<{ data: AuditLogEntry[] }>(`${this.apiUrl}/SuperAdmin/AuditLog`, { params })
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

export interface FeeTier {
    id: string
    scheduleId: string
    minVolumeCents: number
    maxVolumeCents: number | null
    rateBps: number
    sortOrder: number
}

export interface FeeScheduleWithTiers {
    schedule: {
        id: string
        tenantId: string
        effectiveFromUtc: string
        effectiveToUtc: string | null
        monthlyCapCents: number | null
        createdAt: string
    }
    tiers: FeeTier[]
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

export interface FeeTierInput {
    minVolumeCents: number
    maxVolumeCents: number | null
    rateBps: number
}

export interface DisputeListItem {
    id: string
    tenantId: string
    tenantSubdomain: string
    kind: 'day_pass' | 'event_ticket' | 'unlinked'
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
    kind: 'day_pass' | 'event_ticket'
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
