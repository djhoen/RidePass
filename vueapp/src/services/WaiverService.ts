import axios from 'axios'

export interface WaiverDto {
    id: string
    version: number
    name: string
    title: string
    body: string
    isActive: boolean
    expiresAtUtc: string | null
}

export interface UpsertWaiverRequest {
    name: string
    title: string
    body: string
    isActive: boolean
    expiresAtUtc: string | null
}

export interface WaiverSignatureStatus {
    hasSignedCurrent: boolean
    signatureId: string | null
    signedAt: string | null
    currentVersion: number
    signatureDataUrl: string | null
    riderIsMinor: boolean
    signedByParent: boolean
    parentName: string | null
    parentPhone: string | null
    riderHasEmergencyContact: boolean
}

export interface SignWaiverRequest {
    signatureDataUrl: string
    parentName?: string | null
    parentPhone?: string | null
}

export interface WaiverEventAssociation {
    id: string
    title: string
    startsAtUtc: string
    endsAtUtc: string
    asRider: boolean
    asSpectator: boolean
}

export interface AdminWaiverSignatureItem {
    id: string
    signedAtUtc: string
    userId: string | null
    signerName: string | null
    signerEmail: string | null
    birthdate: string | null
    signedByParent: boolean
    parentName: string | null
    parentPhone: string | null
    waiverName: string
    waiverVersion: number
    waiverIsCurrent: boolean
    context: 'ticket' | 'rental' | 'account'
}

export interface AdminWaiverSignaturesPage {
    items: AdminWaiverSignatureItem[]
    total: number
    page: number
    pageSize: number
}

export interface AdminWaiverPersonItem {
    personKey: string
    userId: string | null
    personName: string
    personEmail: string | null
    birthdate: string | null
    isMinor: boolean
    agingOutSoon: boolean
    hasGuardianSignature: boolean
    guardianName: string | null
    guardianPhone: string | null
    lastSignedAtUtc: string
    signatureCount: number
    hasCurrentWaiver: boolean
}

export interface AdminWaiverPeoplePage {
    items: AdminWaiverPersonItem[]
    total: number
    page: number
    pageSize: number
}

export interface AdminWaiverSignatureDetail {
    id: string
    signedAtUtc: string
    userId: string | null
    signerName: string | null
    signerEmail: string | null
    birthdate: string | null
    signedByParent: boolean
    parentName: string | null
    parentPhone: string | null
    ipAddress: string | null
    signatureDataUrl: string | null
    waiverName: string
    waiverTitle: string
    waiverVersion: number
    emergencyContactName: string | null
    emergencyContactPhone: string | null
    ticketEventTitle: string | null
    rentalLabel: string | null
}

export interface ListSignaturesParams {
    search?: string
    fromUtc?: string
    toUtc?: string
    waiverId?: string
    minorsOnly?: boolean
    context?: string
    page?: number
    pageSize?: number
}

export interface ListPeopleParams {
    search?: string
    status?: string
    agingOut?: boolean
    minorsOnly?: boolean
    page?: number
    pageSize?: number
}

export interface WaiverComplianceItem {
    source: 'scan' | 'pass' | 'rental' | 'lesson'
    label: string
    personName: string
    email: string | null
    atUtc: string
    waiverStatus: 'signed' | 'missing'
}

export interface WaiverComplianceResponse {
    items: WaiverComplianceItem[]
    totalOnSite: number
    missingCount: number
}

export interface WaiverSignRequestItem {
    id: string
    recipientEmail: string
    recipientName: string | null
    waiverName: string | null
    waiverVersion: number | null
    eventTitle: string | null
    status: 'pending' | 'sent' | 'opened' | 'signed' | 'cancelled'
    createdAtUtc: string
    sentAtUtc: string | null
    openedAtUtc: string | null
    signedAtUtc: string | null
    link: string
}

export interface WaiverSignRequestsPage {
    items: WaiverSignRequestItem[]
    total: number
    page: number
    pageSize: number
}

export interface BulkSignRequestResult {
    created: number
    alreadyCovered: number
    emailFailures: number
}

