import { reactive } from 'vue'
import axios from 'axios'
import tenantHelper from '@/helpers/TenantHelper'

export interface BrandingState {
    loaded: boolean
    tenantId: string
    subdomain: string
    displayName: string
    tenantType: 'motocross' | 'mountain_bike'
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
    requireEmergencyContact: boolean
    allowEventSubscriptions: boolean
    stripeConnectAccountId: string | null
    stripeConnectStatus: string | null
    serviceChargeBps: number
    shippingName: string | null
    aboutHtml: string | null
    hoursJson: string | null
    homeNextUpTitle: string | null
    homeNextUpEventTypeIds: string[] | null
    dailyStatusOpen: boolean | null
    dailyStatusMessage: string | null
    dailyStatusUpdatedAt: string | null
    contactEmail: string | null
    phone: string | null
    socialFacebookUrl: string | null
    socialInstagramUrl: string | null
    socialTiktokUrl: string | null
    socialYoutubeUrl: string | null
    refundPolicyHtml: string | null
    addressLine: string | null
    city: string | null
    region: string | null
    postalCode: string | null
    country: string | null
    latitude: number | null
    longitude: number | null
    giftCardsEnabled: boolean
    giftCardMinCents: number
    giftCardMaxCents: number
    rentalsEnabled: boolean
    extrasEnabled: boolean
    seasonPassesEnabled: boolean
    allowSelfCancel: boolean
    waitlistEnabled: boolean
    waitlistConfirmWindowMinutes: number
    membershipEnabled: boolean
    membershipName: string
    membershipPriceCents: number
    membershipDurationKind: 'one_time' | 'yearly'
    membershipRequiredForRiders: boolean
    membershipRequiredForSpectators: boolean
}

const defaults: BrandingState = {
    loaded: false,
    tenantId: '',
    subdomain: '',
    displayName: 'RidePass',
    tenantType: 'motocross',
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
    requireEmergencyContact: false,
    allowEventSubscriptions: true,
    stripeConnectAccountId: null,
    stripeConnectStatus: null,
    serviceChargeBps: 300,
    shippingName: null,
    aboutHtml: null,
    hoursJson: null,
    homeNextUpTitle: null,
    homeNextUpEventTypeIds: null,
    dailyStatusOpen: null,
    dailyStatusMessage: null,
    dailyStatusUpdatedAt: null,
    contactEmail: null,
    phone: null,
    socialFacebookUrl: null,
    socialInstagramUrl: null,
    socialTiktokUrl: null,
    socialYoutubeUrl: null,
    refundPolicyHtml: null,
    addressLine: null,
    city: null,
    region: null,
    postalCode: null,
    country: null,
    latitude: null,
    longitude: null,
    giftCardsEnabled: false,
    giftCardMinCents: 1000,
    giftCardMaxCents: 50000,
    rentalsEnabled: false,
    extrasEnabled: false,
    seasonPassesEnabled: true,
    allowSelfCancel: false,
    waitlistEnabled: true,
    waitlistConfirmWindowMinutes: 20,
    membershipEnabled: false,
    membershipName: 'Track Membership',
    membershipPriceCents: 0,
    membershipDurationKind: 'yearly',
    membershipRequiredForRiders: true,
    membershipRequiredForSpectators: false,
}

export const branding = reactive<BrandingState>({ ...defaults })

const apiUrl: string = import.meta.env.VITE_API_ENDPOINT ?? ''

// Static uploads (logos, hero images, gallery photos, etc.) are served from the API
// host's root via UseStaticFiles — they're NOT under the /api route prefix that
// VITE_API_ENDPOINT carries. Strip the path so we end up with origin + /uploads/...
function apiOrigin(): string {
    try {
        return new URL(apiUrl, window.location.origin).origin
    } catch {
        return ''
    }
}

function toAbsoluteUrl(url: string | null | undefined): string | null {
    if (!url) return null
    if (/^https?:\/\//i.test(url)) return url
    return `${apiOrigin()}${url}`
}

export async function loadBranding(): Promise<void> {
    if (!tenantHelper.getSubdomain()) return
    try {
        const response = await axios.get(`${apiUrl}/Tenant/Branding`)
        const data = response.data.data
        branding.tenantId = data.tenantId
        branding.subdomain = data.subdomain
        branding.displayName = data.displayName
        branding.tenantType = data.tenantType ?? 'motocross'
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
        branding.requireEmergencyContact = !!data.requireEmergencyContact
        branding.allowEventSubscriptions = !!data.allowEventSubscriptions
        branding.stripeConnectAccountId = data.stripeConnectAccountId ?? null
        branding.stripeConnectStatus = data.stripeConnectStatus ?? null
        branding.serviceChargeBps = data.serviceChargeBps ?? 300
        branding.shippingName = data.shippingName ?? null
        branding.aboutHtml = data.aboutHtml ?? null
        branding.hoursJson = data.hoursJson ?? null
        branding.homeNextUpTitle = data.homeNextUpTitle ?? null
        branding.homeNextUpEventTypeIds = Array.isArray(data.homeNextUpEventTypeIds) ? data.homeNextUpEventTypeIds : null
        branding.dailyStatusOpen = (typeof data.dailyStatusOpen === 'boolean') ? data.dailyStatusOpen : null
        branding.dailyStatusMessage = data.dailyStatusMessage ?? null
        branding.dailyStatusUpdatedAt = data.dailyStatusUpdatedAt ?? null
        branding.contactEmail = data.contactEmail ?? null
        branding.phone = data.phone ?? null
        branding.socialFacebookUrl = data.socialFacebookUrl ?? null
        branding.socialInstagramUrl = data.socialInstagramUrl ?? null
        branding.socialTiktokUrl = data.socialTiktokUrl ?? null
        branding.socialYoutubeUrl = data.socialYoutubeUrl ?? null
        branding.refundPolicyHtml = data.refundPolicyHtml ?? null
        branding.addressLine = data.addressLine ?? null
        branding.city = data.city ?? null
        branding.region = data.region ?? null
        branding.postalCode = data.postalCode ?? null
        branding.country = data.country ?? null
        branding.latitude = data.latitude ?? null
        branding.longitude = data.longitude ?? null
        branding.giftCardsEnabled = !!data.giftCardsEnabled
        branding.giftCardMinCents = data.giftCardMinCents ?? 1000
        branding.giftCardMaxCents = data.giftCardMaxCents ?? 50000
        branding.rentalsEnabled = !!data.rentalsEnabled
        branding.extrasEnabled = !!data.extrasEnabled
        branding.seasonPassesEnabled = data.seasonPassesEnabled !== false   // default true
        branding.allowSelfCancel = !!data.allowSelfCancel
        branding.waitlistEnabled = data.waitlistEnabled !== false   // default true
        branding.waitlistConfirmWindowMinutes = data.waitlistConfirmWindowMinutes ?? 20
        branding.membershipEnabled = !!data.membershipEnabled
        branding.membershipName = data.membershipName ?? 'Track Membership'
        branding.membershipPriceCents = data.membershipPriceCents ?? 0
        branding.membershipDurationKind = data.membershipDurationKind ?? 'yearly'
        // Riders defaults to true server-side; preserve that when the field is missing.
        branding.membershipRequiredForRiders = data.membershipRequiredForRiders !== false
        branding.membershipRequiredForSpectators = !!data.membershipRequiredForSpectators
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
