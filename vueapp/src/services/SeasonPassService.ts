import axios from 'axios'

export interface SeasonPassPerk {
    eventTypeId: string
    discountPercent: number
}

export interface SeasonPassProduct {
    id: string
    name: string
    description: string | null
    priceCents: number
    validFromDate: string
    validToDate: string
    kind: 'unlimited' | 'days_of_week' | 'credits'
    validDaysOfWeek: number[] | null
    totalCredits: number | null
    requiresWaiver: boolean
    riderPaidServiceChargeBps: number
    isActive: boolean
    sortOrder: number
    perks: SeasonPassPerk[]
}

export interface UpsertSeasonPassProduct {
    name: string
    description: string | null
    priceCents: number
    validFromDate: string
    validToDate: string
    kind: 'unlimited' | 'days_of_week' | 'credits'
    validDaysOfWeek: number[] | null
    totalCredits: number | null
    requiresWaiver: boolean
    riderPaidServiceChargeBps: number
    isActive: boolean
    sortOrder: number
    perks: SeasonPassPerk[]
}

export interface BuySeasonPassResponse {
    purchaseId: string
    redemptionToken: string
    clientSecret: string
    amountCents: number
    riderServiceChargeCents: number
    giftCardAppliedCents: number
}

export interface MySeasonPass {
    id: string
    redemptionToken: string
    productName: string
    productKind: 'unlimited' | 'days_of_week' | 'credits'
    creditsRemaining: number | null
    validDaysOfWeek: number[] | null
    validFromDate: string
    validToDate: string
    status: string
    createdAtUtc: string
}

export interface PassReservation {
    id: string
    eventId: string
    eventTitle: string
    eventStartsAtUtc: string
    eventEndsAtUtc: string
    status: string
    checkedInAtUtc: string | null
}

export interface PassLookup {
    id: string
    purchaserName: string
    purchaserEmail: string
    status: string
    validFromDate: string
    validToDate: string
    creditsRemaining: number | null
    photoDataUrl: string | null
    productName: string
    productKind: string
    validDaysOfWeek: number[] | null
    todaysReservations: PassReservation[]
}

export class SeasonPassService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    listActive() {
        return axios.get<{ data: SeasonPassProduct[] }>(`${this.apiUrl}/SeasonPass/Products`)
    }
    listForAdmin() {
        return axios.get<{ data: SeasonPassProduct[] }>(`${this.apiUrl}/SeasonPass/Products/Admin`)
    }
    create(req: UpsertSeasonPassProduct) {
        return axios.post<{ data: SeasonPassProduct }>(`${this.apiUrl}/SeasonPass/Products`, req)
    }
    update(id: string, req: UpsertSeasonPassProduct) {
        return axios.put<{ data: SeasonPassProduct }>(`${this.apiUrl}/SeasonPass/Products/${id}`, req)
    }
    deleteProduct(id: string) {
        return axios.delete(`${this.apiUrl}/SeasonPass/Products/${id}`)
    }
    reorderProducts(items: { id: string; sortOrder: number }[]) {
        return axios.post(`${this.apiUrl}/SeasonPass/Products/Reorder`, { items })
    }
    buy(productId: string, photoDataUrl: string, couponCode: string | null = null, giftCardCode: string | null = null) {
        return axios.post<{ data: BuySeasonPassResponse }>(`${this.apiUrl}/SeasonPass/Buy`,
            { productId, photoDataUrl, couponCode, giftCardCode })
    }
    listMine() {
        return axios.get<{ data: MySeasonPass[] }>(`${this.apiUrl}/SeasonPass/Mine`)
    }
    reserve(passPurchaseId: string, eventId: string) {
        return axios.post(`${this.apiUrl}/SeasonPass/Reserve`, { passPurchaseId, eventId })
    }
    lookupByToken(token: string) {
        return axios.get<{ data: PassLookup }>(`${this.apiUrl}/SeasonPass/Pass/${token}`)
    }
    checkIn(reservationId: string) {
        return axios.post(`${this.apiUrl}/SeasonPass/Reservations/${reservationId}/CheckIn`)
    }
}
