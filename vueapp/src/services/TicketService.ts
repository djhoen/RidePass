import axios from 'axios'

export interface TicketTier {
    id: string
    eventId: string
    kind: 'spectator_pass' | 'race_entry'
    name: string
    priceCents: number
    inventory: number | null
    sold: number | null
    sortOrder: number
    isActive: boolean
    riderPaidServiceChargeBps: number
    bundledCouponCount: number | null
    bundledCouponDiscountKind: 'percent' | 'amount' | null
    bundledCouponDiscountValue: number | null
    bundledCouponScope: 'all' | 'pass' | 'event_ticket' | 'season_pass' | null
    bundledCouponExpiresInDays: number | null
}

export interface TicketPurchaseResponse {
    purchaseId: string
    redemptionToken: string
    tickets: TicketRedemption[]
    clientSecret: string
    amountCents: number
    riderServiceChargeCents: number
    giftCardAppliedCents: number
}

export interface TicketRedemption {
    purchaseId: string
    redemptionToken: string
    tierName: string
    amountCents: number
}

export interface MyPurchase {
    kind: 'pass' | 'event_ticket'
    id: string
    itemName: string
    eventId: string | null
    eventStartsAtUtc: string | null
    validOnDate: string | null
    amountCents: number
    status: string
    redemptionToken: string
    createdAtUtc: string
    tierKind: 'race_entry' | 'spectator_pass' | null
}

export interface MyCoupon {
    id: string
    code: string
    description: string | null
    discountKind: 'percent' | 'amount'
    discountValue: number
    applicableScope: 'all' | 'pass' | 'event_ticket' | 'season_pass'
    validToUtc: string | null
    issuedFromPurchaseId: string | null
    isActive: boolean
    redeemedCount: number
    maxTotalUses: number | null
    shareCount: number
    lastSharedAtUtc: string | null
    lastSharedToEmail: string | null
}

export interface RedemptionPreview {
    kind: 'pass' | 'event_ticket'
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
    reorderTiers(eventId: string, items: { id: string; sortOrder: number }[]) {
        return axios.post(`${this.apiUrl}/Event/${eventId}/Tiers/Reorder`, { items })
    }

    // Ticket purchase — cart of {tierId, quantity}. Email + name required when not authenticated.
    createTicketPurchase(req: {
        items: { tierId: string; quantity: number }[]
        email?: string | null
        name?: string | null
        rewardRedemptionId?: string | null
        couponCode?: string | null
        giftCardCode?: string | null
        extras?: Array<{ productId: string; quantity: number; variantId?: string | null }> | null
        // Bundles a track-membership purchase into the same PI when the rider opts in
        // from the membership-required dialog instead of being kicked out to /Membership.
        addMembership?: boolean
    }) {
        return axios.post<{ data: TicketPurchaseResponse }>(`${this.apiUrl}/Purchase/EventTicket`, req)
    }

    // Redeem one Loam Pass credit to cover a single rider-entry (race_entry) tier instead of paying.
    redeemLoampassForTicket(tierId: string) {
        return axios.post<{ data: TicketPurchaseResponse }>(`${this.apiUrl}/Purchase/EventTicket/RedeemLoampass`, { tierId })
    }

    // My Purchases
    getMyPurchases() {
        return axios.get<{ data: MyPurchase[] }>(`${this.apiUrl}/Me/Purchases`)
    }

    // Coupons issued to me as part of a race-entry bundle
    getMyCoupons(ticketPurchaseId?: string) {
        return axios.get<{ data: MyCoupon[] }>(`${this.apiUrl}/Me/Coupons`, {
            params: ticketPurchaseId ? { ticketPurchaseId } : undefined,
        })
    }

    shareCoupon(couponId: string, body: { recipientEmail: string; recipientName?: string | null; personalNote?: string | null }) {
        return axios.post(`${this.apiUrl}/Me/Coupons/${couponId}/Share`, body)
    }

    cancelMyPass(id: string, reason: string | null) {
        return axios.post<{ data: { id: string; status: string; refundCents?: number } }>(
            `${this.apiUrl}/Me/Purchases/Pass/${id}/Cancel`, { reason })
    }
    cancelMyTicket(id: string, reason: string | null) {
        return axios.post<{ data: { id: string; status: string; refundCents?: number } }>(
            `${this.apiUrl}/Me/Purchases/Ticket/${id}/Cancel`, { reason })
    }

    // Redemption (admin)
    preview(token: string) {
        return axios.get<{ data: RedemptionPreview }>(`${this.apiUrl}/Redemption/Preview/${token}`)
    }
    redeem(token: string) {
        return axios.post<{ data: RedemptionPreview }>(`${this.apiUrl}/Redemption/Redeem/${token}`)
    }

    // Order-scope: fetch every purchase row tied to the same payment as the
    // scanned token so staff can pick which items to redeem now.
    orderLookup(token: string) {
        return axios.get<{ data: OrderLookup }>(`${this.apiUrl}/Redemption/Order/${token}`)
    }

    // Bulk-redeem: submit a list of {kind, purchaseId} entries from the order.
    // The server validates each belongs to the same order before redeeming.
    redeemBulk(req: { orderToken: string; items: { kind: string; purchaseId: string }[] }) {
        return axios.post<{ data: BulkRedeemResponse }>(`${this.apiUrl}/Redemption/Order/Redeem`, req)
    }
}

export interface OrderLookup {
    stripePaymentIntentId: string | null
    purchaserName: string
    purchaserEmail: string
    items: OrderItem[]
}

export interface OrderItem {
    kind: 'pass' | 'event_ticket' | 'extras' | 'membership' | string
    purchaseId: string
    redemptionToken: string
    itemName: string
    amountCents: number
    status: string
    isRedeemableToday: boolean
    notRedeemableReason: string | null
    redeemedAtUtc: string | null
    redeemedByName: string | null
}

export interface BulkRedeemResponse {
    redeemedCount: number
    errors: string[]
}
