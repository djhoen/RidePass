import axios from 'axios'

export interface ActiveMembership {
    id: string
    name: string
    durationKind: 'one_time' | 'yearly'
    validFromUtc: string
    validToUtc: string | null
    amountCents: number
}

export interface MembershipHistoryItem {
    id: string
    name: string
    durationKind: 'one_time' | 'yearly'
    validFromUtc: string
    validToUtc: string | null
    amountCents: number
    status: string
    createdAtUtc: string
}

export interface MembershipStatus {
    enabled: boolean
    name: string
    priceCents: number
    durationKind: 'one_time' | 'yearly'
    requiredForRiders: boolean
    requiredForSpectators: boolean
    active: ActiveMembership | null
    history: MembershipHistoryItem[]
}

export interface BuyMembershipResponse {
    purchaseId: string
    clientSecret: string
    amountCents: number
    riderServiceChargeCents: number
}

export interface UpdateMembershipSettingsRequest {
    enabled: boolean
    name: string
    priceCents: number
    durationKind: 'one_time' | 'yearly'
    requiredForRiders: boolean
    requiredForSpectators: boolean
}

export class MembershipService {
    private apiUrl: string
    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    getStatus() {
        return axios.get<{ data: MembershipStatus }>(`${this.apiUrl}/Membership/Status`)
    }
    buy() {
        return axios.post<{ data: BuyMembershipResponse }>(`${this.apiUrl}/Membership/Buy`)
    }
    updateSettings(req: UpdateMembershipSettingsRequest) {
        return axios.put(`${this.apiUrl}/Membership/Settings`, req)
    }
    listForAdmin() {
        return axios.get<{ data: MembershipHistoryItem[] }>(`${this.apiUrl}/Membership/Admin`)
    }
}
