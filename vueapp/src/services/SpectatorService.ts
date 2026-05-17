import axios from 'axios'

export interface SpectatorBuyItem {
    productId: string
    quantity: number
    variantId?: string | null
}

export interface SpectatorEntry {
    firstName: string
    lastName: string
    birthdate: string                       // ISO YYYY-MM-DD
    signatureDataUrl?: string | null
    parentName?: string | null
    parentPhone?: string | null
}

export interface SpectatorBuyRequest {
    eventId: string
    purchaserEmail: string
    purchaserName: string
    items: SpectatorBuyItem[]
    spectators: SpectatorEntry[]
}

export interface SpectatorBuyResponse {
    purchaseIds: string[]
    clientSecret: string
    amountCents: number
}

export class SpectatorService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    checkSignature(waiverId: string, email: string) {
        return axios.get<{ data: { hasSigned: boolean } }>(
            `${this.apiUrl}/Spectator/Waiver/${waiverId}/Check`,
            { params: { email } })
    }

    buy(req: SpectatorBuyRequest) {
        return axios.post<{ data: SpectatorBuyResponse }>(`${this.apiUrl}/Spectator/Buy`, req)
    }
}
