import axios from 'axios'

export interface PlatformTestimonial {
    id: string
    sortOrder: number
    riderName: string
    riderPhotoUrl: string | null
    quote: string
    rating: number
    isActive: boolean
}

export interface PlatformBranding {
    heroImageUrl: string | null
    heroHeadline: string | null
    heroSubhead: string | null
    heroCtaPrimaryLabel: string | null
    heroCtaPrimaryUrl: string | null
    heroCtaSecondaryLabel: string | null
    heroCtaSecondaryUrl: string | null

    statsShowTracks: boolean
    statsShowEventDays: boolean
    statsPriceLabel: string | null

    sectionTracksTitle: string | null
    sectionEventsTitle: string | null
    sectionBenefitsTitle: string | null
    sectionTestimonialsTitle: string | null
    sectionTracksNearYouTitle: string | null

    benefitsHtml: string | null
    benefitsImageUrl: string | null

    ctaBannerHeadline: string | null
    ctaBannerSubhead: string | null
    ctaBannerPriceLabel: string | null
    ctaBannerCtaLabel: string | null
    ctaBannerCtaUrl: string | null

    featuredTrackIds: string[] | null

    navBarColor: string | null
    navBarTextColor: string | null
    navBarHomeColor: string | null
    navBarHomeTextColor: string | null

    testimonials: PlatformTestimonial[]
}

export interface SavePlatformBranding {
    heroHeadline: string | null
    heroSubhead: string | null
    heroCtaPrimaryLabel: string | null
    heroCtaPrimaryUrl: string | null
    heroCtaSecondaryLabel: string | null
    heroCtaSecondaryUrl: string | null

    statsShowTracks: boolean
    statsShowEventDays: boolean
    statsPriceLabel: string | null

    sectionTracksTitle: string | null
    sectionEventsTitle: string | null
    sectionBenefitsTitle: string | null
    sectionTestimonialsTitle: string | null
    sectionTracksNearYouTitle: string | null

    benefitsHtml: string | null

    ctaBannerHeadline: string | null
    ctaBannerSubhead: string | null
    ctaBannerPriceLabel: string | null
    ctaBannerCtaLabel: string | null
    ctaBannerCtaUrl: string | null

    featuredTrackIds: string[] | null

    navBarColor: string | null
    navBarTextColor: string | null
    navBarHomeColor: string | null
    navBarHomeTextColor: string | null
}

export interface UpsertTestimonial {
    riderName: string
    quote: string
    rating: number
    isActive: boolean
}

export class PlatformBrandingService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    get() {
        return axios.get<{ data: PlatformBranding }>(`${this.apiUrl}/PlatformBranding`)
    }

    save(payload: SavePlatformBranding) {
        return axios.put<{ data: PlatformBranding }>(`${this.apiUrl}/PlatformBranding`, payload)
    }

    uploadImage(kind: 'hero' | 'benefits', file: File) {
        const fd = new FormData()
        fd.append('file', file)
        return axios.post<{ data: { url: string } }>(
            `${this.apiUrl}/PlatformBranding/Image/${kind}`, fd,
            { headers: { 'Content-Type': 'multipart/form-data' } })
    }

    deleteImage(kind: 'hero' | 'benefits') {
        return axios.delete<{ data: PlatformBranding }>(`${this.apiUrl}/PlatformBranding/Image/${kind}`)
    }

    // ── Testimonials ─────────────────────────────────────────────────────────
    listTestimonials() {
        return axios.get<{ data: PlatformTestimonial[] }>(
            `${this.apiUrl}/PlatformBranding/Testimonials?includeInactive=true`)
    }

    createTestimonial(payload: UpsertTestimonial) {
        return axios.post<{ data: PlatformTestimonial }>(
            `${this.apiUrl}/PlatformBranding/Testimonials`, payload)
    }

    updateTestimonial(id: string, payload: UpsertTestimonial) {
        return axios.put<{ data: PlatformTestimonial }>(
            `${this.apiUrl}/PlatformBranding/Testimonials/${id}`, payload)
    }

    deleteTestimonial(id: string) {
        return axios.delete(`${this.apiUrl}/PlatformBranding/Testimonials/${id}`)
    }

    reorderTestimonials(orderedIds: string[]) {
        return axios.post(`${this.apiUrl}/PlatformBranding/Testimonials/Reorder`, { orderedIds })
    }

    uploadTestimonialPhoto(id: string, file: File) {
        const fd = new FormData()
        fd.append('file', file)
        return axios.post<{ data: PlatformTestimonial }>(
            `${this.apiUrl}/PlatformBranding/Testimonials/${id}/Photo`, fd,
            { headers: { 'Content-Type': 'multipart/form-data' } })
    }
}
