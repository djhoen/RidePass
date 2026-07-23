import axios from 'axios'

export interface SeasonPassPerk {
    eventTypeId: string
    discountPercent: number
}

export type SeasonPassBenefitType = 'event' | 'concession' | 'rental' | 'retail' | 'buddy_pass'

/** Something a pass grants its holder. Supersedes SeasonPassPerk. */
export interface SeasonPassBenefit {
    benefitType: SeasonPassBenefitType
    /** Event type id for 'event'; null = the whole surface. */
    scopeId: string | null
    discountKind: 'percent' | 'amount'
    /** Basis points when percent (10000 = 100% = included free), cents when amount. */
    discountValue: number
    /** Uses per season; null = unlimited. */
    quantity: number | null
    /** Display name of what scopeId points at. Response-only. */
    scopeName?: string | null
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
    /** Landing-page fields (null slug = no landing page). Body is Tiptap HTML. */
    slug: string | null
    heroImageUrl: string | null
    landingHtml: string | null
    landingPublished: boolean
    /** @deprecated Legacy event-only shape. Read `benefits` instead; still emitted for older clients. */
    perks: SeasonPassPerk[]
    benefits: SeasonPassBenefit[]
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
    slug: string | null
    heroImageUrl: string | null
    landingHtml: string | null
    landingPublished: boolean
    benefits: SeasonPassBenefit[]
}

/** A pass product's landing page: authored content + live product facts. */
export interface SeasonPassLanding {
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
    slug: string | null
    heroImageUrl: string | null
    landingHtml: string | null
    landingPublished: boolean
    benefits: SeasonPassBenefit[]
}

export interface SeasonPassCartItem {
    productId: string
    quantity: number
}

/** One pass created by checkout, to be registered to a holder after payment. */
export interface SeasonPassPurchaseItem {
    purchaseId: string
    redemptionToken: string
    productId: string
    productName: string
    requiresWaiver: boolean
}

export interface BuySeasonPassResponse {
    passes: SeasonPassPurchaseItem[]
    clientSecret: string
    amountCents: number
    riderServiceChargeCents: number
    giftCardAppliedCents: number
}

/** Holder details for one pass, collected after payment. */
export interface SeasonPassRegistrationItem {
    purchaseId: string
    firstName: string
    lastName: string
    birthdate?: string | null
    photoDataUrl: string
    waiverSignatureDataUrl?: string | null
    parentGuardianName?: string | null
    parentGuardianPhone?: string | null
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
    requiresWaiver: boolean
    /** False while the pass is paid but has no holder/photo/waiver — the gate refuses it until fixed. */
    registrationComplete: boolean
    holderFirstName: string | null
    holderLastName: string | null
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

export interface PassTodayEvent {
    id: string
    title: string
    startsAtUtc: string
    endsAtUtc: string
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
    /** Who the pass admits — may differ from the buyer. Null until registration finishes. */
    holderName: string | null
    /** False while the pass is paid but has no holder/photo/waiver yet; check-in is refused. */
    registrationComplete: boolean
    productName: string
    productKind: string
    productTotalCredits: number | null
    validDaysOfWeek: number[] | null
    /** Scheduled events running today (tenant tz) — walk-up redemption targets. */
    todaysEvents: PassTodayEvent[]
    todaysReservations: PassReservation[]
}

export interface GateRedeemResult {
    reservationId: string
    alreadyAdmitted: boolean
    checkedInAtUtc: string | null
    creditsRemaining: number | null
}

export class SeasonPassService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    listActive() {
        return axios.get<{ data: SeasonPassProduct[] }>(`${this.apiUrl}/SeasonPass/Products`)
    }
    /** Landing page by slug (public URL) or product id (embed widget). */
    getLanding(slugOrId: string) {
        return axios.get<{ data: SeasonPassLanding }>(`${this.apiUrl}/SeasonPass/Landing/${encodeURIComponent(slugOrId)}`)
    }
    /** Landing hero / inline-body image upload; returns the stored image URL. */
    uploadImage(file: File) {
        const form = new FormData()
        form.append('file', file)
        return axios.post<{ data: { imageUrl: string } }>(`${this.apiUrl}/SeasonPass/Products/Image`, form)
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
    buy(items: SeasonPassCartItem[], couponCode: string | null = null, giftCardCode: string | null = null) {
        return axios.post<{ data: BuySeasonPassResponse }>(`${this.apiUrl}/SeasonPass/Buy`,
            { items, couponCode, giftCardCode })
    }
    completeRegistration(passes: SeasonPassRegistrationItem[]) {
        return axios.post(`${this.apiUrl}/SeasonPass/CompleteRegistration`, { passes })
    }
    confirmIntent(paymentIntentId: string) {
        return axios.post(`${this.apiUrl}/Payment/ConfirmIntent`, { paymentIntentId })
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
    /** Walk-up gate admission for a scanned pass: burns one credit on credits passes. */
    redeemAtGate(token: string, eventId: string) {
        return axios.post<{ data: GateRedeemResult }>(`${this.apiUrl}/SeasonPass/Pass/${token}/Redeem`, { eventId })
    }
    /** Admin support override of a credits pass's remaining rides (audit-logged, reason required). */
    adjustCredits(passPurchaseId: string, creditsRemaining: number, reason: string) {
        return axios.put(`${this.apiUrl}/SeasonPass/Admin/Purchases/${passPurchaseId}/Credits`,
            { creditsRemaining, reason })
    }
}
