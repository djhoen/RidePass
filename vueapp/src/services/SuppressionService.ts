import axios from 'axios'

export interface SuppressionItem {
    id: string
    email: string
    reason: string   // bounce | complaint | unsubscribe | manual
    scope: string    // all | marketing
    source: string | null
    detail: string | null
    createdAtUtc: string
}

export interface MarketingUnsubStatus {
    email: string
    tenantDisplayName: string
    unsubscribed: boolean
}

export class SuppressionService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    // ── Tenant admin ──────────────────────────────────────────────
    list() {
        return axios.get<{ data: SuppressionItem[] }>(`${this.apiUrl}/Suppression`)
    }

    add(email: string, note: string | null) {
        return axios.post(`${this.apiUrl}/Suppression`, { email, note })
    }

    remove(id: string) {
        return axios.delete(`${this.apiUrl}/Suppression/${id}`)
    }

    // ── Public one-click unsubscribe (suppression-based flow) ──────
    // Token carries the tenant + recipient; no auth.
    status(token: string) {
        return axios.get<{ data: MarketingUnsubStatus }>(`${this.apiUrl}/Unsubscribe/Status`, { params: { token } })
    }

    unsubscribe(token: string) {
        return axios.post(`${this.apiUrl}/Unsubscribe`, null, { params: { token } })
    }

    unsubscribeAllTracks(token: string) {
        return axios.post(`${this.apiUrl}/Unsubscribe/AllTracks`, null, { params: { token } })
    }

    resubscribe(token: string) {
        return axios.post(`${this.apiUrl}/Unsubscribe/Resubscribe`, null, { params: { token } })
    }
}
