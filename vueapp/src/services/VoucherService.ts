import axios from 'axios'

// Vouchers (Script0315): codes good for one free rider admission, redeemed onto a rider profile at
// online checkout (the coupon box) or at Gate Sale. Staff give them from a yearly allowance; Admins
// are unlimited and make vendor lists.

export interface VoucherBatch {
    id: string
    kind: 'employee' | 'vendor'
    issuedByUserId: string
    issuedByName: string
    label: string | null
    recipientEmail: string | null
    note: string | null
    codeCount: number
    redeemedCount: number
    voidedCount: number
    eventId: string | null
    eventTitle: string | null
    expiresAtUtc: string | null
    createdAtUtc: string
}

export interface VoucherCode {
    id: string
    code: string
    status: 'unused' | 'redeemed' | 'voided' | 'expired'
    expiresAtUtc: string | null
    redeemedAtUtc: string | null
    redeemedByUserId: string | null
    redeemedByName: string | null
    riderName: string | null
    eventTitle: string | null
}

export interface MyVouchers {
    unlimited: boolean
    remaining: number | null
    yearlyAllowance: number | null
    issuedThisYear: number
    year: number
    canManage: boolean
    batches: VoucherBatch[]
}

export interface IssuedVouchers {
    batch: VoucherBatch
    codes: string[]
    emailed: boolean
}

export interface StaffVoucher {
    userId: string
    name: string
    email: string
    isAdmin: boolean
    unlimited: boolean
    yearlyAllowance: number | null
    issuedThisYear: number
    redeemedThisYear: number
    remaining: number | null
}

export class VoucherService {
    private apiUrl: string
    // Typed loosely: this project's env typings are not set up for vue-tsc.
    constructor() { this.apiUrl = (import.meta as any).env?.VITE_API_ENDPOINT ?? '' }

    mine() { return axios.get<{ data: MyVouchers }>(`${this.apiUrl}/Voucher/Mine`) }
    give(req: { count: number; recipientName: string; recipientEmail: string | null; note: string | null; expiresOn: string | null }) {
        return axios.post<{ data: IssuedVouchers }>(`${this.apiUrl}/Voucher/Mine`, req)
    }
    codes(batchId: string) { return axios.get<{ data: VoucherCode[] }>(`${this.apiUrl}/Voucher/Batches/${batchId}/Codes`) }
    void(batchId: string, couponId: string | null) {
        return axios.post<{ data: { voided: number } }>(`${this.apiUrl}/Voucher/Batches/${batchId}/Void`, { couponId })
    }
    // Through axios so the Bearer token rides along; a plain <a href> would 401.
    async downloadCsv(batchId: string): Promise<{ blob: Blob; filename: string }> {
        const r = await axios.get(`${this.apiUrl}/Voucher/Batches/${batchId}/Codes.csv`, { responseType: 'blob' })
        const cd = (r.headers['content-disposition'] as string | undefined) ?? ''
        const filename = cd.match(/filename="?([^";]+)"?/)?.[1] ?? 'vouchers.csv'
        return { blob: r.data as Blob, filename }
    }

    // Admins
    staff() { return axios.get<{ data: StaffVoucher[] }>(`${this.apiUrl}/Voucher/Staff`) }
    setAllowance(userId: string, yearlyAllowance: number | null, unlimited: boolean) {
        return axios.put(`${this.apiUrl}/Voucher/Staff/${userId}/Allowance`, { yearlyAllowance, unlimited })
    }
    batches(kind: 'employee' | 'vendor' | null) {
        return axios.get<{ data: VoucherBatch[] }>(`${this.apiUrl}/Voucher/Batches`, { params: { kind: kind ?? undefined } })
    }
    createVendor(req: { label: string; count: number; recipientEmail: string | null; note: string | null; expiresOn: string | null }) {
        return axios.post<{ data: IssuedVouchers }>(`${this.apiUrl}/Voucher/Vendor`, req)
    }
}
