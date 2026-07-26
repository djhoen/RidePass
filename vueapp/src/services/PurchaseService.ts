import axios from 'axios'
import type { PurchaseRow } from './PassService'

// Dedicated service for the Admin > Purchases list endpoint (Purchase/Admin, ListForAdmin).
// Kept separate from PassService (which still owns disputes/cancel/refund/order-details) so this
// page's paging + filter contract can evolve without touching that shared file.

export interface ListPurchasesForAdminParams {
    /** Lift the default 'hide abandoned' exclusion without naming statuses. */
    includeAbandoned?: boolean
    fromUtc?: string
    toUtc?: string
    // Present together: dropped by the server whenever email/orderId is set (all-time search).
    email?: string
    orderId?: string
    // Absent/empty -> server hides 'abandoned' by default. Present -> exact match on the set,
    // including 'abandoned' if explicitly asked for.
    statuses?: string[]
    // Absent/empty -> all kinds (v_recent_sales.kind).
    kinds?: string[]
    offset?: number
    limit?: number
}

export interface PurchaseAdminListResult {
    rows: PurchaseRow[]
    // Count matching the filters, ignoring offset/limit, so the UI can page and can tell
    // the admin when a result set is bigger than the current page.
    total: number
    offset: number
    limit: number
}

export class PurchaseService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    listPurchasesForAdmin(params: ListPurchasesForAdminParams) {
        return axios.get<{ data: PurchaseAdminListResult }>(`${this.apiUrl}/Purchase/Admin`, {
            params: {
                fromUtc: params.fromUtc,
                toUtc: params.toUtc,
                email: params.email,
                orderId: params.orderId,
                statuses: params.statuses && params.statuses.length > 0 ? params.statuses : undefined,
                kinds: params.kinds && params.kinds.length > 0 ? params.kinds : undefined,
                includeAbandoned: params.includeAbandoned ? true : undefined,
                offset: params.offset,
                limit: params.limit,
            },
            // Serialize statuses/kinds as repeated keys without `[]` brackets
            // (statuses=paid&statuses=refunded) so the ASP.NET string[]? query params bind.
            paramsSerializer: { indexes: null },
        })
    }
}
