import axios from 'axios'

export type UpcomingKind = 'event_ticket' | 'pass' | 'season_pass' | 'membership'

export interface UpcomingItem {
    kind: UpcomingKind
    id: string
    tenantId: string
    tenantSubdomain: string
    tenantDisplayName: string
    itemName: string
    imageUrl: string | null
    tenantLogoUrl: string | null
    registrationComplete: boolean
    waiverSigned: boolean
    occursAtUtc: string | null
    // event_ticket: when the event ends. An event stays "upcoming" until the day after this.
    endsAtUtc: string | null
    validToUtc: string | null
    amountCents: number
    redemptionToken: string | null
    createdAtUtc: string
}

export interface EventOrderItem {
    id: string
    tierName: string
    kind: 'race_entry' | 'gate_fee'
    audience: 'rider' | 'spectator'
    status: string
    amountCents: number
    basePriceCents: number
    raceNumber: string | null
    riderName: string | null
    registrationComplete: boolean
    waiverSigned: boolean
    redemptionToken: string
}

export interface EventOrderDetail {
    eventTitle: string | null
    items: EventOrderItem[]
}

export class UpcomingService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    list() {
        return axios.get<{ data: UpcomingItem[] }>(`${this.apiUrl}/Me/Upcoming`)
    }

    // The rider's tickets for one event (entries + gate fees), for the Order Detail dialog.
    eventOrder(eventId: string) {
        return axios.get<{ data: EventOrderDetail }>(`${this.apiUrl}/Me/EventOrder/${eventId}`)
    }

    // Resend the consolidated order confirmation email (items + total + gate QR).
    resendConfirmation(eventId: string) {
        return axios.post<{ data: { email: string } }>(`${this.apiUrl}/Me/EventOrder/${eventId}/ResendConfirmation`)
    }
}
