import { reactive } from 'vue'
import axios from 'axios'
import tenantHelper from '@/helpers/TenantHelper'

export interface BrandingState {
    loaded: boolean
    tenantId: string
    subdomain: string
    displayName: string
    timezone: string
    primaryColor: string
    secondaryColor: string
    accentColor: string
    tagline: string | null
    themeMode: 'light' | 'dark'
    logoUrl: string | null
    faviconUrl: string | null
    heroImageUrl: string | null
    secondaryHeroUrl: string | null
    stripePublishableKey: string | null
    requireReservationForPasses: boolean
    addressLine: string | null
    city: string | null
    region: string | null
    postalCode: string | null
    country: string | null
    latitude: number | null
    longitude: number | null
}

const defaults: BrandingState = {
    loaded: false,
    tenantId: '',
    subdomain: '',
    displayName: 'RidePass',
    timezone: 'UTC',
    primaryColor: '#1976D2',
    secondaryColor: '#424242',
    accentColor: '#82B1FF',
    tagline: null,
    themeMode: 'light',
    logoUrl: null,
    faviconUrl: null,
    heroImageUrl: null,
    secondaryHeroUrl: null,
    stripePublishableKey: null,
    requireReservationForPasses: false,
    addressLine: null,
    city: null,
    region: null,
    postalCode: null,
    country: null,
    latitude: null,
    longitude: null,
}

export const branding = reactive<BrandingState>({ ...defaults })

const apiUrl: string = import.meta.env.VITE_API_ENDPOINT ?? ''

function toAbsoluteUrl(url: string | null | undefined): string | null {
    if (!url) return null
    if (/^https?:\/\//i.test(url)) return url
    return `${apiUrl}${url}`
}

export async function loadBranding(): Promise<void> {
    if (!tenantHelper.getSubdomain()) return
    try {
        const response = await axios.get(`${apiUrl}/Tenant/Branding`)
        const data = response.data.data
        branding.tenantId = data.tenantId
        branding.subdomain = data.subdomain
        branding.displayName = data.displayName
        branding.timezone = data.timezone ?? 'UTC'
        branding.primaryColor = data.primaryColor
        branding.secondaryColor = data.secondaryColor
        branding.accentColor = data.accentColor
        branding.tagline = data.tagline
        branding.themeMode = data.themeMode
        branding.logoUrl = toAbsoluteUrl(data.logoUrl)
        branding.faviconUrl = toAbsoluteUrl(data.faviconUrl)
        branding.heroImageUrl = toAbsoluteUrl(data.heroImageUrl)
        branding.secondaryHeroUrl = toAbsoluteUrl(data.secondaryHeroUrl)
        branding.stripePublishableKey = data.stripePublishableKey ?? null
        branding.requireReservationForPasses = !!data.requireReservationForPasses
        branding.addressLine = data.addressLine ?? null
        branding.city = data.city ?? null
        branding.region = data.region ?? null
        branding.postalCode = data.postalCode ?? null
        branding.country = data.country ?? null
        branding.latitude = data.latitude ?? null
        branding.longitude = data.longitude ?? null
        branding.loaded = true
        applyFavicon(branding.faviconUrl)
        document.title = branding.displayName
    } catch (err) {
        console.error('Failed to load tenant branding', err)
    }
}

function applyFavicon(url: string | null): void {
    if (!url) return
    let link = document.querySelector<HTMLLinkElement>('link[rel~="icon"]')
    if (!link) {
        link = document.createElement('link')
        link.rel = 'icon'
        document.head.appendChild(link)
    }
    link.href = url
}
