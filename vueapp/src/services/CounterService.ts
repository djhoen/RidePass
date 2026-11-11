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
    /** Height (whole inches) and weight (whole pounds) on file (Script0317). */
    heightIn?: number | null
    weightLb?: number | null
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
    // "Another rider" on the payer's order (a day pass / gate ticket, quantity 1): who it is for, as
    // entered. The sale checks it again and creates / links their profile. Null = the payer's own line.
    otherRider?: OtherRiderInput | null
    // "rental" only: the customer's height (whole inches) and weight (whole pounds) the bike is sized for
    // (Script0317). Null = what is on their profile; the sale is refused when neither has them.
    riderHeightIn?: number | null
    riderWeightLb?: number | null
}

/** "Another rider": pick an existing profile, or type a new rider (and a minor's guardian). */
export interface OtherRiderInput {
    existingUserId?: string | null
    existingDependentId?: string | null
    firstName?: string | null
    lastName?: string | null
    birthdate?: string | null
    email?: string | null
    phone?: string | null
    guardianIsPayer?: boolean
    guardianUserId?: string | null
    guardianFirstName?: string | null
    guardianLastName?: string | null
    guardianEmail?: string | null
    guardianPhone?: string | null
    /** Needed when the guardian's age isn't on file: a guardian must be 18 or over. */
    guardianBirthdate?: string | null
    /** Tenants that require one. A minor defaults to their guardian. */
    emergencyContactName?: string | null
    emergencyContactPhone?: string | null
    /** The kiosk sign-up this rider came from: its waiver signature goes on the ticket. */
    kioskSignupId?: string | null
}

export interface OtherRiderRequest extends OtherRiderInput {
    payerUserId: string
}

/** The draft profile a line will be sold against: nothing is created until the sale. */
export interface OtherRiderProfile {
    riderUserId: string | null
    riderDependentId: string | null
    firstName: string
    lastName: string
    birthdate: string | null
    age: number | null
    isMinor: boolean
    email: string | null
    guardianUserId: string | null
    guardianName: string | null
    guardianEmail: string | null
    guardianPhone: string | null
    emergencyContactName: string | null
    emergencyContactPhone: string | null
    matchedExistingRider: boolean
    matchedExistingGuardian: boolean
    willCreateRider: boolean
    willCreateGuardian: boolean
    willCreateDependent: boolean
}

export interface CounterDependent {
    id: string
    guardianUserId: string
    riderUserId: string | null
    firstName: string
    lastName: string
    birthdate: string | null
    isMinor: boolean
}

/** signed | not_required | link_sent (a link is out, not signed yet) | needs_signature */
export type RiderWaiverStatus = 'signed' | 'not_required' | 'link_sent' | 'needs_signature'

export interface RiderWaiverLink {
    purchaseId: string
    requestId: string | null
    status: RiderWaiverStatus
    link: string | null
    signAs: 'guardian' | 'adult' | null
    recipientName: string | null
    recipientEmail: string | null
    recipientPhone: string | null
    emailSent: boolean
    smsSent: boolean
    smsAvailable: boolean
    reused: boolean
}

export interface RiderWaiverStatusItem {
    purchaseId: string
    riderName: string | null
    isMinor: boolean
    status: RiderWaiverStatus
    signedByGuardian: boolean
    guardianName: string | null
    signerName: string | null
    blockReason: string | null
    requestStatus: string | null
    requestSentAtUtc: string | null
    recipientEmail: string | null
    link: string | null
}

export interface CounterSaleLineItem {
    kind: 'pass' | 'event_ticket' | 'extras' | 'membership' | 'shop_rental'
    purchaseId: string
    redemptionToken: string
    displayName: string
    quantity: number
    unitPriceCents: number
    lineAmountCents: number
    // A ticket bought for another rider: who it is for (their waiver is collected on the receipt).
    otherRider?: boolean
    riderName?: string | null
    riderIsMinor?: boolean
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

    /** Counter customer lookup by email, name, or phone. Returns candidates; the caller picks one

     *  and then calls findRider with that email for the full record (waiver state etc). */

    searchRiders(query: string) {

        return axios.post<{ data: { id: string; email: string; firstName: string; lastName: string; phone: string | null }[] }>(

            `${this.apiUrl}/Counter/Riders/Search`, { query })

    }


    findRider(email: string) {
        return axios.post<{ data: CounterRider }>(`${this.apiUrl}/Counter/Riders/Find`, { email })
    }

    createRider(body: { email: string; firstName: string; lastName: string; birthdate: string; emergencyContactName?: string; emergencyContactPhone?: string }) {
        return axios.post<{ data: CounterRider }>(`${this.apiUrl}/Counter/Riders`, body)
    }

    createSale(body: { riderId: string; items: CounterCartItem[]; signWaiver: boolean; signatureDataUrl?: string | null; parentName?: string | null; parentPhone?: string | null; discountPresetId?: string | null; managerPin?: string | null; paymentMethod?: 'stripe' | 'cash' | null; creditAccountId?: string | null; creditCents?: number; kioskSignupIds?: string[] | null; voucherCode?: string | null }) {
        return axios.post<{ data: CounterSaleResponse }>(`${this.apiUrl}/Counter/Sale`, body)
    }

    /** Find or create another rider's profile (and a minor's guardian's), for an "another rider" line. */
    resolveOtherRider(body: OtherRiderRequest) {
        return axios.post<{ data: OtherRiderProfile }>(`${this.apiUrl}/Counter/OtherRiders`, body)
    }

    /** Minors already linked to this guardian (e.g. the paying parent's own kids). */
    listDependents(guardianUserId: string) {
        return axios.get<{ data: CounterDependent[] }>(`${this.apiUrl}/Counter/OtherRiders/Dependents`,
            { params: { guardianUserId } })
    }

    /** A signing link for one other-rider ticket; deliver = email (and text when SMS is set up). */
    riderWaiverLink(purchaseId: string, deliver: boolean) {
        return axios.post<{ data: RiderWaiverLink }>(`${this.apiUrl}/Counter/OtherRiders/WaiverLink`, { purchaseId, deliver })
    }

    /** Each ticket's waiver as the gate will judge it. */
    riderWaiverStatus(purchaseIds: string[]) {
        return axios.post<{ data: RiderWaiverStatusItem[] }>(`${this.apiUrl}/Counter/OtherRiders/WaiverStatus`, { purchaseIds })
    }
}
