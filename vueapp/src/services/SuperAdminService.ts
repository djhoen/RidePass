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
