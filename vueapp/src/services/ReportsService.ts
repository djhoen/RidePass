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
    /** Same daily money as dailyRevenue, split by profit center; series sum to it day by day. */
    revenueByProfitCenter: ProfitCenterSeries[]
    topPassProducts: TopProduct[]
    topEvents: TopEvent[]
}

export interface ProfitCenterSeriesPoint {
    date: string
    revenueCents: number
}
export interface ProfitCenterSeries {
    /** Stable per-entity key; the color follows this, never the series' position in the list. */
    key: string
    label: string
    color: string
    totalCents: number
    /** Gapless: one point per date in dailyRevenue, in the same order. */
    points: ProfitCenterSeriesPoint[]
}

// ── End of Day (Z report) ─────────────────────────────────────────────
// One tenant-local business date, sourced from the same accounting read model the QuickBooks
// sync posts from, so every line here matches that day's journal entry.
export interface EndOfDayRevenueLine {
    key: string
    label: string
    saleCount: number
    refundCount: number
    grossCents: number
    refundCents: number
    netGrossCents: number
    taxCents: number
    tipCents: number
    netRevenueCents: number
    // Set only for tenants with configured profit centers; the table groups by these when present.
    profitCenterKey: string | null
    profitCenterLabel: string | null
    profitCenterColor: string | null
    profitCenterSort: number
}
export interface EndOfDayTotals {
    grossSalesCents: number
    refundsCents: number
    netSalesCents: number
    taxCents: number
    tipsCents: number
    netRevenueCents: number
    giftCardsSoldCents: number
    giftCardsRedeemedCents: number
    depositsCollectedCents: number
    depositsReleasedCents: number
    disputeLossCents: number
    disputeFeeCents: number
    platformChargesCents: number
    stripeFeesCents: number
    ridepassFeesCents: number
    netToTenantCents: number
    transactionCount: number
    refundCount: number
}
export interface EndOfDayTenderLine {
    method: string
    label: string
    amountCents: number
    count: number
}
export interface EndOfDayStaffLine {
    userId: string
    name: string
    saleCount: number
    refundCount: number
    grossCents: number
    cashCents: number
}
export interface EndOfDayCashSession {
    id: string
    userId: string
    userName: string
    eventTitle: string | null
    deviceId: string | null
    openingFloatCents: number
    status: string
    openedAtUtc: string
    closedAtUtc: string | null
}
export interface EndOfDayCashTurnIn {
    id: string
    workerName: string
    managerName: string | null
    expectedCents: number | null
    workerCountedCents: number
    managerCountedCents: number | null
    varianceCents: number | null
    status: string
    note: string | null
    submittedAtUtc: string
    confirmedAtUtc: string | null
}
export interface EndOfDayCashSection {
    sessions: EndOfDayCashSession[]
    turnIns: EndOfDayCashTurnIn[]
    openingFloatCents: number
    workerCountedCents: number
    managerCountedCents: number
    cashSalesCents: number
}
export interface EndOfDayQuickBooksStatus {
    connected: boolean
    // not_connected | disabled | pending | success | failed | no_activity
    status: string
    docNumber: string | null
    journalEntryId: string | null
    syncedAtUtc: string | null
    lastError: string | null
}
export interface EndOfDayReport {
    // Tenant-local calendar date, yyyy-MM-dd. A DATE, not an instant: never run it through the
    // UTC-to-tenant-timezone formatters or western timezones will shift it back a day.
    businessDate: string
    timezone: string
    generatedAtUtc: string
    revenue: EndOfDayRevenueLine[]
    totals: EndOfDayTotals
    tenders: EndOfDayTenderLine[]
    staff: EndOfDayStaffLine[]
    cash: EndOfDayCashSection
    quickBooks: EndOfDayQuickBooksStatus
}

// ── Tax ───────────────────────────────────────────────────────────────
export interface AdmissionTaxReport {
    fromUtc: string
    toUtc: string
    taxCollectedCents: number
    refundedTaxCents: number
    netTaxCents: number
    taxableSalesCents: number
    taxedTicketCount: number
    currentRateBps: number
    jurisdictionLabel: string | null
}
export interface SalesTaxCategoryRow {
    key: string
    label: string
    taxCents: number
    collectedTaxCents: number
    refundedTaxCents: number
    taxableSalesCents: number
    saleCount: number
}
export interface SalesTaxDayRow {
    // Tenant-local calendar date, yyyy-MM-dd. Date-only, see EndOfDayReport.businessDate.
    businessDate: string
    taxCents: number
    collectedTaxCents: number
    refundedTaxCents: number
    taxableSalesCents: number
    saleCount: number
}
export interface SalesTaxReport {
    fromUtc: string
    toUtc: string
    timezone: string
    netTaxCents: number
    collectedTaxCents: number
    refundedTaxCents: number
    taxableSalesCents: number
    taxedSaleCount: number
    byCategory: SalesTaxCategoryRow[]
    byDay: SalesTaxDayRow[]
}

