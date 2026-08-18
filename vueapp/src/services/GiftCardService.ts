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

// ── Admin (list / detail / import / void) ─────────────────────────────
export interface GiftCardAdminRow {
    id: string
    codeMasked: string
    initialAmountCents: number
    balanceCents: number
    status: string            // pending | active | depleted | refunded | void
    recipientName: string | null
    recipientEmail: string | null
    buyerName: string | null
    imported: boolean
    importedFrom: string | null
    createdAt: string
}

export interface GiftCardAdminList {
    items: GiftCardAdminRow[]
    total: number
}

export interface GiftCardAdminDetail {
    id: string
    code: string
    initialAmountCents: number
    balanceCents: number
    status: string
    deliveryStatus: string
    buyerName: string | null
    buyerEmail: string | null
    recipientName: string | null
    recipientEmail: string | null
    personalNote: string | null
    imported: boolean
    importedFrom: string | null
    importedAt: string | null
    createdAt: string
    redemptions: { sourceKind: string; amountCents: number; redeemedAt: string }[]
}

export interface GiftCardImportReport {
    dryRun: boolean
    totalRows: number
    imported: number
    totalBalanceCents: number
    errors: { line: number; code: string | null; reason: string }[]
}

export class GiftCardService {
    private apiUrl: string
    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    buy(req: BuyGiftCardRequest) {
        return axios.post<{ data: BuyGiftCardResponse }>(`${this.apiUrl}/Purchase/GiftCard`, req)
    }

    adminList(params: { search?: string; status?: string; page: number; pageSize: number }) {
        const qs = new URLSearchParams()
        if (params.search) qs.set('search', params.search)
        if (params.status) qs.set('status', params.status)
        qs.set('page', String(params.page))
        qs.set('pageSize', String(params.pageSize))
        return axios.get<{ data: GiftCardAdminList }>(`${this.apiUrl}/GiftCard/Admin/List?${qs.toString()}`)
    }
    adminGet(id: string) {
        return axios.get<{ data: GiftCardAdminDetail }>(`${this.apiUrl}/GiftCard/Admin/${id}`)
    }
    adminImport(csvText: string, dryRun: boolean, source: string | null) {
        return axios.post<{ data: GiftCardImportReport }>(`${this.apiUrl}/GiftCard/Admin/Import`, { csvText, dryRun, source })
    }
    adminVoid(id: string) {
        return axios.post(`${this.apiUrl}/GiftCard/Admin/${id}/Void`, {})
    }
}
