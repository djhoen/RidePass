import axios from 'axios'
import type { StaffCodeMode, TipsPayoutMode } from '@/services/ConcessionService'

// Cash sessions, blind-count turn-ins and the F&B tips flow (shift review, shift close, house-owes,
// manager pay-out, tip jar). Mirrors webapi/Controllers/CashController.cs; every call is
// tenant-scoped server side.

export interface CashSession {
    id: string
    tenantId: string
    eventId: string | null          // null = general (non-event) counter session
    userId: string
    deviceId: string | null
    openingFloatCents: number
    status: 'open' | 'turned_in' | 'closed' | string
    openedAt: string
    closedAt: string | null
}

// A worker's turn-in. The tip columns are snapshotted at shift close so the numbers on the
// receipt never drift when later sales or refunds land.
export interface CashTurnIn {
    id: string
    tenantId: string
    cashSessionId: string
    eventId: string | null
    workerUserId: string
    managerUserId: string | null
    expectedCents: number | null
    workerCountedCents: number
    managerCountedCents: number | null
    varianceCents: number | null
    status: 'submitted' | 'confirmed' | string
    note: string | null
    submittedAt: string
    confirmedAt: string | null
    tipsEarnedCents: number
    cardTipsCents: number
    cashTipsDeclaredCents: number
    tipsFromDrawerCents: number
    houseOwesCents: number
}

// The worker's own shift numbers. Deliberately carries no expected-cash figure: the count that
// follows is blind.
export interface ShiftReview {
    session: CashSession | null
    windowFromUtc: string
    windowToUtc: string
    tipsEarnedCents: number
    cardTipsCents: number
    cashSaleTipsCents: number
    saleCount: number
    cashSaleCount: number
    owedToYouCents: number          // outstanding house-owes from earlier shifts
    // tipsEarnedCents is every tip not yet settled on a shift close. A tab opened on an earlier
    // shift and closed after it was turned in lands here; this is that older part.
    tipsFromEarlierShiftsCents: number
    // The drawer the terminal has open (eventId, else no-event), the one tips come out of in
    // drawer_at_close mode. When false the shift report still works, nothing is taken.
    drawerOpen: boolean
    drawerSessionId: string | null
    // Tenant settings echoed so the close dialog knows whether to ask for a drawer amount.
    // Optional only so a POS talking to an API without them still renders (defaults apply).
    tipsPayoutMode?: TipsPayoutMode
    staffCodeMode?: StaffCodeMode
}

export interface OpenCashSessionRequest {
    eventId: string | null
    openingFloatCents: number
    deviceId?: string | null
}

// A pure blind-count drawer turn-in. Tips are settled by shiftClose() now; the two tip fields are
// kept for compatibility and ignored by the server.
export interface SubmitTurnInRequest {
    eventId: string | null
    workerCountedCents: number
    note: string
    /** @deprecated settled by shiftClose(); ignored if sent */
    tipsFromDrawerCents?: number
    /** @deprecated settled by shiftClose(); ignored if sent */
    cashTipsDeclaredCents?: number
}

// One person's shift report. cashierToken names the person when staff codes are on; null means
// the signed-in account. In manager_later mode tipsFromDrawerCents must be 0.
export interface ShiftCloseRequest {
    cashierToken: string | null
    cashTipsDeclaredCents: number
    tipsFromDrawerCents: number
    note: string
    // Fresh per dialog open. A retry after a timed-out success returns the same shift instead of a second one.
    clientRequestId: string
    eventId?: string | null
}

// The tip_shift row: the unit of "a person's shift report". Figures are snapshotted at close.
export interface TipShift {
    id: string
    tenantId: string
    userId: string
    name: string
    closedByUserId: string | null       // terminal account
    cashSessionId: string | null        // drawer the tips came out of (drawer_at_close only)
    businessDate: string                // yyyy-MM-dd, tenant calendar date
    closedAt: string
    tipsEarnedCents: number
    cardTipsCents: number
    cashTipsDeclaredCents: number
    tipsFromDrawerCents: number
    houseOwesCents: number
    note: string | null
}

// ── Tip jar (cash outside the register, split and taken right after the shift) ──────────
export interface TipJarStaff { userId: string; name: string }
export interface TipJarShare { userId: string; name: string; amountCents: number }
export interface TipJarSplit {
    id: string
    totalCents: number
    note: string | null
    createdByName: string
    createdAtUtc: string
    shares: TipJarShare[]
}
export interface TipJarResponse {
    date: string
    suggestedUserIds: string[]          // people with a shift report that day
    staff: TipJarStaff[]                // every counter staff member
    splits: TipJarSplit[]
}
export interface RecordTipJarRequest {
    date: string                        // yyyy-MM-dd
    totalCents: number
    userIds: string[]                   // order matters: remainder cents go to the first ones
    note: string
}

export type TipPayoutSource = 'drawer' | 'cash' | 'payroll'

export interface TipsOwedRow {
    userId: string
    name: string
    owedCents: number
    lastShiftAtUtc: string | null
    earnedTotalCents: number
    paidFromDrawerCents: number
    paidByManagerCents: number
    unclaimedTipsCents: number      // tips on paid sales not yet settled on any turn-in
}

