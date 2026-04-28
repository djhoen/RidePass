import axios from 'axios'

export interface DailyRevenuePoint {
    date: string
    revenueCents: number
    passesSold: number
    ticketsSold: number
}

export interface TopProduct {
    productId: string
    productName: string
    soldCount: number
    revenueCents: number
}

export interface TopEvent {
    eventId: string
    eventTitle: string
    eventStartUtc: string
    soldCount: number
    revenueCents: number
}

export interface TenantReportSummary {
    fromUtc: string
    toUtc: string
    totalRevenueCents: number
    passesSold: number
    ticketsSold: number
    uniqueRiders: number
    refundedCount: number
    cancelledCount: number
    disputedCount: number
    refundedAmountCents: number
    dailyRevenue: DailyRevenuePoint[]
    topDayPassProducts: TopProduct[]
    topEvents: TopEvent[]
}

export interface TenantBreakdownRow {
    tenantId: string
    subdomain: string
    displayName: string
    passesSold: number
    ticketsSold: number
    revenueCents: number
    refundedCount: number
    disputedCount: number
}

export interface PlatformAnalyticsSummary {
    fromUtc: string
    toUtc: string
    totalRevenueCents: number
    passesSold: number
    ticketsSold: number
    refundedCount: number
    disputedCount: number
    totalTenants: number
    activeTenants: number
    dailyRevenue: DailyRevenuePoint[]
    tenantBreakdown: TenantBreakdownRow[]
}

export class ReportsService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    getTenantSummary(fromUtc: string, toUtc: string) {
        return axios.get<{ data: TenantReportSummary }>(`${this.apiUrl}/Reports/Admin/Summary`, {
            params: { fromUtc, toUtc },
        })
    }

    getPlatformAnalytics(fromUtc: string, toUtc: string) {
        return axios.get<{ data: PlatformAnalyticsSummary }>(`${this.apiUrl}/SuperAdmin/Analytics`, {
            params: { fromUtc, toUtc },
        })
    }
}
