import axios from 'axios'

export interface PassProduct {
    id: string
    name: string
    description: string | null
    priceCents: number
    isActive: boolean
    sortOrder: number
    requiresWaiver: boolean
    riderPaidServiceChargeBps: number
}

export interface WaiverDto {
    id: string
    version: number
    title: string
    body: string
}

export interface WaiverSignatureStatus {
    hasSignedCurrent: boolean
    signatureId: string | null
    signedAt: string | null
    currentVersion: number
    signatureDataUrl: string | null
    riderIsMinor: boolean
    signedByParent: boolean
    parentName: string | null
    parentPhone: string | null
    riderHasEmergencyContact: boolean
}

export interface CreatePurchaseResponse {
    purchaseId: string
    clientSecret: string
    amountCents: number
    giftCardAppliedCents: number
}

export interface PurchaseRow {
    id: string
    // Discriminator from v_recent_sales (Script0080) — 'pass', 'event_ticket',
    // 'event_extra', 'season_pass', 'membership', 'gift_card', 'rental'.
    // Drives kind-specific UI (per-kind cancel endpoint, etc.).
    kind: string
    productName: string
    purchaserName: string
    purchaserEmail: string
    amountCents: number
    status: string
    createdAt: string
}

export class PassService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    // Products
    listActive() { return axios.get<{ data: PassProduct[] }>(`${this.apiUrl}/PassProduct`) }
    listForAdmin() { return axios.get<{ data: PassProduct[] }>(`${this.apiUrl}/PassProduct/Admin`) }
    createProduct(req: Omit<PassProduct, 'id'>) { return axios.post(`${this.apiUrl}/PassProduct`, req) }
    updateProduct(id: string, req: Omit<PassProduct, 'id'>) { return axios.put(`${this.apiUrl}/PassProduct/${id}`, req) }
    deleteProduct(id: string) { return axios.delete(`${this.apiUrl}/PassProduct/${id}`) }
    reorderProducts(items: { id: string; sortOrder: number }[]) {
        return axios.post(`${this.apiUrl}/PassProduct/Reorder`, { items })
    }

    // Waiver
    getWaiver() { return axios.get<{ data: WaiverDto }>(`${this.apiUrl}/Waiver`) }
    publishWaiver(req: { title: string; body: string }) { return axios.put(`${this.apiUrl}/Waiver`, req) }
    getMySignatureStatus() { return axios.get<{ data: WaiverSignatureStatus }>(`${this.apiUrl}/Waiver/MySignature`) }
    signWaiver(body: { signatureDataUrl: string; parentName?: string | null; parentPhone?: string | null }) {
        return axios.post(`${this.apiUrl}/Waiver/Sign`, body)
    }

    // Purchases
    createPurchase(req: {
        productId: string
        validOnDate: string | null
        eventId?: string | null
        quantity?: number
        rewardRedemptionId?: string | null
        couponCode?: string | null
        giftCardCode?: string | null
        extras?: Array<{ productId: string; quantity: number; variantId?: string | null }> | null
        // Bundles a track-membership purchase into the same PI when the rider opts in
        // from the membership-required dialog instead of being kicked to /Membership.
        addMembership?: boolean
    }) {
        return axios.post<{ data: CreatePurchaseResponse }>(`${this.apiUrl}/Purchase/Pass`, req)
    }
    listPurchasesForAdmin(params: { fromUtc?: string; toUtc?: string; status?: string }) {
        return axios.get<{ data: PurchaseRow[] }>(`${this.apiUrl}/Purchase/Admin`, { params })
    }
    cancelPass(id: string, reason: string | null) {
        return axios.post(`${this.apiUrl}/Purchase/Pass/${id}/Cancel`, { reason })
    }
    cancelTicket(id: string, reason: string | null) {
        return axios.post(`${this.apiUrl}/Purchase/Ticket/${id}/Cancel`, { reason })
    }
    // Tenant-admin direct refund of any single purchase (gift cards excluded). amountCents
    // null = full-minus-service-charge default; the server clamps and executes the money.
    refund(kind: string, purchaseId: string, amountCents: number | null, reason: string | null) {
        return axios.post<{ data: { refunded: boolean; amountCents: number; refundId: string | null } }>(
            `${this.apiUrl}/Purchase/Refund`, { kind, purchaseId, amountCents, reason })
    }

    listDisputes() {
        return axios.get<{ data: TenantDisputeListItem[] }>(`${this.apiUrl}/Purchase/Admin/Disputes`)
    }
}

export interface TenantDisputeListItem {
    id: string
    kind: 'pass' | 'event_ticket' | 'unlinked'
    purchaseId: string | null
    itemName: string | null
    purchaserName: string | null
    purchaserEmail: string | null
    stripeDisputeId: string
    amountCents: number
    currency: string
    reason: string | null
    status: string
    evidenceDueByUtc: string | null
    stripeCreatedAtUtc: string
    updatedAtUtc: string
}
