import axios from 'axios'

export interface CounterRider {
    id: string
    email: string
    firstName: string
    lastName: string
    hasSignedCurrentWaiver?: boolean
    waiverSignedAtUtc?: string | null
    waiverSignatureDataUrl?: string | null
    isMinor?: boolean
    waiverSignedByParent?: boolean
    waiverParentName?: string | null
    waiverParentPhone?: string | null
    emergencyContactName?: string | null
    emergencyContactPhone?: string | null
}

export interface CounterCartItem {
    // Mirrors CounterCartItem on the server. There is no 'pass' kind: a gate fee or day pass is
    // sold as an event_ticket tier.
    kind: 'event_ticket' | 'extras' | 'membership' | 'rental' | 'season_pass'
    // Per-kind: event_ticket -> tier id, extras -> product id, rental -> shop variant id,
    // membership -> ignored, season_pass -> SeasonPassProduct id.
    itemId: string
    quantity: number
    // Required when kind === 'extras' (anchors the add-on to a specific event) and when
    // kind === 'rental' (the lesson the bike is booked for).
    eventId?: string | null
    // Required when kind === 'extras' AND the product has any active variants.
    variantId?: string | null
}

export interface CounterSaleLineItem {
    kind: 'pass' | 'event_ticket' | 'extras' | 'membership' | 'shop_rental'
    purchaseId: string
    redemptionToken: string
    displayName: string
    quantity: number
    unitPriceCents: number
    lineAmountCents: number
}

export interface CounterSaleResponse {
    clientSecret: string
    totalAmountCents: number
    creditAppliedCents?: number
    dueCents?: number
    lineItems: CounterSaleLineItem[]
}

export class CounterService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    findRider(email: string) {
        return axios.post<{ data: CounterRider }>(`${this.apiUrl}/Counter/Riders/Find`, { email })
    }

    createRider(body: { email: string; firstName: string; lastName: string; birthdate: string; emergencyContactName?: string; emergencyContactPhone?: string }) {
        return axios.post<{ data: CounterRider }>(`${this.apiUrl}/Counter/Riders`, body)
    }

    createSale(body: { riderId: string; items: CounterCartItem[]; signWaiver: boolean; signatureDataUrl?: string | null; parentName?: string | null; parentPhone?: string | null; discountPresetId?: string | null; managerPin?: string | null; paymentMethod?: 'stripe' | 'cash' | null; creditAccountId?: string | null; creditCents?: number }) {
        return axios.post<{ data: CounterSaleResponse }>(`${this.apiUrl}/Counter/Sale`, body)
    }
}
