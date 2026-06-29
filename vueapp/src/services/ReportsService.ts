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

export interface RevenueByKind {
    kind: string
    revenueCents: number
    saleCount: number
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
    revenueByType: RevenueByKind[]
    dailyRevenue: DailyRevenuePoint[]
    topPassProducts: TopProduct[]
    topEvents: TopEvent[]
}

// ── F&B profitability ────────────────────────────────────────────────
export interface ConcessionProfitItem {
    name: string
    qtySold: number
    revenueCents: number
    cogsCents: number
    profitCents: number
    marginPct: number
}
export interface ConcessionProfitCategory {
    category: string
    revenueCents: number
    cogsCents: number
    profitCents: number
    marginPct: number
}
export interface ConcessionProfitPayment {
    method: string
    count: number
    amountCents: number
}
export interface ConcessionProfitHour {
    hour: number
    revenueCents: number
    orderCount: number
}
export interface ConcessionProfitabilityReport {
    fromUtc: string
    toUtc: string
    netSalesCents: number
    taxCents: number
    tipsCents: number
    grossSalesCents: number
    cogsCents: number
    grossProfitCents: number
    marginPct: number
    orderCount: number
    avgOrderValueCents: number
    refundedCount: number
    refundedAmountCents: number
    items: ConcessionProfitItem[]
    categories: ConcessionProfitCategory[]
    payments: ConcessionProfitPayment[]
    hours: ConcessionProfitHour[]
}

// ── F&B sales by employee ─────────────────────────────────────────────
export interface ConcessionEmployeeRow {
    userId: string | null
    name: string
    ordersCount: number
    grossSalesCents: number
    netSalesCents: number
    taxCents: number
    tipCents: number
    cashCents: number
    cardCents: number
    refundedCount: number
    refundedCents: number
    avgOrderValueCents: number
}
export interface ConcessionEmployeeReport {
    fromUtc: string
    toUtc: string
    rows: ConcessionEmployeeRow[]
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

    getConcessionProfitability(fromUtc: string, toUtc: string) {
        return axios.get<{ data: ConcessionProfitabilityReport }>(`${this.apiUrl}/Reports/Admin/ConcessionProfitability`, {
            params: { fromUtc, toUtc },
        })
    }

    getConcessionEmployees(fromUtc: string, toUtc: string) {
        return axios.get<{ data: ConcessionEmployeeReport }>(`${this.apiUrl}/Reports/Admin/ConcessionEmployees`, {
            params: { fromUtc, toUtc },
        })
    }

    getPlatformAnalytics(fromUtc: string, toUtc: string) {
        return axios.get<{ data: PlatformAnalyticsSummary }>(`${this.apiUrl}/SuperAdmin/Analytics`, {
            params: { fromUtc, toUtc },
        })
    }

    getEventRiders(eventId: string) {
        return axios.get<{ data: EventRiderReport }>(`${this.apiUrl}/Reports/Admin/EventRiders/${eventId}`)
    }
    setCheckIn(purchaseId: string, source: 'pass' | 'event_ticket' | 'season_pass', checkedIn: boolean) {
        return axios.put(`${this.apiUrl}/Reports/Admin/EventRiders/${purchaseId}/CheckIn`,
            { source, checkedIn })
    }
    setRaceNumber(purchaseId: string, raceNumber: string | null) {
        return axios.put(`${this.apiUrl}/Reports/Admin/EventRiders/Ticket/${purchaseId}/RaceNumber`,
            { raceNumber })
    }
    // Unified send-now / schedule endpoint. When runAtUtc is null/past the
    // server returns Sent/Skipped (immediate path). When in the future the
    // server returns ScheduledTaskId so the FE can show it in the panel.
    sendRiderMessage(eventId: string, body: {
        purchaseIds: string[]
        channel: 'sms' | 'email'
        subject?: string | null
        body: string
        runAtUtc?: string | null
    }) {
        return axios.post<{ data: SendRiderMessageResponse }>(
            `${this.apiUrl}/Reports/Admin/EventRiders/${eventId}/SendMessage`, body)
    }
    listScheduledRiderMessages(eventId: string) {
        return axios.get<{ data: ScheduledRiderMessage[] }>(
            `${this.apiUrl}/Reports/Admin/EventRiders/${eventId}/ScheduledMessages`)
    }
    cancelScheduledRiderMessage(id: string) {
        return axios.post(`${this.apiUrl}/Reports/Admin/ScheduledMessages/${id}/Cancel`)
    }
    tracksideExportUrl(eventId: string): string {
        return `${this.apiUrl}/Reports/Admin/EventRiders/${eventId}/Export/Trackside`
    }

