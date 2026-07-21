import axios from 'axios'

// Store credit: per-tenant customer balances (see docs/bike-shop.md, Script0193).
// Lookup is the counter-facing call (ShopCounter); the rest back the admin page.

export interface CreditAccount {
    id: string
    userId: string | null
    email: string | null
    phone: string | null
    displayName: string | null
    balanceCents: number
    createdAt: string
    updatedAt: string
}

export interface CreditEntry {
    id: string
    accountId: string
    deltaCents: number
    kind: 'deposit_excess' | 'refund_to_credit' | 'loyalty_award' | 'manual_adjust' | 'redeem' | 'redeem_reversal'
    referenceKind: string | null
    referenceId: string | null
    note: string | null
    createdByUserId: string | null
    createdAt: string
}

export interface CreditLookupResult {
    id: string
    displayName: string | null
    balanceCents: number
}

export class CreditService {
    private apiUrl = import.meta.env.VITE_API_URL

    searchAccounts(query: string | null, limit = 50) {
        const q = query ? `&query=${encodeURIComponent(query)}` : ''
        return axios.get<{ data: { accounts: CreditAccount[]; outstandingCents: number } }>(
            `${this.apiUrl}/Credit/Accounts?limit=${limit}${q}`)
    }
    getEntries(accountId: string, limit = 100) {
        return axios.get<{ data: { account: CreditAccount; entries: CreditEntry[] } }>(
            `${this.apiUrl}/Credit/Accounts/${accountId}/Entries?limit=${limit}`)
    }
    createAccount(req: { email?: string | null; phone?: string | null; displayName?: string | null; userId?: string | null }) {
        return axios.post<{ data: CreditAccount }>(`${this.apiUrl}/Credit/Accounts`, req)
    }
    adjust(accountId: string, deltaCents: number, note: string | null) {
        return axios.post<{ data: CreditAccount }>(`${this.apiUrl}/Credit/Accounts/${accountId}/Adjust`, { deltaCents, note })
    }
    lookup(query: string) {
        return axios.get<{ data: CreditLookupResult }>(`${this.apiUrl}/Credit/Lookup?query=${encodeURIComponent(query)}`)
    }
    // The signed-in rider's own balance + recent history.
    mine() {
        return axios.get<{ data: { balanceCents: number; entries: Pick<CreditEntry, 'deltaCents' | 'kind' | 'note' | 'createdAt'>[] } }>(
            `${this.apiUrl}/Credit/Mine`)
    }
}
