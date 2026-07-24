/**
 * Uploaded images come back either absolute (DO Spaces) or relative to the API host
 * (`/uploads/...` from local-disk storage). A relative path only resolves against the
 * SPA's own origin, which breaks whenever the app and the API are different origins
 * (local dev, and any embedded widget), so it has to be absolutized against the API.
 *
 * This body is currently copy-pasted into ~15 views. New code should import it from
 * here; migrating the existing copies is a separate cleanup.
 */
export function absoluteUrl(url: string | null | undefined): string | null {
    if (!url) return null
    if (/^https?:\/\//i.test(url)) return url
    const base = (import.meta.env.VITE_API_ENDPOINT ?? '').replace(/\/api\/?$/, '')
    return `${base}${url}`
}
