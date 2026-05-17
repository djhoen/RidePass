import axios from 'axios'

export interface Coupon {
    id: string
    code: string
    description: string | null
    discountKind: 'percent' | 'amount'
    /** bps when percent (10000 = 100%), cents when amount */
    discountValue: number
    applicableScope: 'all' | 'pass' | 'event_ticket' | 'season_pass'
    applicableEventId: string | null
    validFromUtc: string | null
    validToUtc: string | null
    maxTotalUses: number | null
    maxUsesPerUser: number | null
    isActive: boolean
    redemptionCount: number
    createdAt: string
}

export interface UpsertCoupon {
    code: string
    description: string | null
    discountKind: 'percent' | 'amount'
    discountValue: number
    applicableScope: 'all' | 'pass' | 'event_ticket' | 'season_pass'
    applicableEventId: string | null
    validFromUtc: string | null
    validToUtc: string | null
    maxTotalUses: number | null
    maxUsesPerUser: number | null
    isActive: boolean
}

export class CouponService {
    private apiUrl: string
    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }
    list() {
        return axios.get<{ data: Coupon[] }>(`${this.apiUrl}/Coupon`)
    }
    create(body: UpsertCoupon) {
        return axios.post<{ data: Coupon }>(`${this.apiUrl}/Coupon`, body)
    }
    update(id: string, body: UpsertCoupon) {
        return axios.put<{ data: Coupon }>(`${this.apiUrl}/Coupon/${id}`, body)
    }
    delete(id: string) {
        return axios.delete(`${this.apiUrl}/Coupon/${id}`)
    }
}
