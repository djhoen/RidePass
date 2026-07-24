import { reactive } from 'vue'
import axios from 'axios'
import tenantHelper from '@/helpers/TenantHelper'
import { loadPlatformBranding } from './platformBranding'
import { setHomeScreenIcon } from '@/helpers/HomeScreenIcon'

export interface BrandingState {
    loaded: boolean
    // True when the tenant's branding 404s (unknown / inactive / unpublished and
    // the viewer isn't allowed to see it). Drives the "not available" page.
    unavailable: boolean
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
    logoWhiteUrl: string | null
    faviconUrl: string | null
    heroImageUrl: string | null
    secondaryHeroUrl: string | null
    navBarColor: string | null
    navBarTextColor: string | null
    stripePublishableKey: string | null
    requireReservationForPasses: boolean
    requireEmergencyContact: boolean
    allowEventSubscriptions: boolean
    requireIdAtCheckin: boolean
    stripeConnectAccountId: string | null
    stripeConnectStatus: string | null
    stripeChargeMode: string
    serviceChargeBps: number
    // Renter's share of the service charge on rentals (bps). 10000 = renter pays all.
    rentalRiderPaidServiceChargeBps: number
    // Rental sales tax (bps). null = never configured, which the admin UI warns about.
    rentalTaxBps: number | null
    rentalTaxServiceChargeTaxable: boolean
    shippingName: string | null
    aboutHtml: string | null
    hoursJson: string | null
    homeNextUpTitle: string | null
    homeNextUpEventTypeIds: string[] | null
    benefitsHtml: string | null
    benefitsImageUrl: string | null
    homeSections: Record<string, boolean>   // sectionKey -> visible; missing key = visible
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
    extrasEnabled: boolean
    seasonPassesEnabled: boolean
    concessionsEnabled: boolean
    bikeShopEnabled: boolean
    rentalInsuranceEnabled: boolean
    rentalInsuranceLabel: string
    // Percent of rental value, in basis points (1500 = 15%).
    rentalInsuranceBps: number
    // Days after pickup to email a shop service reminder; 0 = off.
    shopServiceReminderDays: number
    shopReadyNotifyEmail: boolean
    shopReadyNotifySms: boolean
    shopSupplyFeeBps: number
    shopSupplyFeeCapCents: number | null
    shopSupplyFeeLabel: string
    shopLaborRateCents: number | null
    wristbandsEnabled: boolean
    tracksideExportEnabled: boolean
    /// The tenant has ever configured a spectator ticket tier; drives Spectator Report visibility.
    sellsSpectatorPasses: boolean
    blogEnabled: boolean
    // Published, nav-visible custom pages, in sort order. Rendered as top-level links
    // (public top bar + drawer) alongside the built-in Blog link.
    navPages: { slug: string; label: string }[]
    // Super-admin feature toggles gating the tier editor's advanced pricing sections.
    dynamicPricingEnabled: boolean
    bundledCouponsEnabled: boolean
    loampassMxEnabled: boolean
    embedEnabled: boolean
    embedAllowedOrigins: string[]
    globalEmbedAllowedOrigins: string[]
    clientType: 'hosted' | 'custom_domain' | 'embedded'
    customDomain: string | null
    customDomainVerified: boolean
    externalHomeUrl: string | null
    externalEventsUrl: string | null
    embedEventTarget: 'external' | 'ridepass'
    allowSelfCancel: boolean
    // Custom gate-fee section headings (checkout / event pricing); null = defaults.
    riderGateLabel: string | null
    spectatorGateLabel: string | null
    waitlistEnabled: boolean
    waitlistPrepayEnabled: boolean
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
    unavailable: false,
    tenantId: '',
    subdomain: '',
    displayName: 'RidePass',
    tenantType: 'motocross',
    timezone: 'UTC',
    primaryColor: '#FF6B1A',
    secondaryColor: '#1A1F2B',
    accentColor: '#FFA559',
    tagline: null,
    themeMode: 'light',
    logoUrl: null,
    logoWhiteUrl: null,
    faviconUrl: null,
    heroImageUrl: null,
    secondaryHeroUrl: null,
    navBarColor: null,
    navBarTextColor: null,
    stripePublishableKey: null,
    requireReservationForPasses: false,
    requireEmergencyContact: false,
    allowEventSubscriptions: true,
    requireIdAtCheckin: false,
    stripeConnectAccountId: null,
    stripeConnectStatus: null,
    stripeChargeMode: 'platform',
    serviceChargeBps: 300,
    rentalRiderPaidServiceChargeBps: 10000,
    rentalTaxBps: null,
    rentalTaxServiceChargeTaxable: true,
    shippingName: null,
    aboutHtml: null,
    hoursJson: null,
    homeNextUpTitle: null,
    homeNextUpEventTypeIds: null,
    benefitsHtml: null,
    benefitsImageUrl: null,
    homeSections: {},
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
    extrasEnabled: false,
    seasonPassesEnabled: true,
    concessionsEnabled: false,
    bikeShopEnabled: false,
    rentalInsuranceEnabled: false,
    rentalInsuranceLabel: 'Damage Protection',
    rentalInsuranceBps: 0,
    shopServiceReminderDays: 0,
    shopReadyNotifyEmail: true,
    shopReadyNotifySms: false,
    shopSupplyFeeBps: 0,
    shopSupplyFeeCapCents: null,
    shopSupplyFeeLabel: 'Shop supplies',
    shopLaborRateCents: null,
    wristbandsEnabled: false,
    tracksideExportEnabled: false,
    sellsSpectatorPasses: false,
    blogEnabled: false,
    navPages: [],
    dynamicPricingEnabled: false,
    bundledCouponsEnabled: false,
    loampassMxEnabled: false,
    embedEnabled: false,
    embedAllowedOrigins: [],
    globalEmbedAllowedOrigins: [],
    clientType: 'hosted',
    customDomain: null,
    customDomainVerified: false,
    externalHomeUrl: null,
    externalEventsUrl: null,
    embedEventTarget: 'external',
    allowSelfCancel: false,
    riderGateLabel: null,
    spectatorGateLabel: null,
    waitlistEnabled: false,
    waitlistPrepayEnabled: false,
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

// Parse the home section-visibility map. Only keeps real booleans; anything
// malformed falls back to {} (which the home page treats as "all visible").
function parseSections(json: string | null | undefined): Record<string, boolean> {
    if (!json) return {}
    try {
        const obj = JSON.parse(json)
        if (obj && typeof obj === 'object') {
            const out: Record<string, boolean> = {}
            for (const [k, v] of Object.entries(obj)) if (typeof v === 'boolean') out[k] = v
            return out
        }
    } catch { /* ignore */ }
    return {}
}

export async function loadBranding(): Promise<void> {
    if (!tenantHelper.getSubdomain()) {
        // Apex domain: no tenant, but the apex Home renders content edited
        // through Super Admin → Home page. Block the splash on that fetch so
        // the apex hero arrives at the same time as the rest of the page,
        // instead of momentarily flashing the default tagline.
        await loadPlatformBranding()
        branding.loaded = true
        document.title = branding.displayName
        return
    }
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
        branding.logoWhiteUrl = toAbsoluteUrl(data.logoWhiteUrl)
        branding.faviconUrl = toAbsoluteUrl(data.faviconUrl)
        branding.heroImageUrl = toAbsoluteUrl(data.heroImageUrl)
        branding.secondaryHeroUrl = toAbsoluteUrl(data.secondaryHeroUrl)
        branding.navBarColor = data.navBarColor ?? null
        branding.navBarTextColor = data.navBarTextColor ?? null
        branding.stripePublishableKey = data.stripePublishableKey ?? null
        branding.requireReservationForPasses = !!data.requireReservationForPasses
        branding.requireEmergencyContact = !!data.requireEmergencyContact
        branding.allowEventSubscriptions = !!data.allowEventSubscriptions
        branding.requireIdAtCheckin = !!data.requireIdAtCheckin
        branding.stripeConnectAccountId = data.stripeConnectAccountId ?? null
        branding.stripeConnectStatus = data.stripeConnectStatus ?? null
        branding.stripeChargeMode = data.stripeChargeMode ?? 'platform'
        branding.serviceChargeBps = data.serviceChargeBps ?? 300
        branding.rentalRiderPaidServiceChargeBps = data.rentalRiderPaidServiceChargeBps ?? 10000
        // Preserve null: it is what distinguishes "never set" from "deliberately 0%".
        branding.rentalTaxBps = data.rentalTaxBps ?? null
        branding.rentalTaxServiceChargeTaxable = data.rentalTaxServiceChargeTaxable ?? true
        branding.shippingName = data.shippingName ?? null
        branding.aboutHtml = data.aboutHtml ?? null
        branding.hoursJson = data.hoursJson ?? null
        branding.homeNextUpTitle = data.homeNextUpTitle ?? null
        branding.homeNextUpEventTypeIds = Array.isArray(data.homeNextUpEventTypeIds) ? data.homeNextUpEventTypeIds : null
        branding.benefitsHtml = data.homeBenefitsHtml ?? null
        branding.benefitsImageUrl = toAbsoluteUrl(data.homeBenefitsImageUrl)
        branding.homeSections = parseSections(data.homeSectionsJson)
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
        branding.extrasEnabled = !!data.extrasEnabled
        branding.seasonPassesEnabled = data.seasonPassesEnabled !== false   // default true
        branding.concessionsEnabled = !!data.concessionsEnabled
        branding.bikeShopEnabled = !!data.bikeShopEnabled
        branding.rentalInsuranceEnabled = !!data.rentalInsuranceEnabled
        branding.rentalInsuranceLabel = data.rentalInsuranceLabel ?? 'Damage Protection'
        branding.rentalInsuranceBps = data.rentalInsuranceBps ?? 0
        branding.shopServiceReminderDays = data.shopServiceReminderDays ?? 0
        branding.shopReadyNotifyEmail = data.shopReadyNotifyEmail !== false
        branding.shopReadyNotifySms = !!data.shopReadyNotifySms
        branding.shopSupplyFeeBps = data.shopSupplyFeeBps ?? 0
        branding.shopSupplyFeeCapCents = data.shopSupplyFeeCapCents ?? null
        branding.shopSupplyFeeLabel = data.shopSupplyFeeLabel ?? 'Shop supplies'
        branding.shopLaborRateCents = data.shopLaborRateCents ?? null
        branding.wristbandsEnabled = !!data.wristbandsEnabled
        branding.tracksideExportEnabled = !!data.tracksideExportEnabled
        branding.sellsSpectatorPasses = !!data.sellsSpectatorPasses
        branding.blogEnabled = !!data.blogEnabled
        branding.navPages = Array.isArray(data.navPages) ? data.navPages : []
        branding.dynamicPricingEnabled = !!data.dynamicPricingEnabled
        branding.bundledCouponsEnabled = !!data.bundledCouponsEnabled
        branding.loampassMxEnabled = !!data.loampassMxEnabled
        branding.embedEnabled = !!data.embedEnabled
        branding.embedAllowedOrigins = Array.isArray(data.embedAllowedOrigins) ? data.embedAllowedOrigins : []
        branding.globalEmbedAllowedOrigins = Array.isArray(data.globalEmbedAllowedOrigins) ? data.globalEmbedAllowedOrigins : []
        branding.clientType = data.clientType ?? 'hosted'
        branding.customDomain = data.customDomain ?? null
        branding.customDomainVerified = !!data.customDomainVerified
        branding.externalHomeUrl = data.externalHomeUrl ?? null
        branding.externalEventsUrl = data.externalEventsUrl ?? null
        branding.embedEventTarget = data.embedEventTarget === 'ridepass' ? 'ridepass' : 'external'
        branding.allowSelfCancel = !!data.allowSelfCancel
        branding.riderGateLabel = data.riderGateLabel ?? null
        branding.spectatorGateLabel = data.spectatorGateLabel ?? null
        // Both default OFF now (Script0180): the waitlist is a super-admin-gated platform feature,
        // so an absent flag must read as "off" rather than quietly showing a waitlist the API rejects.
        branding.waitlistEnabled = !!data.waitlistEnabled
        branding.waitlistPrepayEnabled = !!data.waitlistPrepayEnabled
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
        applyPwaInstall(branding.faviconUrl, branding.displayName)
        document.title = branding.displayName
    } catch (err: any) {
        // A 404 from the tenant-resolution middleware means this tenant is
        // unknown, inactive, or unpublished (and the viewer isn't allowed to see
        // it). Flip to a clean "unavailable" state and clear the splash instead
        // of silently rendering with default branding.
        if (err?.response?.status === 404) {
            branding.unavailable = true
            branding.loaded = true
        }
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

// Default home-screen install for the tenant: the tenant favicon + name, opening the login page. The
// POS and cook screens override this with their own role icons (see setHomeScreenIcon call sites).
function applyPwaInstall(faviconUrl: string | null, name: string): void {
    setHomeScreenIcon({ title: name, iconUrl: faviconUrl, startPath: '/Login' })
}