export interface TipPayout {
    id: string
    userId: string
    name: string
    amountCents: number
    source: TipPayoutSource
    paidByName: string              // "(removed user)" once the paying manager's account is gone
    note: string | null
    createdAtUtc: string
}

export interface TipsOwedResponse {
    rows: TipsOwedRow[]
    payouts: TipPayout[]            // last 90 days, newest first, capped at 500 rows
    paidByManagerLast90Cents: number // exact server-side sum, never affected by the cap
    payoutsTruncated: boolean
}

export interface PayTipsOwedRequest {
    userId: string
    amountCents: number
    source: 'cash' | 'payroll'
    note: string
}

// ── Shift math (pure; the server re-derives the same figures and is the authority) ─────
// What the worker takes from the drawer can never exceed what they earned this shift.
export function clampTipsFromDrawer(tipsFromDrawerCents: number, tipsEarnedCents: number): number {
    if (!Number.isFinite(tipsFromDrawerCents)) return 0
    return Math.min(Math.max(0, Math.round(tipsFromDrawerCents)), Math.max(0, tipsEarnedCents))
}
// The part of this shift's tips the drawer did not cover; a manager settles it later.
export function houseOwesCents(tipsEarnedCents: number, tipsFromDrawerCents: number): number {
    return Math.max(0, tipsEarnedCents - clampTipsFromDrawer(tipsFromDrawerCents, tipsEarnedCents))
}
// Even tip-jar split, same rule as the server: whole cents each, and the remainder goes one cent
// each to the first people in the order given. [] when there is nobody to split between.
export function splitEvenly(totalCents: number, count: number): number[] {
    if (count <= 0 || !Number.isFinite(totalCents) || totalCents <= 0) return Array(Math.max(0, count)).fill(0)
    const each = Math.floor(totalCents / count)
    const remainder = totalCents - each * count
    return Array.from({ length: count }, (_, i) => each + (i < remainder ? 1 : 0))
}

export class CashService {
    private apiUrl: string
    constructor() {
        // Same VITE_API_ENDPOINT every service reads. The older services access import.meta.env
        // directly and each pays a pre-existing vue-tsc error for it (ImportMeta has no `env` in
        // this tsconfig); the cast keeps this file from adding another one.
        const env = (import.meta as any).env as Record<string, string | undefined> | undefined
        this.apiUrl = env?.VITE_API_ENDPOINT ?? ''
    }

    // ── Worker ───────────────────────────────────────────────────────
    // Get-or-create: re-opening returns the existing open session for this worker + event.
    openSession(req: OpenCashSessionRequest) {
        return axios.post<{ data: CashSession }>(`${this.apiUrl}/Cash/Session/Open`, req)
    }
    // null data = no open session (a normal state, not an error).
    currentSession(eventId: string | null = null) {
        return axios.get<{ data: CashSession | null }>(`${this.apiUrl}/Cash/Session/Current`, {
            params: { eventId: eventId || undefined },
        })
    }
    // With a cashier token the review is for the token's person; without, the signed-in account.
    shiftReview(cashierToken: string | null = null, eventId: string | null = null) {
        // The token travels as a header, never in the query string: a 14-hour attribution credential
        // must not land in access logs, proxies or the tablet's browser history.
        return axios.get<{ data: ShiftReview }>(`${this.apiUrl}/Cash/ShiftReview`, {
            params: { eventId: eventId || undefined },
            headers: cashierToken ? { 'X-Cashier-Token': cashierToken } : undefined,
        })
    }
    // Claims the person's unsettled tips and writes their shift report in one transaction. A 400
    // carrying fresh `data.tipsEarnedCents` means the figures moved mid-dialog: reload and retry.
    shiftClose(req: ShiftCloseRequest) {
        return axios.post<{ data: TipShift }>(`${this.apiUrl}/Cash/ShiftClose`, req)
    }
    turnIn(req: SubmitTurnInRequest) {
        return axios.post<{ data: CashTurnIn }>(`${this.apiUrl}/Cash/TurnIn`, req)
    }

    // ── Manager ──────────────────────────────────────────────────────
    tipsOwed() {
        return axios.get<{ data: TipsOwedResponse }>(`${this.apiUrl}/Cash/TipsOwed`)
    }
    // Returns the recipient's updated TipsOwed row.
    payTipsOwed(req: PayTipsOwedRequest) {
        return axios.post<{ data: TipsOwedRow }>(`${this.apiUrl}/Cash/TipsOwed/Pay`, req)
    }
    // Tip jar for one tenant calendar date (yyyy-MM-dd): who worked, all staff, the day's splits.
    tipJar(date: string) {
        return axios.get<{ data: TipJarResponse }>(`${this.apiUrl}/Cash/TipJar`, { params: { date } })
    }
    // Records an even split; no ledger row or liability (the cash was already handed out).
    recordTipJar(req: RecordTipJarRequest) {
        return axios.post<{ data: TipJarSplit }>(`${this.apiUrl}/Cash/TipJar`, req)
    }
    deleteTipJar(id: string) {
        return axios.delete(`${this.apiUrl}/Cash/TipJar/${id}`)
    }
}
