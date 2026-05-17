import axios from 'axios'

export interface BuyGiftCardRequest {
    amountCents: number
    recipientName: string
    recipientEmail: string
    personalNote?: string | null
    scheduledDeliveryAtUtc?: string | null
}

export interface BuyGiftCardResponse {
    giftCardId: string
    clientSecret: string
    amountCents: number
}

export class GiftCardService {
    private apiUrl: string
    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    buy(req: BuyGiftCardRequest) {
        return axios.post<{ data: BuyGiftCardResponse }>(`${this.apiUrl}/Purchase/GiftCard`, req)
    }
}
