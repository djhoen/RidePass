import axios from 'axios'

export interface WristbandResolution {
    ticketId: string
    eventId: string
    code: string
    redemptionToken: string
    eventTitle: string
    tierName: string
    status: string
    name: string
    raceNumber: string | null
    linkedAt: string
}

export class WristbandService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    link(ticketId: string, code: string) {
        return axios.post(`${this.apiUrl}/Wristband/Link`, { ticketId, code })
    }
    unlink(ticketId: string) {
        return axios.post(`${this.apiUrl}/Wristband/Unlink`, { ticketId })
    }
    resolve(code: string) {
        return axios.get<{ data: WristbandResolution }>(`${this.apiUrl}/Wristband/Resolve?code=${encodeURIComponent(code)}`)
    }
    codes(ticketIds: string[]) {
        return axios.post<{ data: { ticketId: string; code: string }[] }>(`${this.apiUrl}/Wristband/Codes`, { ticketIds })
    }
}