    getDailyEvents(fromUtc: string, toUtc: string, localDate: string) {
        return axios.get<{ data: DailyEventReport }>(`${this.apiUrl}/Reports/Admin/DailyEvents`, {
            params: { fromUtc, toUtc, localDate },
        })
    }

    checkInLookup(token: string, fromUtc: string, toUtc: string) {
        return axios.get<{ data: CheckInLookup }>(`${this.apiUrl}/Reports/Admin/CheckInLookup`, {
            params: { token, fromUtc, toUtc },
        })
    }
}

// Check-in lookup wire types
export interface CheckInLookup {
    userId: string | null
    purchaserName: string
    purchaserEmail: string
    purchaserPhone: string | null
    photoDataUrl: string | null
    matchedTokenKind: 'pass' | 'event_ticket' | 'season_pass'

    requiresWaiver: boolean
    waiverSigned: boolean
    requiresMembership: boolean
    membershipActive: boolean
    membershipName: string

    todayRegistrations: CheckInRegistration[]
    futureRegistrations: CheckInRegistration[]
}

export interface CheckInRegistration {
    id: string
    source: 'pass' | 'event_ticket' | 'season_pass'
    eventId: string
    eventTitle: string
    eventStartsAtUtc: string
    eventEndsAtUtc: string
    itemName: string
    status: string
    checkedIn: boolean
    checkedInAtUtc: string | null
    redemptionToken: string | null
}

export interface EventRiderReport {
    eventId: string
    eventTitle: string
    eventStartsAtUtc: string
    rows: EventRiderRow[]
    totalRegistrants: number
    totalCheckedIn: number
}

export interface EventRiderRow {
    purchaseId: string
    source: 'pass' | 'event_ticket' | 'season_pass'
    purchaserName: string
    firstName: string
    lastName: string
    purchaserEmail: string
    purchaserPhone: string | null
    itemName: string
    tierKind: 'race_entry' | 'gate_fee' | 'spectator_pass' | null
    tierAudience: 'rider' | 'spectator' | null
    raceNumber: string | null
    userRaceNumber: string | null
    hometown: string | null
    quantity: number
    amountCents: number
    status: string
    checkedIn: boolean
    checkedInAtUtc: string | null
    createdAtUtc: string
}

export interface SendRiderSmsResponse {
    sent: number
    skipped: number
    skippedNames: string[]
}

// New unified response: populated for the immediate-send path OR the
// scheduled path. Exactly one of the two halves is set per response.
export interface SendRiderMessageResponse {
    // Immediate-send half.
    sent?: number | null
    skipped?: number | null
    skippedNames?: string[] | null
    // Scheduled half.
    scheduledTaskId?: string | null
    scheduledRunAtUtc?: string | null
}

export interface ScheduledRiderMessage {
    id: string
    kind: string
    runAtUtc: string
    status: string
    summary: string | null
    createdAtUtc: string
    createdByUserId: string | null
}

export interface DailyEventReport {
    localDate: string
    rows: DailyEventRow[]
}

export interface DailyEventRow {
    eventId: string
    title: string
    eventTypeName: string
    startsAtUtc: string
    endsAtUtc: string
    allDay: boolean
    capacity: number | null
    status: string
    registered: number
    checkedIn: number
    revenueCents: number
}
