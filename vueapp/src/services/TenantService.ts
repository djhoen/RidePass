import axios from 'axios'

export class TenantService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    async getBranding() {
        return axios.get(`${this.apiUrl}/Tenant/Branding`)
    }

    async updateSettings(req: { timezone: string; requireReservationForPasses: boolean; requireEmergencyContact: boolean; allowEventSubscriptions: boolean; requireIdAtCheckin: boolean }) {
        return axios.put(`${this.apiUrl}/Tenant`, req)
    }

    async updateGiftCardSettings(req: { enabled: boolean; minCents: number; maxCents: number }) {
        return axios.put(`${this.apiUrl}/Tenant/GiftCardSettings`, req)
    }

    async updateRentalsEnabled(req: { enabled: boolean }) {
        return axios.put(`${this.apiUrl}/Tenant/RentalsEnabled`, req)
    }

    async updateExtrasEnabled(req: { enabled: boolean }) {
        return axios.put(`${this.apiUrl}/Tenant/ExtrasEnabled`, req)
    }

    async updateSeasonPassesEnabled(req: { enabled: boolean }) {
        return axios.put(`${this.apiUrl}/Tenant/SeasonPassesEnabled`, req)
    }

    async updateConcessionsEnabled(req: { enabled: boolean }) {
        return axios.put(`${this.apiUrl}/Tenant/ConcessionsEnabled`, req)
    }

    async updateBlogEnabled(req: { enabled: boolean }) {
        return axios.put(`${this.apiUrl}/Tenant/BlogEnabled`, req)
    }

    async updateCancellationPolicy(req: {
        allowSelfCancel: boolean
        waitlistEnabled: boolean
        waitlistConfirmWindowMinutes: number
    }) {
        return axios.put(`${this.apiUrl}/Tenant/CancellationPolicy`, req)
    }

    async updateLocation(req: {
        shippingName: string | null
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
        navBarColor: string | null
        navBarTextColor: string | null
        navBarHomeColor: string | null
        navBarHomeTextColor: string | null
    }) {
        return axios.put(`${this.apiUrl}/Tenant/Branding`, req)
    }

    async uploadBrandingImage(kind: 'logo' | 'favicon' | 'hero' | 'secondaryHero' | 'benefits', file: File) {
        const form = new FormData()
        form.append('file', file)
        return axios.post(`${this.apiUrl}/Tenant/Branding/Image/${kind}`, form, {
            headers: { 'Content-Type': 'multipart/form-data' },
        })
    }

    async deleteBrandingImage(kind: 'logo' | 'favicon' | 'hero' | 'secondaryHero' | 'benefits') {
        return axios.delete(`${this.apiUrl}/Tenant/Branding/Image/${kind}`)
    }

    async startStripeConnectOnboarding() {
        return axios.post(`${this.apiUrl}/Tenant/StripeConnect/Onboard`)
    }

    async refreshStripeConnectStatus() {
        return axios.post(`${this.apiUrl}/Tenant/StripeConnect/Refresh`)
    }

    async testStripeConnect() {
        return axios.post(`${this.apiUrl}/Tenant/StripeConnect/Test`)
    }

    async updateHomeContent(req: {
        aboutHtml: string | null
        hoursJson: string | null
        homeNextUpTitle: string | null
        homeNextUpEventTypeIds: string[] | null
        homeBenefitsHtml: string | null
        homeSectionsJson: string | null
    }) {
        return axios.put(`${this.apiUrl}/Tenant/Home/Content`, req)
    }

    async updateDailyStatus(req: { open: boolean | null; message: string | null }) {
        return axios.put(`${this.apiUrl}/Tenant/Home/DailyStatus`, req)
    }

    async updateFooter(req: {
        contactEmail: string | null
        phone: string | null
        socialFacebookUrl: string | null
        socialInstagramUrl: string | null
        socialTiktokUrl: string | null
        socialYoutubeUrl: string | null
        refundPolicyHtml: string | null
    }) {
        return axios.put(`${this.apiUrl}/Tenant/Home/Footer`, req)
    }

    async disconnectStripeConnect() {
        return axios.delete(`${this.apiUrl}/Tenant/StripeConnect`)
    }

    listGallery() {
        return axios.get<{ data: GalleryImage[] }>(`${this.apiUrl}/Tenant/Home/Gallery`)
    }

    async addGalleryImage(file: File, caption: string | null, sortOrder: number) {
        const form = new FormData()
        form.append('file', file)
        if (caption) form.append('caption', caption)
        form.append('sortOrder', String(sortOrder))
        return axios.post(`${this.apiUrl}/Tenant/Home/Gallery`, form, {
            headers: { 'Content-Type': 'multipart/form-data' },
        })
    }

    updateGalleryImage(id: string, body: { caption: string | null; sortOrder: number }) {
        return axios.put(`${this.apiUrl}/Tenant/Home/Gallery/${id}`, body)
    }

    deleteGalleryImage(id: string) {
        return axios.delete(`${this.apiUrl}/Tenant/Home/Gallery/${id}`)
    }
    reorderGallery(items: { id: string; sortOrder: number }[]) {
        return axios.post(`${this.apiUrl}/Tenant/Home/Gallery/Reorder`, { items })
    }

    listTrackGraphics() {
        return axios.get<{ data: TrackGraphic[] }>(`${this.apiUrl}/Tenant/Home/TrackGraphics`)
    }

    async addTrackGraphic(file: File, title: string | null, description: string | null, sortOrder: number) {
        const form = new FormData()
        form.append('file', file)
        if (title) form.append('title', title)
        if (description) form.append('description', description)
        form.append('sortOrder', String(sortOrder))
        return axios.post(`${this.apiUrl}/Tenant/Home/TrackGraphics`, form, {
            headers: { 'Content-Type': 'multipart/form-data' },
        })
    }

    updateTrackGraphic(id: string, body: { title: string | null; description: string | null; sortOrder: number }) {
        return axios.put(`${this.apiUrl}/Tenant/Home/TrackGraphics/${id}`, body)
    }

    deleteTrackGraphic(id: string) {
        return axios.delete(`${this.apiUrl}/Tenant/Home/TrackGraphics/${id}`)
    }
    reorderTrackGraphics(items: { id: string; sortOrder: number }[]) {
        return axios.post(`${this.apiUrl}/Tenant/Home/TrackGraphics/Reorder`, { items })
    }
}

export interface GalleryImage {
    id: string
    tenantId: string
    imageUrl: string
    caption: string | null
    sortOrder: number
    createdAt: string
}

export interface TrackGraphic {
    id: string
    tenantId: string
    imageUrl: string
    title: string | null
    description: string | null
    sortOrder: number
    createdAt: string
}
