import axios from 'axios'

export interface AutomationListItem {
    id: string
    name: string
    triggerKind: string
    fromProductId: string | null
    fromProductName: string | null
    isActive: boolean
    stepCount: number
    /** Delay on the first email, so the list can say "30 days after purchase". */
    firstDelayDays: number | null
    sent: number
    failed: number
    skipped: number
    conversions: number
    enrolFromUtc: string | null
    updatedAt: string
}

export interface AutomationStepItem {
    id: string
    stepOrder: number
    delayDays: number
    subject: string
    bodyHtml: string
    bodyText: string | null
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
    delayDays: number
    subject: string
    bodyHtml: string
    bodyText?: string | null
}

export interface UpsertAutomationRequest {
    name: string
    fromProductId: string | null
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

    /** Pass products for the trigger select, reachable with campaigns.manage alone. */
    products() {
        return axios.get<{ data: { id: string; name: string; isActive: boolean }[] }>(
            `${this.apiUrl}/Automation/Products`)
    }

    mergeFields() {
        return axios.get<{ data: MergeFieldItem[] }>(`${this.apiUrl}/Automation/MergeFields`)
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

    testSend(id: string, stepIndex: number, toEmail: string) {
        return axios.post<{ data: { usedRealPass: boolean; sampleProduct: string | null } }>(
            `${this.apiUrl}/Automation/${id}/TestSend`, { stepIndex, toEmail })
    }

    forUpgrades() {
        return axios.get<{ data: UpgradeAutomationStatus[] }>(`${this.apiUrl}/Automation/ForUpgrades`)
    }
}
