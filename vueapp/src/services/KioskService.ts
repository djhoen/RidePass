import axios from 'axios'
import tenantHelper from '@/helpers/TenantHelper'

// ── The kiosk itself (Script0310) ────────────────────────────────────────────────────────────────
// The kiosk runs with NO staff session: its own axios instance (no app interceptors, so no staff JWT is
// ever attached) carrying only the tenant and the kiosk's secret.

// The API base, read once (typed loosely: this project's env typings are not set up for vue-tsc).
const API_BASE: string = (import.meta as any).env?.VITE_API_ENDPOINT ?? ''

const KIOSK_TOKEN_KEY = 'rp-kiosk-token'
const KIOSK_NAME_KEY = 'rp-kiosk-name'

export function getKioskToken(): string | null {
    try { return localStorage.getItem(KIOSK_TOKEN_KEY) } catch { return null }
}
export function getKioskName(): string | null {
    try { return localStorage.getItem(KIOSK_NAME_KEY) } catch { return null }
}
export function saveKioskPairing(token: string, name: string) {
    try {
        localStorage.setItem(KIOSK_TOKEN_KEY, token)
        localStorage.setItem(KIOSK_NAME_KEY, name)
    } catch { /* storage blocked: the kiosk will ask to be paired again */ }
}
export function forgetKioskPairing() {
    try {
        localStorage.removeItem(KIOSK_TOKEN_KEY)
        localStorage.removeItem(KIOSK_NAME_KEY)
    } catch { /* nothing to clear */ }
}

export interface KioskConfig {
    kioskName: string
    venueName: string
    waiverId: string | null
    waiverTitle: string | null
    waiverBody: string | null
    photoMode: 'off' | 'prompt' | 'required'
    photoRequired: boolean
    requireEmergencyContact: boolean
    /** The venue's date (yyyy-MM-dd): under-18 is judged on it, as at the gate. */
    today: string
}

export interface KioskSignupBody {
    firstName: string
    lastName: string
    email: string | null
    phone: string | null
    birthdate: string
    guardianFirstName?: string | null
    guardianLastName?: string | null
    guardianEmail?: string | null
    guardianPhone?: string | null
    guardianBirthdate?: string | null
    emergencyContactName?: string | null
    emergencyContactPhone?: string | null
    /** The waiver shown: the server refuses if it is no longer the active one. */
    waiverId: string | null
    agreedToWaiver: boolean
    signatureDataUrl: string | null
    photoDataUrl: string | null
}

function kioskClient() {
    const headers: Record<string, string> = {}
    const subdomain = tenantHelper.getSubdomain()
    if (subdomain) headers['X-Tenant-Subdomain'] = subdomain
    const token = getKioskToken()
    if (token) headers['X-Kiosk-Token'] = token
    return axios.create({ baseURL: API_BASE, headers })
}

export class KioskDeviceApi {
    pair(code: string) {
        return kioskClient().post<{ data: { token: string; kioskName: string } }>('/Kiosk/Pair', { code })
    }
    config() {
        return kioskClient().get<{ data: KioskConfig }>('/Kiosk/Config')
    }
    signup(body: KioskSignupBody) {
        return kioskClient().post<{ data: { checkedIn: boolean } }>('/Kiosk/Signup', body)
    }
    /** A step staff pushed to this kiosk (asked only while idle), or null. */
    pollPush() {
        return kioskClient().get<{ data: KioskPushPayload | null }>('/Kiosk/Push')
    }
    startPush(id: string) {
        return kioskClient().post(`/Kiosk/Push/${id}/Start`)
    }
    completePush(id: string, body: KioskPushCompleteBody) {
        return kioskClient().post<{ data: { done: boolean } }>(`/Kiosk/Push/${id}/Complete`, body)
    }
}

/** What a pushed step shows: first name only, the steps, the signing mode and the waiver text. */
export interface KioskPushPayload {
    pushId: string
    firstName: string
    wantWaiver: boolean
    wantPhoto: boolean
    signAs: 'guardian' | 'adult' | null
    waiverId: string | null
    waiverTitle: string | null
    waiverBody: string | null
    expiresAtUtc: string
}

export interface KioskPushCompleteBody {
    waiverId: string | null
    agreedToWaiver: boolean
    signatureDataUrl: string | null
    guardianName: string | null
    guardianPhone: string | null
    photoDataUrl: string | null
}

