import axios from 'axios'

export interface TicketTier {
    id: string
    eventId: string
    name: string
    priceCents: number
    inventory: number | null
    sold: number | null
    sortOrder: number
    isActive: boolean
}

export interface TicketPurchaseResponse {
    purchaseId: string
    redemptionToken: string
    clientSecret: string
    amountCents: number
}

export interface MyPurchase {
    kind: 'day_pass' | 'event_ticket'
    id: string
    itemName: string
    eventId: string | null
    eventStartsAtUtc: string | null
    validOnDate: string | null
    amountCents: number
    status: string
    redemptionToken: string
    createdAtUtc: string
}

export interface RedemptionPreview {
    kind: 'day_pass' | 'event_ticket'
    purchaseId: string
    redemptionToken: string
    purchaserName: string
    purchaserEmail: string
    itemName: string
    amountCents: number
    status: string
    validOnDate: string | null
    eventTitle: string | null
    tierName: string | null
    eventDescription: string | null
    eventLocationLabel: string | null
    eventStartsAtUtc: string | null
    eventEndsAtUtc: string | null
    eventAllDay: boolean
    createdAtUtc: string
    isRedeemableToday: boolean
    notRedeemableReason: string | null
}

export class TicketService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    // Tiers (admin)
    listTiersForAdmin(eventId: string) {
        return axios.get<{ data: TicketTier[] }>(`${this.apiUrl}/Event/${eventId}/Tiers/Admin`)
    }
    listActiveTiers(eventId: string) {
        return axios.get<{ data: TicketTier[] }>(`${this.apiUrl}/Event/${eventId}/Tiers`)
    }
    createTier(eventId: string, body: Omit<TicketTier, 'id' | 'eventId' | 'sold'>) {
        return axios.post(`${this.apiUrl}/Event/${eventId}/Tiers`, body)
    }
    updateTier(eventId: string, tierId: string, body: Omit<TicketTier, 'id' | 'eventId' | 'sold'>) {
        return axios.put(`${this.apiUrl}/Event/${eventId}/Tiers/${tierId}`, body)
    }
    deleteTier(eventId: string, tierId: string) {
        return axios.delete(`${this.apiUrl}/Event/${eventId}/Tiers/${tierId}`)
    }

    // Ticket purchase (email + name required when not authenticated; ignored otherwise)
    createTicketPurchase(req: { tierId: string; email?: string | null; name?: string | null }) {
        return axios.post<{ data: TicketPurchaseResponse }>(`${this.apiUrl}/Purchase/EventTicket`, req)
    }

    // My Purchases
    getMyPurchases() {
        return axios.get<{ data: MyPurchase[] }>(`${this.apiUrl}/Me/Purchases`)
    }

    // Redemption (admin)
    preview(token: string) {
        return axios.get<{ data: RedemptionPreview }>(`${this.apiUrl}/Redemption/Preview/${token}`)
    }
    redeem(token: string) {
        return axios.post<{ data: RedemptionPreview }>(`${this.apiUrl}/Redemption/Redeem/${token}`)
    }
}
