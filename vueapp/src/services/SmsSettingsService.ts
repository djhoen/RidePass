import axios from 'axios'

export interface SmsStatus {
    enabled: boolean
    hasProvisionedNumber: boolean
    phoneNumber: string | null
    enabledAtUtc: string | null
    masterConfigured: boolean
    outboundPerSegmentCents: number
}

export interface AvailableNumber {
    phoneNumber: string
    friendlyName: string
    region: string
    isoCountry: string
}

export class SmsSettingsService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    status() {
        return axios.get<{ data: SmsStatus }>(`${this.apiUrl}/SmsSettings/Status`)
    }

    search(areaCode: string | null, max = 10) {
        const params: Record<string, string | number> = { max }
        if (areaCode) params.areaCode = areaCode
        return axios.get<{ data: AvailableNumber[] }>(`${this.apiUrl}/SmsSettings/Search`, { params })
    }

    provision(phoneNumber: string) {
        return axios.post<{ data: { phoneNumber: string } }>(
            `${this.apiUrl}/SmsSettings/Provision`, { phoneNumber })
    }

    enable() {
        return axios.post(`${this.apiUrl}/SmsSettings/Enable`)
    }

    disable() {
        return axios.post(`${this.apiUrl}/SmsSettings/Disable`)
    }

    release() {
        return axios.post<{ data: { released: boolean } }>(`${this.apiUrl}/SmsSettings/Release`)
    }

    getVerification() {
        return axios.get<{ data: TollfreeVerification }>(`${this.apiUrl}/TollfreeVerification`)
    }

    saveVerification(payload: TollfreeVerificationDraft) {
        return axios.put<{ data: TollfreeVerification }>(`${this.apiUrl}/TollfreeVerification`, payload)
    }

    submitVerification() {
        return axios.post<{ data: TollfreeVerification }>(`${this.apiUrl}/TollfreeVerification/Submit`)
    }

    refreshVerification() {
        return axios.post<{ data: TollfreeVerification }>(`${this.apiUrl}/TollfreeVerification/RefreshStatus`)
    }
}

export interface TollfreeVerificationDraft {
    businessName: string | null
    businessWebsite: string | null
    businessStreetAddress: string | null
    businessCity: string | null
    businessStateProvinceRegion: string | null
    businessPostalCode: string | null
    businessCountry: string | null
    businessContactFirstName: string | null
    businessContactLastName: string | null
    businessContactEmail: string | null
    businessContactPhone: string | null
    notificationEmail: string | null
    useCaseCategories: string[]
    useCaseSummary: string | null
    productionMessageSamples: string[]
    optInType: string | null
    optInImageUrls: string[]
    messageVolume: string | null
    additionalInformation: string | null
}

export interface TollfreeVerification extends TollfreeVerificationDraft {
    // Server-managed lifecycle fields. Status null = draft never submitted.
    status: string | null
    rejectionReason: string | null
    lastSubmittedAtUtc: string | null
    lastStatusCheckedAtUtc: string | null
}
