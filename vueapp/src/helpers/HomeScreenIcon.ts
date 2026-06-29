// Drives what an "Add to Home Screen" install captures: the icon, the label, and the URL it opens.
// Call it per context so a tablet can pin several chromeless icons - e.g. a "Cashier" icon that opens
// the POS and a "Cook" icon that opens the kitchen - each launching standalone (no address bar/back).
//
// iOS reads the live <link rel="apple-touch-icon"> + <meta apple-mobile-web-app-title> at add time;
// Android reads the <link rel="manifest">. We update all three. Set this on the screen the user will
// add to their home screen (it's captured at the moment they add it).

function setLink(rel: string, href: string): void {
    let el = document.querySelector<HTMLLinkElement>(`link[rel="${rel}"]`)
    if (!el) { el = document.createElement('link'); el.rel = rel; document.head.appendChild(el) }
    el.href = href
}

function setMeta(name: string, content: string): void {
    let el = document.querySelector<HTMLMetaElement>(`meta[name="${name}"]`)
    if (!el) { el = document.createElement('meta'); el.name = name; document.head.appendChild(el) }
    el.content = content
}

function absolute(url: string, origin: string): string {
    return url.startsWith('http') ? url : `${origin}${url.startsWith('/') ? '' : '/'}${url}`
}

export function setHomeScreenIcon(opts: {
    title: string
    iconUrl: string | null    // tenant favicon or a role icon path; falls back to the bundled icon
    startPath: string         // path the installed icon opens, e.g. '/Admin/ConcessionPos'
    scope?: string
}): void {
    const origin = window.location.origin
    const icon = opts.iconUrl || '/pwa-icon.png'

    setLink('apple-touch-icon', absolute(icon, origin))
    setMeta('apple-mobile-web-app-title', opts.title)

    // Android: a per-context manifest. A blob: manifest has no base, so every URL must be absolute.
    const manifest = {
        name: opts.title,
        short_name: opts.title,
        start_url: `${origin}${opts.startPath}`,
        scope: `${origin}${opts.scope ?? '/'}`,
        display: 'standalone',
        background_color: '#ffffff',
        theme_color: '#111827',
        icons: [
            { src: absolute(icon, origin), sizes: '192x192', type: 'image/png', purpose: 'any' },
            { src: absolute(icon, origin), sizes: '512x512', type: 'image/png', purpose: 'any maskable' },
        ],
    }
    const blobUrl = URL.createObjectURL(new Blob([JSON.stringify(manifest)], { type: 'application/manifest+json' }))
    setLink('manifest', blobUrl)
}
