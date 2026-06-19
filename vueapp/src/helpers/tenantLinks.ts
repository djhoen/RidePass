import tenantHelper from '@/helpers/TenantHelper'

/**
 * Resolves a tenant's public link based on its client type, used by the apex
 * discovery surfaces (track cards, event cards, map). Accepts the track-shaped
 * field names; event discover items pass their `tenant*`-prefixed values mapped
 * onto this shape.
 */
export interface TenantLinkFields {
    subdomain: string
    clientType?: string | null
    customDomain?: string | null
    customDomainVerified?: boolean
    externalHomeUrl?: string | null
    externalEventsUrl?: string | null
    // Embedded clients only: 'external' (their site) or 'ridepass' (hosted event page).
    embedEventTarget?: string | null
}

function withScheme(u: string): string {
    return /^https?:\/\//i.test(u) ? u : `https://${u}`
}
function subdomainBase(subdomain: string): string {
    const proto = window.location.protocol
    const port = window.location.port ? `:${window.location.port}` : ''
    return `${proto}//${subdomain}.${tenantHelper.rootDomain()}${port}`
}

// The tenant's public home: their custom domain (when verified), an embedded
// client's external website home, else their hosted *.ridepass.io subdomain.
export function tenantHomeUrl(t: TenantLinkFields): string {
    if (t.clientType === 'custom_domain' && t.customDomainVerified && t.customDomain) return withScheme(t.customDomain)
    if (t.clientType === 'embedded' && t.externalHomeUrl) return withScheme(t.externalHomeUrl)
    return subdomainBase(t.subdomain) + '/'
}

// A specific event's public page. A custom domain serves the RidePass app, so it
// deep-links to /Event/:id. An embedded client chooses per-tenant via embedEventTarget:
// 'ridepass' deep-links to the hosted /Event/:id page; otherwise (default 'external')
// it points at their external events page (falling back to their home).
export function tenantEventUrl(t: TenantLinkFields, eventId: string): string {
    if (t.clientType === 'custom_domain' && t.customDomainVerified && t.customDomain) {
        return withScheme(t.customDomain).replace(/\/$/, '') + `/Event/${eventId}`
    }
    if (t.clientType === 'embedded' && t.embedEventTarget !== 'ridepass') {
        if (t.externalEventsUrl) return withScheme(t.externalEventsUrl)
        if (t.externalHomeUrl) return withScheme(t.externalHomeUrl)
    }
    return subdomainBase(t.subdomain) + `/Event/${eventId}`
}
