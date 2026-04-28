import axios from 'axios'

export interface RevenueBlock {
    revenueCents: number
    passesSold: number
    ticketsSold: number
}

export interface DailySparkPoint {
    date: string
    revenueCents: number
}

export interface UpcomingEvent {
    id: string
    title: string
    startsAtUtc: string
    endsAtUtc: string
    eventTypeName: string
    eventTypeColor: string
    capacity: number | null
    locationLabel: string | null
}

export interface RecentPurchase {
    id: string
    productName: string
    purchaserName: string
    amountCents: number
    status: string
    createdAtUtc: string
}

export interface DashboardSnapshot {
    permissions: string[]
    todayRevenue: RevenueBlock | null
    monthRevenue: RevenueBlock | null
    uniqueRidersMonth: number | null
    last7Days: DailySparkPoint[] | null
    upcomingEvents: UpcomingEvent[]
    recentPurchases: RecentPurchase[] | null
    openDisputesCount: number | null
    pendingRefundsCount: number | null
}

export interface DashboardWidgetEntry {
    type: string
    visible: boolean
    order: number
}

export interface DashboardConfig {
    widgets: DashboardWidgetEntry[]
}

export class DashboardService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    getSnapshot() {
        return axios.get<{ data: DashboardSnapshot }>(`${this.apiUrl}/Dashboard/Snapshot`)
    }

    getConfig() {
        return axios.get<{ data: { config: string | null } }>(`${this.apiUrl}/Dashboard/Config`)
    }

    saveConfig(config: DashboardConfig) {
        return axios.put(`${this.apiUrl}/Dashboard/Config`, config)
    }
}
