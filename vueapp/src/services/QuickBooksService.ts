import axios from 'axios'

export interface QuickBooksStatus {
    // False when this RidePass deployment has no Intuit app credentials at all (Online only).
    isConfigured: boolean
    // Which QuickBooks the track uses; null until they pick one.
    edition: 'online' | 'desktop' | null
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
    // Desktop only.
    listsUploadedAtUtc: string | null
    companyProduct: string | null
    accountCount: number
    classCount: number
    // Once any file has been exported the start date is fixed.
    hasExported: boolean
}

/** What an uploaded QuickBooks Desktop list file replaced (null = that list was not in the file). */
export interface QbdListUploadResult {
    accountCount: number | null
    classCount: number | null
    product: string | null
    // Mapped names the new lists no longer contain; they will block the next export.
    missingMappedNames: string[]
}

export interface QbdExportResult {
    // Null when there was nothing new to export.
    batchId: string | null
    daysInFile: number
    daysWithNoActivity: number
    fromDate: string | null
    toDate: string | null
    lastCompleteDay: string
    moreRemaining: boolean
}

export interface QbdExportBatch {
    id: string
    fromDate: string
    toDate: string
    dayCount: number
    totalDebitsCents: number
    downloadCount: number
    lastDownloadedAtUtc: string | null
    voidedAtUtc: string | null
    createdAt: string
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
    // The QuickBooks Desktop export file carrying this day; null for Online posts.
    exportBatchId: string | null
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

    // ── QuickBooks Desktop ───────────────────────────────────────────────

    /** Switch this track to QuickBooks Desktop. Omit the date for today at the track. */
    desktopSetup(syncStartDate: string | null) {
        return axios.post(`${this.apiUrl}/QuickBooks/Desktop/Setup`, { syncStartDate })
    }

    desktopSetStartDate(syncStartDate: string) {
        return axios.put(`${this.apiUrl}/QuickBooks/Desktop/StartDate`, { syncStartDate })
    }

    desktopRemove() {
        return axios.delete(`${this.apiUrl}/QuickBooks/Desktop/Setup`)
    }

    /** The .IIF file from QuickBooks Desktop's File > Utilities > Export > Lists to IIF Files. */
    desktopUploadLists(file: File) {
        const form = new FormData()
        form.append('file', file)
        return axios.post<{ data: QbdListUploadResult }>(`${this.apiUrl}/QuickBooks/Desktop/Lists`, form)
    }

    desktopExport() {
        return axios.post<{ data: QbdExportResult }>(`${this.apiUrl}/QuickBooks/Desktop/Export`)
    }

    desktopBatches() {
        return axios.get<{ data: QbdExportBatch[] }>(`${this.apiUrl}/QuickBooks/Desktop/Batches`)
    }

    // Through axios so the Bearer token rides along; a plain <a href> would 401.
    async desktopDownload(batchId: string): Promise<{ blob: Blob; filename: string }> {
        const r = await axios.get(`${this.apiUrl}/QuickBooks/Desktop/Batches/${batchId}/File`, { responseType: 'blob' })
        const cd = (r.headers['content-disposition'] as string | undefined) ?? ''
        const filename = cd.match(/filename="?([^";]+)"?/)?.[1] ?? 'RidePass-QuickBooks.iif'
        return { blob: r.data as Blob, filename }
    }

    desktopVoid(batchId: string) {
        return axios.post(`${this.apiUrl}/QuickBooks/Desktop/Batches/${batchId}/Void`)
    }
}
