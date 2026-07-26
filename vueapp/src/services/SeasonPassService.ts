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
    /** Staff-only product: never listed publicly, never purchasable, granted from
     *  Admin > Employee Passes. Only ever true on the admin product list. */
    isEmployee: boolean
    /** Event types the buddy-pass perk is good for. Empty (with a quantity) is invalid. */
    buddyEventTypeIds: string[]
    /** Buddy passes also work on days with no event. Requires the perk to be free. */
    buddyIncludeWalkUp: boolean
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
    /** Staff-only product: never listed publicly, never purchasable, granted from
     *  Admin > Employee Passes. Only ever true on the admin product list. */
    isEmployee: boolean
    /** Event types the buddy-pass perk is good for. Empty (with a quantity) is invalid. */
    buddyEventTypeIds: string[]
    /** Buddy passes also work on days with no event. Requires the perk to be free. */
    buddyIncludeWalkUp: boolean
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
    /** Null on a walk-up admission taken on a day with no calendar event. */
    eventId: string | null
    /** "Walk-up admission" when there was no event. */
    eventTitle: string
    /** Both null on a walk-up admission; render checkInDate instead. */
    eventStartsAtUtc: string | null
    eventEndsAtUtc: string | null
    /** Tenant-local date (YYYY-MM-DD) of a walk-up admission; null on event-anchored rows. */
    checkInDate: string | null
    status: string
    checkedInAtUtc: string | null
}

export interface PassTodayEvent {
    id: string
    title: string
    /** So the buddy flow can offer only events the entitlement is good for. */
    eventTypeId: string
    startsAtUtc: string
    endsAtUtc: string
}

/** A pass holder's guest-admission entitlement, as the counter needs it. */
export interface BuddyEntitlement {
    total: number
    used: number
    remaining: number
    /** True when the perk covers admission outright. Only these redeem today. */
    isFree: boolean
    discountKind: string
    discountValue: number
    /** Human-readable list of what it's good for, for the panel. */
    goodFor: string[]
    /** Event types it covers, for filtering the event picker. */
    eventTypeIds: string[]
    coversWalkUpDays: boolean
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

    // ── The two gate checks the worker has to see before banding someone ──────
    /** Holder has a signed waiver. Computed the same way admission enforces it. */
    waiverSigned: boolean
    /** Why the waiver check fails, when it could be resolved (a single event today). */
    waiverBlockReason: string | null
    idVerified: boolean
    idVerifiedAtUtc: string | null
    idVerifiedByName: string | null
    /** 'rider' = on their account, carries forward. 'credential' = this pass only. 'none'. */
    idVerifiedScope: 'none' | 'rider' | 'credential'
    /** Age from the DOB on the document. Null until verified, never a self-reported age. */
    idVerifiedAge: number | null
    /** Self-reported at registration; the starting point for the verify dialog. */
    holderBirthdate: string | null
    /** Tenant requires waiver + verified ID before a wristband may be issued. */
    requireIdForWristband: boolean
    /** Null when this pass's product grants no buddy admissions (most products). */
    buddyPass: BuddyEntitlement | null
}

export interface VerifyRiderIdResult {
    idVerified: boolean
    idVerifiedAtUtc: string | null
    idVerifiedByName: string | null
    idVerifiedScope: 'none' | 'rider' | 'credential'
    idVerifiedAge: number | null
}

export interface GateRedeemResult {
    reservationId: string
    alreadyAdmitted: boolean
    checkedInAtUtc: string | null
    creditsRemaining: number | null
}

/** One row of the admin Employee Passes roster. */
export interface EmployeePassRosterItem {
    userId: string
    email: string
    name: string | null
    role: string | null
    isActiveEmployee: boolean
    passPurchaseId: string | null
    productName: string | null
    amountCents: number | null
    validFromDate: string | null
    validToDate: string | null
    issuedAtUtc: string | null
    issuedByName: string | null
    /** Server-computed so the page and the gate can't disagree. */
    passState: 'none' | 'pending_payment' | 'not_registered' | 'active' | 'inactive_employee'
}

export interface EmployeePassProductOption {
    id: string
    name: string
    priceCents: number
    validFromDate: string
    validToDate: string
    isActive: boolean
    benefits: SeasonPassBenefit[]
}

export interface EmployeeEventTypeOption {
    id: string
    name: string
    code: string | null
}

export interface EmployeePassRosterResponse {
    rows: EmployeePassRosterItem[]
    products: EmployeePassProductOption[]
    eventTypes: EmployeeEventTypeOption[]
    /** Which surfaces honour a per-pass benefit today, so the UI can't promise one that won't apply. */
    surfaceLive: Record<string, boolean>
}

export interface BuddyRedemptionItem {
    id: string
    holderName: string | null
    buddyName: string | null
    buddyEmail: string | null
    eventTitle: string | null
    redeemedAtUtc: string
    redeemedByName: string | null
    creditReturned: boolean
    creditReturnedAtUtc: string | null
    creditReturnedByName: string | null
    creditReturnReason: string | null
}

export interface UpgradePathItem {
    id: string
    fromProductId: string
    toProductId: string
    fromProductName: string | null
    toProductName: string | null
    priceCents: number
    isActive: boolean
    /** Holders who could take this offer today. */
    eligibleHolders: number
}

export interface UpgradeProductOption {
    id: string
    name: string
    kind: string
    priceCents: number
    isActive: boolean
}

export interface UpgradePathsResponse {
    paths: UpgradePathItem[]
    products: UpgradeProductOption[]
}

