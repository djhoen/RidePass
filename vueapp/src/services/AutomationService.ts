import axios from 'axios'
import type { MessageChannel } from './CampaignService'

export type AutomationTriggerKind = 'season_pass_purchased' | 'event_ticket_purchased' | 'newsletter_subscribed' | 'audience_joined'
export type AutomationAnchor = 'purchase' | 'event_start' | 'event_end' | 'pass_expiry' | 'fixed_date'

export interface AutomationListItem {
    id: string
    name: string
    triggerKind: AutomationTriggerKind
    /** "Buys a ticket to Spring Camp", "Buys any pass". */
    triggerLabel: string
    fromProductId: string | null
    fromProductName: string | null
    eventId: string | null
    eventTypeId: string | null
    /** Audience trigger: the saved audience. */
    audienceId: string | null
    isActive: boolean
    stepCount: number
    /** Delay on the first email when it counts from the purchase; kept for the upgrades panel. */
    firstDelayDays: number | null
    /** "7 days before the event starts", ready for the list. */
    firstStepLabel: string | null
    sent: number
    failed: number
    skipped: number
    /** Texts delivered, a subset of sent. */
    smsSent: number
    conversions: number
    /** Distinct sends opened / clicked across every step; opens are a ceiling. */
    uniqueOpens: number
    uniqueClicks: number
    enrolFromUtc: string | null
    updatedAt: string
}

export interface AutomationStepItem {
    id: string
    stepOrder: number
    delayDays: number
    anchor: AutomationAnchor
    /** Signed days from the anchor; negative = before. */
    offsetDays: number
    /** yyyy-MM-dd for a fixed_date step. */
    sendOn: string | null
    label: string
    subject: string
    bodyHtml: string
    bodyText: string | null
    previewText: string | null
    channel: MessageChannel
    smsBody: string | null
    sent: number
    failed: number
    skipped: number
    /** Texts delivered, a subset of sent. */
    smsSent: number
    /** People who bought a ticket or a pass within a week of this email. */
    conversions: number
    revenueCents: number
    lastSentAtUtc: string | null
    skipReasons: { status: 'skipped' | 'failed'; reason: string; count: number }[]
    uniqueOpens: number
    uniqueClicks: number
}

export interface AutomationDetail extends AutomationListItem {
    stopOnUpgrade: boolean
    stopWhenUsedUp: boolean
    /** "09:00", track local. Null in both means any hour. */
    sendWindowStart: string | null
    sendWindowEnd: string | null
    steps: AutomationStepItem[]
}

export interface UpsertAutomationStep {
    /** Existing step id when editing, so its send history is kept; omit for a new email. */
    id?: string | null
    anchor: AutomationAnchor
    offsetDays: number
    sendOn: string | null
    subject: string
    bodyHtml: string
    bodyText?: string | null
    previewText?: string | null
    channel?: MessageChannel
    smsBody?: string | null
}

export interface UpsertAutomationRequest {
    name: string
    triggerKind: AutomationTriggerKind
    fromProductId: string | null
    eventId: string | null
    eventTypeId: string | null
    audienceId: string | null
    stopOnUpgrade: boolean
    stopWhenUsedUp: boolean
    sendWindowStart: string | null
    sendWindowEnd: string | null
    steps: UpsertAutomationStep[]
}

/** What turning it on would cost, shown before the confirm. */
export interface AutomationEstimate {
    backlogCount: number
    backlogChargeCents: number
    last30DayRate: number
    ongoingChargeCents: number
}

export interface MergeFieldItem {
    token: string
    description: string
}

export interface AutomationAnchorOption {
    value: AutomationAnchor
    /** The phrase after "N days before/after": "they buy", "the event starts". */
    phrase: string
}

export interface AutomationTriggerOption {
    kind: AutomationTriggerKind
    label: string
    anchors: AutomationAnchorOption[]
    mergeFields: MergeFieldItem[]
}

export interface AutomationTriggerOptions {
    triggers: AutomationTriggerOption[]
    events: { id: string; title: string; startsAtUtc: string; status: string; eventTypeName: string }[]
    eventTypes: { id: string; name: string; isActive: boolean }[]
    passProducts: { id: string; name: string; isActive: boolean }[]
    /** Saved audiences, for the "joins an audience" trigger. */
    audiences: { id: string; name: string; isActive: boolean }[]
}

export interface TestSendResponse {
    usedRealSubject: boolean
    emailSent: boolean
    smsSent: boolean
    sampleName: string | null
    wouldSendOn: string | null
    wouldSkip: boolean
}

/** Backing data for the "is anyone being told about this?" panel on Pass Upgrades. */
export interface UpgradeAutomationStatus {
    fromProductId: string | null
    automationId: string
    name: string
    isActive: boolean
    firstDelayDays: number | null
    sent: number
    conversions: number
}

export class AutomationService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    list() {
        return axios.get<{ data: AutomationListItem[] }>(`${this.apiUrl}/Automation`)
    }

    get(id: string) {
        return axios.get<{ data: AutomationDetail }>(`${this.apiUrl}/Automation/${id}`)
    }

    /** Triggers, anchors, merge fields, and the events / event types / pass products to target. */
    triggerOptions() {
        return axios.get<{ data: AutomationTriggerOptions }>(`${this.apiUrl}/Automation/TriggerOptions`)
    }

    /** Pass products for the trigger select, reachable with campaigns.manage alone. */
    products() {
        return axios.get<{ data: { id: string; name: string; isActive: boolean }[] }>(
            `${this.apiUrl}/Automation/Products`)
    }

    mergeFields(triggerKind?: AutomationTriggerKind) {
        return axios.get<{ data: MergeFieldItem[] }>(`${this.apiUrl}/Automation/MergeFields`,
            { params: triggerKind ? { triggerKind } : undefined })
    }

    create(req: UpsertAutomationRequest) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/Automation`, req)
    }

    update(id: string, req: UpsertAutomationRequest) {
        return axios.put(`${this.apiUrl}/Automation/${id}`, req)
    }

    remove(id: string) {
        return axios.delete(`${this.apiUrl}/Automation/${id}`)
    }

    estimate(id: string, newPurchasesOnly: boolean) {
        return axios.get<{ data: AutomationEstimate }>(
            `${this.apiUrl}/Automation/${id}/Estimate`, { params: { newPurchasesOnly } })
    }

    activate(id: string, isActive: boolean, newPurchasesOnly: boolean) {
        return axios.post(`${this.apiUrl}/Automation/${id}/Activate`, { isActive, newPurchasesOnly })
    }

    testSend(id: string, stepIndex: number, toEmail: string | null, toPhone: string | null = null) {
        return axios.post<{ data: TestSendResponse }>(
            `${this.apiUrl}/Automation/${id}/TestSend`, { stepIndex, toEmail, toPhone })
    }

    forUpgrades() {
        return axios.get<{ data: UpgradeAutomationStatus[] }>(`${this.apiUrl}/Automation/ForUpgrades`)
    }
}
