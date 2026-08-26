import axios from 'axios'

export interface QuickBooksStatus {
    // False when this RidePass deployment has no Intuit app credentials at all.
    isConfigured: boolean
    isConnected: boolean
    status: 'active' | 'expired' | 'revoked' | 'error' | null
    realmId: string | null
    companyName: string | null
    syncEnabled: boolean
    syncStartDate: string | null
    lastSyncedDate: string | null
    lastSyncAtUtc: string | null
    lastSyncError: string | null
    connectedAtUtc: string | null
    mappingComplete: boolean
    unmappedKeys: string[]
}

export interface QboAccount {
    id: string
    name: string
    accountType: string
    accountSubType: string | null
    // Revenue | Asset | Liability | Expense, used to filter each slot's dropdown.
    classification: string | null
}

export interface QboMapping {
    mappingKey: string
    label: string
    expectedClassification: string
    qboAccountId: string | null
    qboAccountName: string | null
}

/** One Class in the tenant's QuickBooks company. */
export interface QboClass {
    id: string
    name: string
    /** "Parent:Child" when nested; what the dropdown shows. */
    fullyQualifiedName: string
}

export interface QboClassSettings {
    /** False when the company has class tracking switched off in QuickBooks entirely. */
    trackingEnabled: boolean
    /** False when the company tracks one class per transaction rather than per line. */
    trackingPerLine: boolean
    classes: QboClass[]
}

/** One reporting bucket (profit center or built-in department) and the class it posts under. */
export interface QboClassMapping {
    bucketKey: string
    label: string
    color: string
    isCustom: boolean
    revenueStreams: string[]
    qboClassId: string | null
    qboClassName: string | null
}

export interface QboSyncLogRow {
    businessDate: string
    status: 'success' | 'failed' | 'no_activity'
    qboJournalEntryId: string | null
    qboDocNumber: string | null
    entryCount: number
    totalDebitsCents: number
    attemptCount: number
    lastError: string | null
    syncedAtUtc: string | null
}

export class QuickBooksService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    status() {
        return axios.get<{ data: QuickBooksStatus }>(`${this.apiUrl}/QuickBooks/Status`)
    }

    /** Returns the Intuit consent URL; the caller navigates the browser to it. */
    connect() {
        return axios.post<{ data: { authorizationUrl: string } }>(`${this.apiUrl}/QuickBooks/Connect`)
    }

    disconnect() {
        return axios.delete(`${this.apiUrl}/QuickBooks/Connect`)
    }

    setSyncEnabled(enabled: boolean) {
        return axios.put(`${this.apiUrl}/QuickBooks/SyncEnabled`, enabled, {
            headers: { 'Content-Type': 'application/json' },
        })
    }

    accounts() {
        return axios.get<{ data: QboAccount[] }>(`${this.apiUrl}/QuickBooks/Accounts`)
    }

    mappings() {
        return axios.get<{ data: QboMapping[] }>(`${this.apiUrl}/QuickBooks/Mappings`)
    }

    saveMappings(mappings: { mappingKey: string; qboAccountId: string | null; qboAccountName: string | null }[]) {
        return axios.put(`${this.apiUrl}/QuickBooks/Mappings`, { mappings })
    }

    /** The company's classes plus its class-tracking preference, in one call. */
    classes() {
        return axios.get<{ data: QboClassSettings }>(`${this.apiUrl}/QuickBooks/Classes`)
    }

    classMappings() {
        return axios.get<{ data: QboClassMapping[] }>(`${this.apiUrl}/QuickBooks/ClassMappings`)
    }

    saveClassMappings(mappings: { bucketKey: string; qboClassId: string | null; qboClassName: string | null }[]) {
        return axios.put(`${this.apiUrl}/QuickBooks/ClassMappings`, { mappings })
    }

    syncLog(take = 60) {
        return axios.get<{ data: QboSyncLogRow[] }>(`${this.apiUrl}/QuickBooks/SyncLog`, { params: { take } })
    }

    /** Catch up every outstanding day now rather than waiting for the nightly sweep. */
    syncNow() {
        return axios.post<{ data: { posted: number; skipped: number } }>(`${this.apiUrl}/QuickBooks/Sync`)
    }

    resync(businessDate: string) {
        return axios.post<{ data: { status: string; journalEntryId: string | null } }>(
            `${this.apiUrl}/QuickBooks/Resync`, { businessDate })
    }
}