/** An upgrade the signed-in rider can take on one of their passes. */
export interface UpgradeOfferItem {
    pathId: string
    passPurchaseId: string
    fromProductName: string
    toProductId: string
    toProductName: string
    toProductDescription: string | null
    toProductKind: string
    toProductTotalCredits: number | null
    toValidFromDate: string
    toValidToDate: string
    priceCents: number
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
    /** Admit a scanned pass. eventId null = walk-up on a day with no calendar event, which the
     *  server accepts only for tenants in walk-up admission mode. */
    redeemAtGate(token: string, eventId: string | null) {
        return axios.post<{ data: GateRedeemResult }>(`${this.apiUrl}/SeasonPass/Pass/${token}/Redeem`, { eventId })
    }
    /**
     * Records that the worker checked this holder's photo ID. `verifiedDob` is the date of birth
     * printed on the document; omitting it falls back to the birthdate given at registration.
     * The result persists, so later scans show the tick without re-carding the rider.
     */
    verifyPassHolderId(token: string, verifiedDob: string | null) {
        return axios.post<{ data: VerifyRiderIdResult }>(
            `${this.apiUrl}/SeasonPass/Pass/${token}/VerifyId`, { verifiedDob })
    }
    /** Admin-only correction: undoes a verification recorded in error, account included. */
    clearPassHolderIdVerification(token: string) {
        return axios.post(`${this.apiUrl}/SeasonPass/Pass/${token}/ClearIdVerification`)
    }
    // ── Upgrades ─────────────────────────────────────────────────────────────
    /** Admin: every offer plus both axes of the matrix. */
    listUpgrades() {
        return axios.get<{ data: UpgradePathsResponse }>(`${this.apiUrl}/SeasonPass/Upgrades`)
    }
    /** Admin: write one cell. Upserts on the product pair. */
    upsertUpgrade(body: { fromProductId: string; toProductId: string; priceCents: number; isActive: boolean }) {
        return axios.put(`${this.apiUrl}/SeasonPass/Upgrades`, body)
    }
    deleteUpgrade(id: string) {
        return axios.delete(`${this.apiUrl}/SeasonPass/Upgrades/${id}`)
    }
    /** Rider: upgrades available on my passes right now. */
    myUpgrades() {
        return axios.get<{ data: UpgradeOfferItem[] }>(`${this.apiUrl}/SeasonPass/MyUpgrades`)
    }
    /** Rider: take one. Price is re-resolved server-side, so only ids are sent. */
    buyUpgrade(passPurchaseId: string, pathId: string) {
        return axios.post<{ data: { passPurchaseId: string; redemptionToken: string; amountCents: number; clientSecret: string | null } }>(
            `${this.apiUrl}/SeasonPass/Upgrade`, { passPurchaseId, pathId })
    }

    // ── Buddy passes ─────────────────────────────────────────────────────────
    /** Spend one of the holder's guest admissions. The holder must be present; scanning their
     *  pass to get passPurchaseId is what establishes that. */
    redeemBuddyPass(passPurchaseId: string, buddyUserId: string, tierId: string) {
        return axios.post<{ data: { redemptionId: string; ticketPurchaseId: string; remaining: number } }>(
            `${this.apiUrl}/SeasonPass/Buddy/Redeem`, { passPurchaseId, buddyUserId, tierId })
    }
    /** Buddy usage for the admin report. Returned credits are included and flagged. */
    buddyUsage() {
        return axios.get<{ data: BuddyRedemptionItem[] }>(`${this.apiUrl}/SeasonPass/Buddy/Usage`)
    }
    /** Hand a spent credit back to the holder. Entitlement only: no money, admission untouched. */
    returnBuddyCredit(redemptionId: string, reason: string) {
        return axios.post(`${this.apiUrl}/SeasonPass/Buddy/${redemptionId}/ReturnCredit`, { reason })
    }

    /** Admin support override of a credits pass's remaining rides (audit-logged, reason required). */
    adjustCredits(passPurchaseId: string, creditsRemaining: number, reason: string) {
        return axios.put(`${this.apiUrl}/SeasonPass/Admin/Purchases/${passPurchaseId}/Credits`,
            { creditsRemaining, reason })
    }

    // ── Employee passes ──────────────────────────────────────────────────────
    /** Staff roster with the employee pass each holds, plus the products available to issue. */
    employeePassRoster() {
        return axios.get<{ data: EmployeePassRosterResponse }>(`${this.apiUrl}/SeasonPass/Employee/Roster`)
    }
    /** Approve and issue an employee pass. Never automatic: this call IS the approval. */
    issueEmployeePass(userId: string, productId: string) {
        return axios.post(`${this.apiUrl}/SeasonPass/Employee/Issue`, { userId, productId })
    }
    /** Replace what an employee pass grants. Touches only benefit rows, so it can't blank the
     *  product's landing page or pricing the way a full upsert from this page would. */
    updateEmployeeBenefits(productId: string, benefits: Array<{
        benefitType: string; scopeId: string | null; discountKind: string; discountValue: number
    }>) {
        return axios.put(`${this.apiUrl}/SeasonPass/Employee/Products/${productId}/Benefits`, { benefits })
    }
    /** Withdraw an issued pass. Distinct from the holder simply going inactive. */
    revokeEmployeePass(passPurchaseId: string, reason: string) {
        return axios.post(`${this.apiUrl}/SeasonPass/Employee/${passPurchaseId}/Revoke`, { reason })
    }
}
