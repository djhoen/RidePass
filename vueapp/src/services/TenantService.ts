import axios from 'axios'

export class TenantService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    async getBranding() {
        return axios.get(`${this.apiUrl}/Tenant/Branding`)
    }

    async updateSettings(req: { timezone: string; requireReservationForPasses: boolean }) {
        return axios.put(`${this.apiUrl}/Tenant`, req)
    }

    async updateLocation(req: {
        addressLine: string | null
        city: string | null
        region: string | null
        postalCode: string | null
        country: string | null
        latitude: number | null
        longitude: number | null
    }) {
        return axios.put(`${this.apiUrl}/Tenant/Location`, req)
    }

    async updateBranding(req: {
        primaryColor: string
        secondaryColor: string
        accentColor: string
        tagline: string | null
        themeMode: 'light' | 'dark'
    }) {
        return axios.put(`${this.apiUrl}/Tenant/Branding`, req)
    }

    async uploadBrandingImage(kind: 'logo' | 'favicon' | 'hero' | 'secondaryHero', file: File) {
        const form = new FormData()
        form.append('file', file)
        return axios.post(`${this.apiUrl}/Tenant/Branding/Image/${kind}`, form, {
            headers: { 'Content-Type': 'multipart/form-data' },
        })
    }

    async deleteBrandingImage(kind: 'logo' | 'favicon' | 'hero' | 'secondaryHero') {
        return axios.delete(`${this.apiUrl}/Tenant/Branding/Image/${kind}`)
    }
}
