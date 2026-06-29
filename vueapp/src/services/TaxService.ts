import axios from 'axios'

// Event admission/amusement tax settings for the tenant. rateBps is basis points (900 = 9%).
export interface AdmissionTaxConfig {
    rateBps: number
    pricesIncludeTax: boolean
    serviceChargeTaxable: boolean
    jurisdictionLabel: string | null
    isActive: boolean
}

export class TaxService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    getAdmissionTax() {
        return axios.get(`${this.apiUrl}/Tax/Admission`)
    }

    updateAdmissionTax(req: AdmissionTaxConfig) {
        return axios.put(`${this.apiUrl}/Tax/Admission`, req)
    }
}
