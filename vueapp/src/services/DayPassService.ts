import axios from 'axios'

export interface DayPassProduct {
    id: string
    name: string
    description: string | null
    priceCents: number
    isActive: boolean
    sortOrder: number
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
}

export interface CreatePurchaseResponse {
    purchaseId: string
    clientSecret: string
    amountCents: number
}

export interface PurchaseRow {
    id: string
    productName: string
    purchaserName: string
    purchaserEmail: string
    amountCents: number
    status: string
    validOnDate: string | null
    createdAt: string
}

export class DayPassService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    // Products
    listActive() { return axios.get<{ data: DayPassProduct[] }>(`${this.apiUrl}/DayPassProduct`) }
    listForAdmin() { return axios.get<{ data: DayPassProduct[] }>(`${this.apiUrl}/DayPassProduct/Admin`) }
    createProduct(req: Omit<DayPassProduct, 'id'>) { return axios.post(`${this.apiUrl}/DayPassProduct`, req) }
    updateProduct(id: string, req: Omit<DayPassProduct, 'id'>) { return axios.put(`${this.apiUrl}/DayPassProduct/${id}`, req) }
    deleteProduct(id: string) { return axios.delete(`${this.apiUrl}/DayPassProduct/${id}`) }

    // Waiver
    getWaiver() { return axios.get<{ data: WaiverDto }>(`${this.apiUrl}/Waiver`) }
    publishWaiver(req: { title: string; body: string }) { return axios.put(`${this.apiUrl}/Waiver`, req) }
    getMySignatureStatus() { return axios.get<{ data: WaiverSignatureStatus }>(`${this.apiUrl}/Waiver/MySignature`) }
    signWaiver() { return axios.post(`${this.apiUrl}/Waiver/Sign`) }

    // Purchases
    createPurchase(req: { productId: string; validOnDate: string | null; eventId?: string | null; quantity?: number }) {
        return axios.post<{ data: CreatePurchaseResponse }>(`${this.apiUrl}/Purchase/DayPass`, req)
    }
    listPurchasesForAdmin(params: { fromUtc?: string; toUtc?: string; status?: string }) {
        return axios.get<{ data: PurchaseRow[] }>(`${this.apiUrl}/Purchase/Admin`, { params })
    }
    cancelDayPass(id: string, reason: string | null) {
        return axios.post(`${this.apiUrl}/Purchase/DayPass/${id}/Cancel`, { reason })
    }
    cancelTicket(id: string, reason: string | null) {
        return axios.post(`${this.apiUrl}/Purchase/Ticket/${id}/Cancel`, { reason })
    }

    listDisputes() {
        return axios.get<{ data: TenantDisputeListItem[] }>(`${this.apiUrl}/Purchase/Admin/Disputes`)
    }
}

export interface TenantDisputeListItem {
    id: string
    kind: 'day_pass' | 'event_ticket' | 'unlinked'
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
