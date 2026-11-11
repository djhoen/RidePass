import axios from 'axios'

// Lesson date changes and weather cancellations (Script0319).
export interface LessonRebookOption {
    eventId: string
    eventTitle: string
    tierId: string
    tierName: string
    startsAt: string
    endsAt: string
    /** null = unlimited. */
    spotsLeft: number | null
}

export interface LessonChangeInfo {
    ticketId: string
    status: string
    eventTitle: string
    tierName: string
    riderName: string | null
    sessionStartsAt: string
    amountCents: number
    cutoffHours: number
    weatherCancelled: boolean
    canRebook: boolean
    blockReason: string | null
    canRefund: boolean
    refundNote: string | null
    hasRental: boolean
    options: LessonRebookOption[]
}

export interface LessonSession {
    eventId: string
    title: string
    startsAt: string
    endsAt: string
    status: string
    weatherCancelledAt: string | null
    booked: number
}

export interface LessonBooking {
    ticketId: string
    status: string
    tierName: string
    riderName: string
    purchaserName: string
    purchaserEmail: string
    amountCents: number
}

export class LessonChangeService {
    private apiUrl: string
    // Typed loosely: this project's env typings are not set up for vue-tsc.
    constructor() { this.apiUrl = (import.meta as any).env?.VITE_API_ENDPOINT ?? '' }

    // Rider: the signed-in buyer, or the ticket's token from the emailed link.
    get(ticketId: string, token?: string | null) {
        return axios.get<{ data: LessonChangeInfo }>(`${this.apiUrl}/LessonChange/Ticket/${ticketId}`,
            { params: token ? { token } : undefined })
    }
    rebook(ticketId: string, tierId: string, token?: string | null) {
        return axios.post<{ data: LessonChangeInfo }>(`${this.apiUrl}/LessonChange/Ticket/${ticketId}/Rebook`,
            { tierId, token: token || null })
    }
    weatherRefund(ticketId: string, token?: string | null) {
        return axios.post<{ data: { id: string; refundCents: number } }>(`${this.apiUrl}/Purchase/Ticket/${ticketId}/WeatherRefund`,
            { token: token || null })
    }

    // Staff.
    getForStaff(ticketId: string) {
        return axios.get<{ data: LessonChangeInfo }>(`${this.apiUrl}/LessonChange/Staff/Ticket/${ticketId}`)
    }
    rebookForStaff(ticketId: string, tierId: string) {
        return axios.post<{ data: LessonChangeInfo }>(`${this.apiUrl}/LessonChange/Staff/Ticket/${ticketId}/Rebook`, { tierId })
    }
    sessions(days = 21) {
        return axios.get<{ data: LessonSession[] }>(`${this.apiUrl}/LessonChange/Sessions`, { params: { days } })
    }
    bookings(eventId: string) {
        return axios.get<{ data: LessonBooking[] }>(`${this.apiUrl}/LessonChange/Sessions/${eventId}/Bookings`)
    }
    weatherCancel(eventId: string) {
        return axios.post<{ data: { bookings: number; emailed: number } }>(`${this.apiUrl}/LessonChange/Sessions/${eventId}/WeatherCancel`)
    }
}
