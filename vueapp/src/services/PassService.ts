import axios from 'axios'

// NOTE: Day Pass (pass_product) was retired, but this service also carries the rider
// waiver-signing and tenant admin purchase/refund/dispute calls that the waiver page,
// Counter sale, and Admin → Purchases still use. Those are kept here; only the
// pass-product catalog + pass purchase/cancel methods were removed.

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

export interface PurchaseRow {
    id: string
    // Discriminator from v_recent_sales (Script0080) — 'event_ticket', 'event_extra',
    // 'season_pass', 'membership', 'gift_card', 'rental', 'concession'.
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

    // Waiver
    getWaiver() { return axios.get<{ data: WaiverDto }>(`${this.apiUrl}/Waiver`) }
    publishWaiver(req: { title: string; body: string }) { return axios.put(`${this.apiUrl}/Waiver`, req) }
    getMySignatureStatus() { return axios.get<{ data: WaiverSignatureStatus }>(`${this.apiUrl}/Waiver/MySignature`) }
    signWaiver(body: { signatureDataUrl: string; parentName?: string | null; parentPhone?: string | null }) {
        return axios.post(`${this.apiUrl}/Waiver/Sign`, body)
    }

    // Admin purchases / refunds / disputes
    listPurchasesForAdmin(params: { fromUtc?: string; toUtc?: string; status?: string }) {
        return axios.get<{ data: PurchaseRow[] }>(`${this.apiUrl}/Purchase/Admin`, { params })
    }
    cancelTicket(id: string, reason: string | null) {
        return axios.post(`${this.apiUrl}/Purchase/Ticket/${id}/Cancel`, { reason })
    }
    // Tenant-admin direct refund of any single purchase (gift cards excluded). amountCents
    // null = full-minus-service-charge default; the server clamps and executes the money.
    // forceCheckedIn refunds even an already-checked-in entry (needs sales.refund.override).
    refund(kind: string, purchaseId: string, amountCents: number | null, reason: string | null,
           forceCheckedIn = false) {
        return axios.post<{ data: { refunded: boolean; amountCents: number; refundId: string | null } }>(
            `${this.apiUrl}/Purchase/Refund`, { kind, purchaseId, amountCents, reason, forceCheckedIn })
    }

    // Refund every line on the same order (all items sharing the anchor's PaymentIntent), each in
    // full including the service charge. forceCheckedIn needs sales.refund.override.
    refundOrder(kind: string, purchaseId: string, reason: string | null, forceCheckedIn = false) {
        return axios.post<{ data: { refundedCount: number; totalCents: number; errors: string[] } }>(
            `${this.apiUrl}/Purchase/RefundOrder`, { kind, purchaseId, reason, forceCheckedIn })
    }

    listDisputes() {
        return axios.get<{ data: TenantDisputeListItem[] }>(`${this.apiUrl}/Purchase/Admin/Disputes`)
    }
}

export interface TenantDisputeListItem {
    id: string
    kind: 'event_ticket' | 'unlinked'
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