// ── Revenue by department ────────────────────────────────────────────
// The same QuickBooks revenue slots the End of Day report and the posted journal entry use,
// rolled up into business units. `key` is stable (from QboDepartments / QboAccountKeys), so it
// is safe to key UI state on; `label` is display copy and may be reworded.
export interface RevenueCategoryRow {
    key: string
    label: string
    /** Gross minus tax minus tips, net of refunds. */
    netRevenueCents: number
    grossCents: number
    taxCents: number
    tipCents: number
    /** Negative, and already inside grossCents. */
    refundCents: number
    saleCount: number
    refundCount: number
}
export interface RevenueDepartmentRow {
    key: string
    label: string
    /** #RRGGBB, the same color this bucket wears on the other reports. */
    color: string
    netRevenueCents: number
    grossCents: number
    taxCents: number
    tipCents: number
    refundCents: number
    saleCount: number
    refundCount: number
    /** Share of the period's net revenue, 0-100 with one decimal. Rounded server side. */
    pctOfTotal: number
    categories: RevenueCategoryRow[]
}
export interface RevenueByDepartmentReport {
    fromUtc: string
    toUtc: string
    timezone: string
    netRevenueCents: number
    grossCents: number
    taxCents: number
    tipCents: number
    refundCents: number
    saleCount: number
    refundCount: number
    /** Only departments with activity in the period, in report order. */
    departments: RevenueDepartmentRow[]
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

export interface RiderReportItem {
    purchaseId: string
    source: 'ticket' | 'season_pass'
    // Null for a walk-up season-pass admission: anchored to a calendar date, not an event.
    // eventStartsAtUtc is still always set (the walk-up date at tenant-local midnight).
    eventId: string | null
    eventTitle: string | null
    eventStartsAtUtc: string
    riderName: string
    email: string | null
    userId: string | null
    itemName: string
    checkedIn: boolean
    checkedInAtUtc: string | null
    wristbandCode: string | null
    waiverSigned: boolean
    purchaseType: RiderPurchaseType
    eventTypeName: string | null   // the tenant's own label ("Lift Day", "Clinic")
    eventTypeCode: string | null   // stable code (open_ride, lesson, race, ...) across renames
    registrationComplete: boolean
    ageAtEvent: number | null      // age on the event day, when a birthdate was captured
}

// Server-derived bucket for "how did this person get in". Mirrors RiderPurchaseTypes.
export type RiderPurchaseType =
    | 'day_ticket' | 'race_entry'
    | 'season_pass_unlimited' | 'season_pass_credits' | 'season_pass_days'
    | 'spectator_pass'

export const RIDER_PURCHASE_TYPE_LABELS: Record<RiderPurchaseType, string> = {
    day_ticket: 'Day ticket',
    race_entry: 'Race entry',
    season_pass_unlimited: 'Season pass (unlimited)',
    season_pass_credits: 'Season pass (credit pack)',
    season_pass_days: 'Season pass (set days)',
    spectator_pass: 'Spectator pass',
}

export interface RiderReportResponse {
    rows: RiderReportItem[]
    truncated: boolean
    totalRows: number
    totalCheckedIn: number
    totalMissingWaiver: number
}

export interface RiderWaiverItem {
    id: string
    waiverName: string
    waiverVersion: number
    signedAtUtc: string
    signedByParent: boolean
    parentName: string | null
    signerName: string | null
    waiverIsCurrent: boolean
    hasSignatureImage: boolean
}

export interface RiderProfileItem {
    userId: string | null
    email: string | null
    phone: string | null
    hometown: string | null
    raceNumber: string | null
    birthdateUtc: string | null
    age: number | null
    memberSinceUtc: string | null
    bike: string | null
    emergencyContactName: string | null
    emergencyContactPhone: string | null
    parentGuardianName: string | null
    totalRegistrations: number
    totalCheckedIn: number
    totalSpentCents: number
    firstVisitUtc: string | null
    lastVisitUtc: string | null
    isGuest: boolean
}

export interface RiderDetailResponse {
    riderName: string
    email: string | null
    profile: RiderProfileItem | null
    registrations: RiderReportItem[]
    waivers: RiderWaiverItem[]
}

export class ReportsService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    getRiders(fromUtc: string, toUtc: string, search?: string, audience: 'rider' | 'spectator' = 'rider',
              filters?: { purchaseTypes?: string[]; eventTypeCodes?: string[] }) {
        return axios.get<{ data: RiderReportResponse }>(`${this.apiUrl}/Reports/Admin/Riders`, {
            params: {
                fromUtc, toUtc, search: search || undefined, audience,
                // Comma-separated so the filtered view stays a shareable URL.
                purchaseTypes: filters?.purchaseTypes?.length ? filters.purchaseTypes.join(',') : undefined,
                eventTypeCodes: filters?.eventTypeCodes?.length ? filters.eventTypeCodes.join(',') : undefined,
            },
        })
    }

