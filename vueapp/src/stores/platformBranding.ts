import { reactive } from 'vue'
import { PlatformBrandingService, type PlatformBranding } from '@/services/PlatformBrandingService'

/**
 * Parallel to the per-tenant `branding` store, but for the apex (ridepass.io).
 * Loaded once at app boot when there's no tenant subdomain. The apex Home
 * reads every section's copy + image + featured tracks list from here so
 * super admins can edit the landing page without a deploy.
 */
export const platformBranding = reactive<{
    loaded: boolean
    data: PlatformBranding | null
}>({
    loaded: false,
    data: null,
})

const apiUrl: string = import.meta.env.VITE_API_ENDPOINT ?? ''

function apiOrigin(): string {
    try {
        return new URL(apiUrl, window.location.origin).origin
    } catch {
        return ''
    }
}

/**
 * Convert a relative `/uploads/...` URL coming from the API into an
 * absolute URL pointing at the API host. Mirror of the helper in the
 * per-tenant branding store. Pass-through when the URL is already absolute.
 */
export function platformImageUrl(url: string | null | undefined): string | null {
    if (!url) return null
    if (/^https?:\/\//i.test(url)) return url
    return `${apiOrigin()}${url}`
}

export async function loadPlatformBranding(): Promise<void> {
    try {
        const r = await new PlatformBrandingService().get()
        platformBranding.data = (r.data as any).data
    } catch (err) {
        // Public read failures shouldn't keep the splash up forever. Log and
        // continue with data=null; Home.vue handles the empty-state gracefully.
        console.error('Failed to load platform branding', err)
    } finally {
        platformBranding.loaded = true
    }
}
