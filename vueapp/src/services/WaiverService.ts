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
}
