import axios from 'axios'

// Cash sessions, blind-count turn-ins and the F&B tips flow (shift review, house-owes, manager
// pay-out). Mirrors webapi/Controllers/CashController.cs; every call is tenant-scoped server side.

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
    // tipsEarnedCents is every tip not yet settled on a turn-in. A tab opened on an earlier
    // shift and closed after it was turned in lands here; this is that older part.
    tipsFromEarlierShiftsCents: number
}

export interface OpenCashSessionRequest {
    eventId: string | null
    openingFloatCents: number
    deviceId?: string | null
}

export interface SubmitTurnInRequest {
    eventId: string | null
    workerCountedCents: number
    note: string
    tipsFromDrawerCents: number
    cashTipsDeclaredCents: number
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
    shiftReview(eventId: string | null = null) {
        return axios.get<{ data: ShiftReview }>(`${this.apiUrl}/Cash/ShiftReview`, {
            params: { eventId: eventId || undefined },
        })
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
}
