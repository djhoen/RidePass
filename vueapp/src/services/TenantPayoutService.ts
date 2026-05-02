import axios from 'axios'
import { triggerBlobDownload, type TenantBalanceSummary, type LedgerEntry, type PayoutSummary } from './SuperAdminService'

export class TenantPayoutService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    getBalance() {
        return axios.get<{ data: TenantBalanceSummary }>(`${this.apiUrl}/TenantPayout/Balance`)
    }

    listLedger(fromUtc?: string, toUtc?: string, take = 200) {
        return axios.get<{ data: LedgerEntry[] }>(`${this.apiUrl}/TenantPayout/Ledger`, {
            params: { fromUtc, toUtc, take },
        })
    }

    listPayouts() {
        return axios.get<{ data: PayoutSummary[] }>(`${this.apiUrl}/TenantPayout/Payouts`)
    }

    getPayout(payoutId: string) {
        return axios.get<{ data: { payout: PayoutSummary; entries: LedgerEntry[] } }>(
            `${this.apiUrl}/TenantPayout/Payouts/${payoutId}`)
    }

    async downloadPayoutCsv(payoutId: string) {
        const r = await axios.get(`${this.apiUrl}/TenantPayout/Payouts/${payoutId}/Csv`, { responseType: 'blob' })
        const cd = (r.headers['content-disposition'] as string | undefined) ?? ''
        const filename = cd.match(/filename="?([^";]+)"?/)?.[1] ?? `payout-${payoutId}.csv`
        triggerBlobDownload(r.data, filename)
    }
}
