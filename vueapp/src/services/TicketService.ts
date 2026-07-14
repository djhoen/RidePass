import axios from 'axios'

export interface TicketTier {
    id: string
    eventId: string
    kind: 'race_entry' | 'gate_fee'
    // gate_fee picks an audience; race_entry is always 'rider'.
    audience: 'rider' | 'spectator'
    // gate_fee only: required purchase for that audience (race class + one rider gate fee).
    required: boolean
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
    // Dynamic pricing (price steps). Admin step config:
    ladderGroup: string | null
    minSold: number | null
    effectiveDaysBefore: number | null
    effectiveAtUtc: string | null
    // Public buy-page messaging for a ladder's active step (null for standalone tiers):
    remainingToCapacity?: number | null
    nextPriceCents?: number | null
    nextChangeKind?: 'sold' | 'date' | null
    nextChangeSoldThreshold?: number | null
    nextChangeAtUtc?: string | null
}

export interface TicketPurchaseResponse {
    purchaseId: string
    redemptionToken: string
    tickets: TicketRedemption[]
    clientSecret: string
    amountCents: number
    riderServiceChargeCents: number
    taxCents: number
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
    tierKind: 'race_entry' | 'gate_fee' | 'spectator_pass' | null
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
    registrationComplete: boolean
    raceNumber: string | null
}

// One incomplete ticket in an order, from the resume endpoint. The frontend groups
// these into riders (a rider gate fee + the race classes assigned to it) and spectators.
export interface RegistrationTicket {
    ticketId: string
    tierName: string
    kind: 'race_entry' | 'gate_fee'
    audience: 'rider' | 'spectator'
    isRace: boolean
    isRiderGate: boolean
    isSpectatorGate: boolean
    needsWaiver: boolean
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

    // Tell the server a PaymentIntent succeeded so it finalizes now instead of waiting for
    // the async webhook. The server re-verifies with Stripe; safe to call best-effort.
    confirmIntent(paymentIntentId: string) {
        return axios.post(`${this.apiUrl}/Payment/ConfirmIntent`, { paymentIntentId })
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
        // Unified checkout: take payment first, collect waiver + rider details after
        // (via completeRegistration). Lets guests buy entries for non-account riders.
        deferRegistration?: boolean
    }) {
        return axios.post<{ data: TicketPurchaseResponse }>(`${this.apiUrl}/Purchase/EventTicket`, req)
    }

    // Resume page (from the reminder email): the still-incomplete tickets in an order,
    // looked up by any ticket's redemption token. Returns one row per ticket with enough
    // to group them into riders (gate fee + assigned race classes) vs spectators.
    getRegistration(token: string) {
        return axios.get<{ data: { eventTitle: string | null; tickets: RegistrationTicket[] } }>(
            `${this.apiUrl}/Purchase/EventTicket/Registration/${token}`)
    }

    // Post-payment registration for the unified checkout. One entry per REGISTRANT (a
    // person): their identity + signed waiver (when the event requires one for that
    // audience), plus the tickets they cover — their rider gate fee + assigned race
    // classes (one rider may hold several), or a single spectator gate fee.
    completeRegistration(req: {
        registrants: Array<{
            firstName: string
            lastName: string
            birthdate?: string | null
            bike?: string | null
            parentGuardianName?: string | null
            emergencyContactName?: string | null
            emergencyContactPhone?: string | null
            waiverSignatureDataUrl?: string | null
            tickets: Array<{ ticketId: string; raceNumber?: string | null }>
        }>
    }) {
        return axios.post<{ data: { completed: number } }>(
            `${this.apiUrl}/Purchase/EventTicket/CompleteRegistration`, req)
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

    // Bulk-redeem: submit a list of {kind, purchaseId} entries from the order. The server
    // validates each belongs to the scanned token's event+purchaser set before redeeming.
    // idVerified is the gate worker's ID-check attestation (required when the tenant has
    // require-ID-at-check-in on).
    redeemBulk(req: { orderToken: string; items: { kind: string; purchaseId: string }[]; idVerified?: boolean }) {
        return axios.post<{ data: BulkRedeemResponse }>(`${this.apiUrl}/Redemption/Order/Redeem`, req)
    }

    // The signature image behind one ticket in the scanned order. Fetched on demand: a drawn
    // signature is up to ~1MB of base64, too much to ship for every attendee up front.
    orderSignature(token: string, purchaseId: string) {
        return axios.get<{ data: OrderSignature }>(
            `${this.apiUrl}/Redemption/Order/${token}/Signature/${purchaseId}`)
    }

    // Gate lookup for the rider with no QR: search today's orders by buyer name, buyer email, or
    // rider name. Server requires 3+ characters and only sees events open for check-in today.
    gateSearch(q: string) {
        return axios.get<{ data: GateSearchResult[] }>(
            `${this.apiUrl}/Redemption/Search`, { params: { q } })
    }
}

export interface GateSearchResult {
    eventId: string
    eventTitle: string
    eventStartsAtUtc: string
    purchaserName: string
    purchaserEmail: string
    anchorToken: string
    itemCount: number
    redeemedCount: number
    riderNames: string | null
}

export interface OrderLookup {
    stripePaymentIntentId: string | null
    purchaserName: string
    purchaserEmail: string
    items: OrderItem[]
    requireIdAtCheckin: boolean
    totalAmountCents: number
    // One entry per attending person (tickets grouped by registrant), with waiver status.
    waivers: OrderWaiverAttendee[]
    waiverRequiredCount: number
    waiverSignedCount: number
    waiverMissingCount: number
}

// A person on the order and where they stand on the event's waiver requirement.
export interface OrderWaiverAttendee {
    attendeeKey: string
    purchaseIds: string[]
    name: string | null
    audience: 'rider' | 'spectator' | string
    birthdate: string | null
    age: number | null
    isMinor: boolean
    items: string[]
    registrationComplete: boolean
    waiverRequired: boolean
    waiverSigned: boolean
    waiverName: string | null
    signedAtUtc: string | null
    signedByParent: boolean
    guardianName: string | null
    signerName: string | null
    signerEmail: string | null
    hasSignatureImage: boolean
    signaturePurchaseId: string | null
    blockReason: string | null
}

export interface OrderSignature {
    purchaseId: string
    attendeeName: string | null
    waiverName: string | null
    waiverTitle: string | null
    signedAtUtc: string | null
    signedByParent: boolean
    guardianName: string | null
    signerName: string | null
    signerEmail: string | null
    signatureDataUrl: string | null
}

export interface OrderItem {
    kind: 'pass' | 'event_ticket' | 'extras' | 'membership' | string
    // Event tickets only: what the admission is, so a spectator's gate fee isn't labelled
    // "Race Entry" at the gate. Null on add-ons.
    ticketKind: 'race_entry' | 'gate_fee' | 'spectator_pass' | string | null
    audience: 'rider' | 'spectator' | string | null
    purchaseId: string
    redemptionToken: string
    itemName: string
    quantity: number
    variantLabel: string | null
    amountCents: number
    status: string
    isRedeemableToday: boolean
    notRedeemableReason: string | null
    redeemedAtUtc: string | null
    redeemedByName: string | null
    registrationComplete: boolean
    attendeeName: string | null
    signedByParent: boolean
    guardianName: string | null
}

export interface BulkRedeemResponse {
    redeemedCount: number
    errors: string[]
}
