import axios from 'axios'

export interface JoinWaitlistRequest {
    eventId: string
    tierId?: string | null
    prepay: boolean
    notes?: string | null
}

export interface JoinWaitlistResponse {
    waitlistId: string
    position: number
    isPrepaid: boolean
    clientSecret: string | null
    prepayAmountCents: number
    notifyPhone: string | null
}

export interface MyWaitlistEntry {
    id: string
    eventId: string
    eventTitle: string
    eventStartsAtUtc: string
    tierId: string | null
    tierName: string | null
    position: number
    aheadOfMe: number
    isPrepaid: boolean
    prepayAmountCents: number
    status: 'waiting' | 'promoted' | 'confirmed' | 'expired' | 'cancelled'
    confirmDeadlineUtc: string | null
    confirmToken: string | null
    createdAtUtc: string
}

export class WaitlistService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    join(req: JoinWaitlistRequest) {
        return axios.post<{ data: JoinWaitlistResponse }>(`${this.apiUrl}/Waitlist/Join`, req)
    }
    listMine() {
        return axios.get<{ data: MyWaitlistEntry[] }>(`${this.apiUrl}/Waitlist/Mine`)
    }
    cancel(id: string) {
        return axios.delete(`${this.apiUrl}/Waitlist/${id}`)
    }

    confirmDetails(token: string) {
        return axios.get<{ data: ConfirmDetails }>(`${this.apiUrl}/Waitlist/Confirm/${token}`)
    }

    confirmAndPay(token: string, body: { passProductId?: string | null }) {
        return axios.post<{ data: { clientSecret: string; amountCents: number } }>(
            `${this.apiUrl}/Waitlist/Confirm/${token}/Pay`, body)
    }
}

export interface ConfirmDetails {
    waitlistId: string
    status: 'waiting' | 'promoted' | 'confirmed' | 'expired' | 'cancelled'
    eventId: string
    eventTitle: string
    eventStartsAtUtc: string
    eventLocationLabel: string | null
    tierId: string | null
    tierName: string | null
    tierPriceCents: number | null
    eligiblePasses: { id: string; name: string; priceCents: number; requiresWaiver: boolean }[]
    isPrepaid: boolean
    prepayAmountCents: number
    confirmDeadlineUtc: string | null
    createdPurchaseRedemptionToken: string | null
}