// ── Staff: send a waiver / photo step to a kiosk (Script0311) ───────────────────────────────────

export interface ActiveKiosk {
    id: string
    name: string
    lastSeenAtUtc: string | null
    idle: boolean
    busy: boolean
    activePushId: string | null
}

export type KioskPushTarget =
    | { targetKind: 'ticket'; purchaseId: string }
    | { targetKind: 'season_pass'; purchaseId: string; eventId?: string | null }
    | { targetKind: 'kiosk_signup'; kioskSignupId: string }

export interface KioskPushStatus {
    id: string
    status: 'pending' | 'in_progress' | 'completing' | 'done' | 'cancelled' | 'expired'
    kioskName: string
    kioskIdle: boolean
    riderFirstName: string
    wantWaiver: boolean
    wantPhoto: boolean
    expiresAtUtc: string
    completedAtUtc: string | null
}

export class KioskPushService {
    private apiUrl = API_BASE

    activeKiosks() { return axios.get<{ data: ActiveKiosk[] }>(`${this.apiUrl}/KioskPushes/Kiosks`) }
    send(kioskDeviceId: string, target: KioskPushTarget, waiver: boolean, photo: boolean) {
        return axios.post<{ data: KioskPushStatus }>(`${this.apiUrl}/KioskPushes`, { kioskDeviceId, ...target, waiver, photo })
    }
    status(id: string) { return axios.get<{ data: KioskPushStatus }>(`${this.apiUrl}/KioskPushes/${id}`) }
    cancel(id: string) { return axios.post(`${this.apiUrl}/KioskPushes/${id}/Cancel`) }
}

// ── Staff side: Admin > Kiosks and the gate's kiosk sign-ups ────────────────────────────────────

export interface KioskDeviceItem {
    id: string
    name: string
    paired: boolean
    pairedAtUtc: string | null
    lastSeenAtUtc: string | null
    pairingPending: boolean
    pairingExpiresAtUtc: string | null
    revokedAtUtc: string | null
    createdAtUtc: string
}

export interface KioskPairingCode {
    deviceId: string
    name: string
    code: string
    expiresAtUtc: string
}

export interface KioskSignupItem {
    id: string
    firstName: string
    lastName: string
    birthdate: string | null
    age: number | null
    isMinor: boolean
    riderUserId: string | null
    riderEmail: string | null
    dependentId: string | null
    guardianUserId: string | null
    guardianName: string | null
    guardianEmail: string | null
    waiverSigned: boolean
    photoId: string | null
    photoTakenAtUtc: string | null
    kioskName: string | null
    createdAtUtc: string
    handledAtUtc: string | null
    dismissedAtUtc: string | null
    /** The email matched an existing account: held until a cashier checks ID and confirms. */
    matchPending: boolean
    matchedAccountName: string | null
    matchedGuardianName: string | null
    matchedHasPhoto: boolean
    typedEmail: string | null
}

export class KioskAdminService {
    private apiUrl = API_BASE

    listDevices() { return axios.get<{ data: KioskDeviceItem[] }>(`${this.apiUrl}/KioskDevices`) }
    createDevice(name: string) { return axios.post<{ data: KioskPairingCode }>(`${this.apiUrl}/KioskDevices`, { name }) }
    newPairingCode(id: string) { return axios.post<{ data: KioskPairingCode }>(`${this.apiUrl}/KioskDevices/${id}/PairingCode`) }
    rename(id: string, name: string) { return axios.put(`${this.apiUrl}/KioskDevices/${id}`, { name }) }
    revoke(id: string) { return axios.post(`${this.apiUrl}/KioskDevices/${id}/Revoke`) }

    todaysSignups() { return axios.get<{ data: KioskSignupItem[] }>(`${this.apiUrl}/KioskSignups`) }
    dismissSignup(id: string) { return axios.post(`${this.apiUrl}/KioskSignups/${id}/Dismiss`) }
    confirmSignup(id: string, replacePhoto: boolean) {
        return axios.post<{ data: { waiverAttached: boolean; waiverChanged: boolean; photoAttached: boolean; keptExistingPhoto: boolean } }>(
            `${this.apiUrl}/KioskSignups/${id}/Confirm`, { idChecked: true, replacePhoto })
    }
}