export interface PublicSignRequestInfo {
    status: string
    recipientName: string | null
    recipientEmail: string
    waiverId: string
    waiverName: string
    waiverTitle: string
    waiverBody: string
    waiverVersion: number
    alreadySigned: boolean
}

export class WaiverService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    // Admin CRUD
    listAdmin() {
        return axios.get<{ data: WaiverDto[] }>(`${this.apiUrl}/Waiver/Admin`)
    }
    create(req: UpsertWaiverRequest) {
        return axios.post<{ data: WaiverDto }>(`${this.apiUrl}/Waiver`, req)
    }
    update(id: string, req: UpsertWaiverRequest) {
        return axios.put<{ data: WaiverDto }>(`${this.apiUrl}/Waiver/${id}`, req)
    }

    // Read
    getActive() {
        return axios.get<{ data: WaiverDto }>(`${this.apiUrl}/Waiver`)
    }
    getById(id: string) {
        return axios.get<{ data: WaiverDto }>(`${this.apiUrl}/Waiver/${id}`)
    }

    // Signatures
    getMySignatureFor(id: string) {
        return axios.get<{ data: WaiverSignatureStatus }>(`${this.apiUrl}/Waiver/${id}/MySignature`)
    }
    sign(id: string, req: SignWaiverRequest) {
        return axios.post<{ data: WaiverSignatureStatus }>(`${this.apiUrl}/Waiver/${id}/Sign`, req)
    }

    // Associated events
    listAssociatedEvents(id: string) {
        return axios.get<{ data: WaiverEventAssociation[] }>(`${this.apiUrl}/Waiver/${id}/Events`)
    }
    setEventRole(id: string, eventId: string, asRider: boolean, asSpectator: boolean) {
        return axios.put(`${this.apiUrl}/Waiver/${id}/Events/${eventId}`, { asRider, asSpectator })
    }

    // Admin: Signed Waivers log / People view
    listSignatures(params: ListSignaturesParams) {
        return axios.get<{ data: AdminWaiverSignaturesPage }>(`${this.apiUrl}/Waiver/Signatures`, { params })
    }
    listPeople(params: ListPeopleParams) {
        return axios.get<{ data: AdminWaiverPeoplePage }>(`${this.apiUrl}/Waiver/People`, { params })
    }
    getSignatureDetail(id: string) {
        return axios.get<{ data: AdminWaiverSignatureDetail }>(`${this.apiUrl}/Waiver/Signatures/${id}`)
    }

    // Admin: Compliance Today
    complianceToday() {
        return axios.get<{ data: WaiverComplianceResponse }>(`${this.apiUrl}/Waiver/Compliance/Today`)
    }

    // Admin: signature requests
    listSignRequests(params: { search?: string; status?: string; page?: number; pageSize?: number }) {
        return axios.get<{ data: WaiverSignRequestsPage }>(`${this.apiUrl}/Waiver/SignRequests`, { params })
    }
    createSignRequest(req: { email: string; name?: string | null; waiverId?: string | null }) {
        return axios.post<{ data: WaiverSignRequestItem }>(`${this.apiUrl}/Waiver/SignRequests`, req)
    }
    createBulkSignRequests(eventId: string) {
        return axios.post<{ data: BulkSignRequestResult }>(`${this.apiUrl}/Waiver/SignRequests/Bulk`, { eventId })
    }
    resendSignRequest(id: string) {
        return axios.post<{ data: WaiverSignRequestItem }>(`${this.apiUrl}/Waiver/SignRequests/${id}/Resend`)
    }
    cancelSignRequest(id: string) {
        return axios.post(`${this.apiUrl}/Waiver/SignRequests/${id}/Cancel`)
    }

    // Public signing link (token is the credential)
    getSignRequestByToken(token: string) {
        return axios.get<{ data: PublicSignRequestInfo }>(`${this.apiUrl}/Waiver/SignRequest/${token}`)
    }
    signByToken(token: string, req: {
        firstName: string; lastName: string; birthdate?: string | null
        signatureDataUrl: string; parentName?: string | null; parentPhone?: string | null
    }) {
        return axios.post(`${this.apiUrl}/Waiver/SignRequest/${token}/Sign`, req)
    }
}
