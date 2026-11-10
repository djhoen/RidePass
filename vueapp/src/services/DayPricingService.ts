import axios from 'axios'

// Peak days (Script0316): weekdays or dates when a day pass costs more, online and at the gate.
export interface DayPriceRule {
    id: string
    label: string
    /** 0 = Sunday .. 6 = Saturday; null for a date rule. */
    weekday: number | null
    /** yyyy-MM-dd; null for a weekday rule. */
    onDate: string | null
    /** null = every event type. */
    eventTypeId: string | null
    adjustKind: 'amount' | 'percent'
    /** Cents for amount, basis points for percent (2500 = +25%). */
    adjustValue: number
    isActive: boolean
}

export class DayPricingService {
    private apiUrl: string
    // Typed loosely: this project's env typings are not set up for vue-tsc.
    constructor() { this.apiUrl = (import.meta as any).env?.VITE_API_ENDPOINT ?? '' }

    list() { return axios.get<{ data: DayPriceRule[] }>(`${this.apiUrl}/DayPricing`) }
    create(req: Omit<DayPriceRule, 'id' | 'isActive'>) { return axios.post<{ data: { id: string } }>(`${this.apiUrl}/DayPricing`, req) }
    setActive(id: string, isActive: boolean) { return axios.put(`${this.apiUrl}/DayPricing/${id}/Active`, { isActive }) }
    remove(id: string) { return axios.delete(`${this.apiUrl}/DayPricing/${id}`) }
}
