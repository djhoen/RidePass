import axios from 'axios'

export interface CounterRider {
    id: string
    email: string
    firstName: string
    lastName: string
    hasSignedCurrentWaiver?: boolean
    waiverSignedAtUtc?: string | null
}

export interface CounterCartItem {
    kind: 'day_pass' | 'event_ticket'
    itemId: string
    quantity: number
}

export interface CounterSaleLineItem {
    kind: 'day_pass' | 'event_ticket'
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

    createRider(body: { email: string; firstName: string; lastName: string }) {
        return axios.post<{ data: CounterRider }>(`${this.apiUrl}/Counter/Riders`, body)
    }

    createSale(body: { riderId: string; items: CounterCartItem[]; signWaiver: boolean }) {
        return axios.post<{ data: CounterSaleResponse }>(`${this.apiUrl}/Counter/Sale`, body)
    }
}
