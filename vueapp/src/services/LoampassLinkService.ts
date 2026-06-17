import axios from 'axios'

export interface LoampassAccount {
    loampassEmail: string
    loampassAccountId: string
}

export interface LoampassStatus {
    // True when the current track is a LoamPassMx track (a destination is configured).
    trackParticipates: boolean
    linked: boolean
    // Every LoamMx account the rider has connected (1 rider -> many accounts).
    accounts: LoampassAccount[]
    // Redeemable credits at this track, summed across linked accounts; null when not linked
    // or the track doesn't participate.
    creditsAvailable: number | null
}

export class LoampassLinkService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    status() {
        return axios.get<{ data: LoampassStatus }>(`${this.apiUrl}/RiderLoampass/Status`)
    }

    linkStart(email: string) {
        return axios.post(`${this.apiUrl}/RiderLoampass/LinkStart`, { email })
    }

    linkConfirm(email: string, code: string) {
        return axios.post<{ data: { linked: boolean; loampassEmail: string } }>(
            `${this.apiUrl}/RiderLoampass/LinkConfirm`, { email, code })
    }

    unlink(accountId: string) {
        return axios.delete(`${this.apiUrl}/RiderLoampass`, { params: { accountId } })
    }

    // Staff gate check-in: scan a rider's Loam Pass QR to check in their reservation for an event.
    gateCheckIn(passQr: string, eventId: string) {
        return axios.post<{ data: { checkedIn: boolean; riderName: string; item: string } }>(
            `${this.apiUrl}/RiderLoampass/GateCheckIn`, { passQr, eventId })
    }
}
