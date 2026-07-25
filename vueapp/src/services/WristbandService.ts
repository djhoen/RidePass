import axios from 'axios'

export interface WristbandResolution {
    /** Which anchor this band resolved through. */
    source: 'ticket' | 'season_pass'
    /** Set when source is 'ticket'. */
    ticketId: string | null
    /** Set when source is 'season_pass': the admission the band was issued at. */
    reservationId: string | null
    /** Set when source is 'season_pass': the pass purchase behind that admission. */
    passPurchaseId: string | null
    /** Null on a band issued on a day with no calendar event. */
    eventId: string | null
    code: string
    redemptionToken: string
    eventTitle: string
    tierName: string | null
    status: string
    name: string
    raceNumber: string | null
    linkedAt: string
}

export interface WristbandCodesResult {
    tickets: { ticketId: string; code: string }[]
    reservations: { reservationId: string; code: string }[]
}

export class WristbandService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    link(ticketId: string, code: string) {
        return axios.post(`${this.apiUrl}/Wristband/Link`, { ticketId, code })
    }
    /** Link a band to a season pass admission (a checked-in reservation) rather than a ticket. */
    linkReservation(seasonPassReservationId: string, code: string) {
        return axios.post(`${this.apiUrl}/Wristband/Link`, { seasonPassReservationId, code })
    }
    unlink(ticketId: string) {
        return axios.post(`${this.apiUrl}/Wristband/Unlink`, { ticketId })
    }
    unlinkReservation(seasonPassReservationId: string) {
        return axios.post(`${this.apiUrl}/Wristband/Unlink`, { seasonPassReservationId })
    }
    resolve(code: string) {
        return axios.get<{ data: WristbandResolution }>(`${this.apiUrl}/Wristband/Resolve?code=${encodeURIComponent(code)}`)
    }
    /** Band codes for tickets and/or season pass admissions. The two id spaces are different, so
     *  they come back as separate maps. */
    codes(ticketIds: string[], reservationIds: string[] = []) {
        return axios.post<{ data: WristbandCodesResult }>(
            `${this.apiUrl}/Wristband/Codes`, { ticketIds, reservationIds })
    }
}