    // Fetched only when an admin opens a specific signature; the drill-in payload omits images.
    getRiderWaiverSignature(signatureId: string) {
        return axios.get<{ data: { signatureDataUrl: string } }>(
            `${this.apiUrl}/Reports/Admin/RiderWaiver/${signatureId}/Signature`)
    }

    getRiderDetail(params: { userId?: string | null; email?: string | null; name?: string | null }) {
        return axios.get<{ data: RiderDetailResponse }>(`${this.apiUrl}/Reports/Admin/RiderDetail`, {
            params: { userId: params.userId || undefined, email: params.email || undefined, name: params.name || undefined },
        })
    }

    getTenantSummary(fromUtc: string, toUtc: string) {
        return axios.get<{ data: TenantReportSummary }>(`${this.apiUrl}/Reports/Admin/Summary`, {
            params: { fromUtc, toUtc },
        })
    }

    // date is a tenant-local calendar date (yyyy-MM-dd). Omit it for "today at the track".
    getEndOfDay(date?: string) {
        return axios.get<{ data: EndOfDayReport }>(`${this.apiUrl}/Reports/Admin/EndOfDay`, {
            params: { date: date || undefined },
        })
    }

    // Fetched through axios so the Bearer token rides along; a plain <a href> GET carries no auth
    // and would silently 401.
    async downloadEndOfDayCsv(date: string): Promise<{ blob: Blob; filename: string }> {
        const r = await axios.get(`${this.apiUrl}/Reports/Admin/EndOfDay/Csv`,
            { params: { date: date || undefined }, responseType: 'blob' })
        const cd = (r.headers['content-disposition'] as string | undefined) ?? ''
        const filename = cd.match(/filename="?([^";]+)"?/)?.[1] ?? `end-of-day-${date}.csv`
        return { blob: r.data as Blob, filename }
    }

    getAdmissionTax(fromUtc: string, toUtc: string) {
        return axios.get<{ data: AdmissionTaxReport }>(`${this.apiUrl}/Reports/Admin/AdmissionTax`, {
            params: { fromUtc, toUtc },
        })
    }

    getSalesTax(fromUtc: string, toUtc: string) {
        return axios.get<{ data: SalesTaxReport }>(`${this.apiUrl}/Reports/Admin/SalesTax`, {
            params: { fromUtc, toUtc },
        })
    }

    getRevenueByDepartment(fromUtc: string, toUtc: string) {
        return axios.get<{ data: RevenueByDepartmentReport }>(`${this.apiUrl}/Reports/Admin/RevenueByDepartment`, {
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
    getEventWaiverSignatures(eventId: string) {
        return axios.get<{ data: EventWaiverSignatureReport }>(`${this.apiUrl}/Reports/Admin/Events/${eventId}/WaiverSignatures`)
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
    // Fetch the CSV through axios so the Bearer token is attached (a plain <a href> GET carries no
    // auth and silently 401s), then hand back the blob + filename for a client-side download.
    async downloadTracksideCsv(eventId: string): Promise<{ blob: Blob; filename: string }> {
        const r = await axios.get(`${this.apiUrl}/Reports/Admin/EventRiders/${eventId}/Export/Trackside`,
            { responseType: 'blob' })
        const cd = (r.headers['content-disposition'] as string | undefined) ?? ''
        const filename = cd.match(/filename="?([^";]+)"?/)?.[1] ?? `trackside-${eventId}.csv`
        return { blob: r.data as Blob, filename }
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

export interface EventWaiverSignatureReport {
    eventId: string
    eventTitle: string
    eventStartsAtUtc: string
    totalAttendees: number
    totalSigned: number
    rows: EventWaiverSignatureRow[]
}

export interface EventWaiverSignatureRow {
    purchaseId: string
    attendeeName: string
    audience: 'rider' | 'spectator'
    tierName: string
    raceNumber: string | null
    status: string
    registrationComplete: boolean
    waiverRequired: boolean
    waiverSigned: boolean
    signedAtUtc: string | null
    signedByParent: boolean
    parentGuardianName: string | null
    signerName: string | null
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
