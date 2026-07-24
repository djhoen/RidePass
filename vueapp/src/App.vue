<template>
    <v-app :class="{ 'embed-mode': isEmbed }">
        <!-- Pre-branding splash. Sits above everything in a neutral white state with a
             faint logo so the user doesn't see the default Vuetify theme flash before
             the tenant colors arrive. Vue's <transition> handles the fade-out the
             moment branding.loaded flips true. -->
        <transition name="splash-fade">
            <div v-if="!branding.loaded || !routerReady" class="branding-splash">
                <div class="branding-splash-content">
                    <img :src="splashLogo" alt="RidePass" class="branding-splash-img" />
                    <div class="branding-splash-text">RidePass</div>
                </div>
            </div>
        </transition>

        <!-- Hold all app chrome until branding is known. Rendering NavBar / router-view
             before /api/Tenant/Branding resolves would paint the generic default-tenant
             shell (RidePass brand, no Gift Cards, default colors) and then visibly swap +
             reflow when the real tenant data lands. Gating on branding.loaded means the
             content mounts once, already correct, while the splash above covers the gap.
             routerReady matters for the same reason: until the initial (lazy-loaded) route
             resolves, route.meta is empty, so hideNav pages (embed widgets especially)
             would flash the NavBar and then drop it. -->
        <template v-if="branding.loaded && routerReady">
            <!-- Tenant not available to this visitor (unknown / inactive / unpublished). -->
            <div v-if="branding.unavailable" class="tenant-unavailable">
                <div class="tenant-unavailable-content">
                    <img :src="splashLogo" alt="" class="tenant-unavailable-img" />
                    <h1 class="text-h5 font-weight-bold mt-3 mb-2">This track isn't available yet</h1>
                    <p class="text-body-2 text-medium-emphasis mb-5">
                        This page hasn't been published. Check back soon, or explore other tracks on RidePass.
                    </p>
                    <v-btn color="primary" :href="apexUrl">Explore RidePass</v-btn>
                </div>
            </div>

            <template v-else>
                <NavBar v-if="!$route.meta.hideNav" />
                <!-- Chromeless pages have no nav bar to host the impersonation pill;
                     float it top-right so the state is never invisible. -->
                <ImpersonationMenu v-else floating />
                <v-main>
                    <div v-if="embedBlocked" class="embed-blocked">
                        <p class="text-body-2 text-medium-emphasis">{{ embedBlockedMessage }}</p>
                    </div>
                    <router-view v-else />
                </v-main>
                <Footer v-if="!$route.meta.hideFooter" />
                <ConfirmDialog />
            </template>
        </template>
    </v-app>
</template>

<script setup lang="ts">
import { onMounted, watch, watchEffect, computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useTheme } from 'vuetify'
import NavBar from './components/NavBar.vue'
import ImpersonationMenu from './components/ImpersonationMenu.vue'
import Footer from './components/Footer.vue'
import ConfirmDialog from './components/ConfirmDialog.vue'
import { branding, loadBranding } from './stores/branding'
import tenantHelper from './helpers/TenantHelper'
import splashLogo from './assets/helmet.png'

const theme = useTheme()
const route = useRoute()
const router = useRouter()

// True once the initial navigation (including its lazy component chunk) has
// resolved and route.meta is trustworthy. See the chrome-gating comment above.
const routerReady = ref(false)
// finally: if the initial navigation fails (e.g. a chunk fails to load), still lift
// the gate — a possibly-wrong nav state beats an everlasting splash screen.
router.isReady().catch(() => {}).finally(() => { routerReady.value = true })

// Embed mode: chromeless widget framed on a track's own site (via embed.js).
const isEmbed = computed(() => !!route.meta.embed)

// Scrollbars belong to the DOCUMENT element, which sits outside this component's
// tree, so the class has to be stamped on <html> rather than styled from the
// template. The widget iframe auto-resizes to its content, so nothing is reachable
// only by scrolling — the bar is pure noise (and on Windows, with its arrows, an
// obvious "this is an iframe" tell on someone else's site).
watchEffect(() => {
    document.documentElement.classList.toggle('rp-embed', isEmbed.value)
})

// Best-effort client-side origin guard. The authoritative protection is the
// `frame-ancestors` CSP header served on /embed (added at deploy); this just
// gives a clean message when embedding is off or the framing site isn't allowed.
// Match a parent origin against a CSP-style source (supports a single "*." wildcard
// label, e.g. https://*.loampassmx.com), mirroring the server frame-ancestors list.
function originMatches(parent: string, pattern: string): boolean {
    if (pattern === parent) return true
    const m = pattern.match(/^(https?:\/\/)\*\.(.+)$/i)
    if (!m) return false
    const suffix = `${m[1]}${m[2]}`.toLowerCase()       // https://loampassmx.com
    const dotted = `${m[1]}`.toLowerCase()
    const p = parent.toLowerCase()
    // Allow the apex itself and any single-or-multi-level subdomain of it.
    return p === suffix || p.startsWith(dotted) && p.endsWith(`.${m[2].toLowerCase()}`)
}

function ancestorAllowed(): boolean {
    // Effective allow-list = this tenant's own origins ∪ first-party global origins.
    const allowed = [...branding.embedAllowedOrigins, ...branding.globalEmbedAllowedOrigins]

    // Check the ENTIRE ancestor chain, not just the immediate parent. Site builders
    // (Wix, Squarespace, GoDaddy, ...) wrap a custom-HTML embed in their OWN sandboxed
    // iframe served from a platform origin (e.g. *.filesusr.com), so the immediate parent
    // is that platform origin while the track's real site sits at the TOP of the chain.
    // Matching any ancestor means a track only ever has to whitelist their own domain —
    // no need to discover the platform's internal iframe origin.
    const origins: string[] = []
    const ao = (window.location as any).ancestorOrigins as DOMStringList | undefined
    if (ao && ao.length) {
        for (let i = 0; i < ao.length; i++) origins.push(ao[i])
    } else if (document.referrer) {
        // Firefox has no ancestorOrigins; the referrer is the immediate parent only.
        try { origins.push(new URL(document.referrer).origin) } catch { /* ignore */ }
    }
    if (origins.length === 0) return true     // not framed (direct load) — allow
    // Allow if any ancestor origin is on the list. (The authoritative control is the
    // server-stamped frame-ancestors CSP; this is the friendly in-app fallback.)
    return origins.some(o => allowed.some(a => originMatches(o, a)))
}

const embedBlocked = computed(() => {
    if (!isEmbed.value || !branding.loaded) return false
    // Internal dashboard preview (?preview=1): always render so an admin can see the
    // widget before enabling embedding / whitelisting. Safe because the authoritative
    // guard against real external framing is the server's CSP frame-ancestors header,
    // not this client check.
    if (route.query.preview) return false
    if (!branding.embedEnabled) return true
    return !ancestorAllowed()
})
const embedBlockedMessage = computed(() =>
    branding.embedEnabled
        ? 'This site is not authorized to embed this content.'
        : 'Embedding is not enabled for this track.')

// Frame id stamped by embed.js (?rpfid=) so the parent resizes the matching
// iframe when several widgets for the same tenant share one page.
const embedFrameId: string | null = (() => {
    try { return new URLSearchParams(window.location.search).get('rpfid') } catch { return null }
})()

// Auto-resize: report content height up to the embedding page so embed.js can
// size the iframe (no inner scrollbar). Height-only message; safe to broadcast.
//
// Measure the BODY, not documentElement.scrollHeight: the root element always fills
// the viewport, and inside an iframe the viewport IS the iframe, so a widget shorter
// than its frame would report the frame's own height straight back and stay stuck at
// whatever height it happened to start at (the membership widget is 330px of content
// that latched to the 400px load placeholder forever). The body is content-sized
// (see the html.rp-embed reset), so its box is the honest number.
function postEmbedHeight() {
    if (!isEmbed.value) return
    const body = document.body
    const height = Math.ceil(Math.max(body.getBoundingClientRect().height, body.scrollHeight))
    window.parent?.postMessage(
        { type: 'ridepass:resize', height, frameId: embedFrameId }, '*')
}
let resizeObserver: ResizeObserver | null = null

// Front-door redirect: for custom-domain / embedded clients, the {subdomain}.ridepass.io
// public pages forward to the track's real home. Staff/API/embed/admin paths are never
// redirected so the subdomain stays the always-on tools surface (login, check-in, counter,
// the embed iframe source, the API). Client-side for now; a 301 at the edge is the harden.
const STAFF_PATH_PREFIXES = ['/Admin', '/SuperAdmin', '/Login', '/ResetPassword', '/VerifyEmail', '/redeem', '/embed', '/User']
function frontDoorTarget(): string | null {
    // Only ever redirect from a *.ridepass.io subdomain (never from the custom domain itself).
    if (!tenantHelper.getSubdomain()) return null
    if (branding.clientType === 'embedded' && branding.externalHomeUrl) {
        const u = branding.externalHomeUrl
        return /^https?:\/\//i.test(u) ? u : `https://${u}`
    }
    if (branding.clientType === 'custom_domain' && branding.customDomainVerified && branding.customDomain) {
        return `${window.location.protocol}//${branding.customDomain}${route.fullPath}`
    }
    return null
}
function maybeRedirectFrontDoor() {
    if (!branding.loaded) return
    if (route.query.preview !== undefined) return   // let admins preview without bouncing
    const p = route.path
    if (STAFF_PATH_PREFIXES.some(pre => p === pre || p.startsWith(pre + '/'))) return
    // An embedded client can opt to send apex event clicks to the hosted event page
    // instead of their own site; in that case let /Event/:id render on the subdomain.
    if (branding.clientType === 'embedded' && branding.embedEventTarget === 'ridepass'
        && (p === '/Event' || p.startsWith('/Event/'))) return
    const target = frontDoorTarget()
    if (target) window.location.replace(target)
}

// Apex root URL derived from the current host (strip a leading subdomain), used
// by the "not available" page's link out.
const apexUrl = computed(() => {
    const host = window.location.hostname
    const labels = host.split('.')
    const apexHost = (host === 'localhost' || labels.length <= 2) ? host : labels.slice(-2).join('.')
    const port = window.location.port ? `:${window.location.port}` : ''
    return `${window.location.protocol}//${apexHost}${port}/`
})

onMounted(() => {
    loadBranding()
    // Observe body size and report height to the parent frame whenever embedded.
    resizeObserver = new ResizeObserver(() => postEmbedHeight())
    resizeObserver.observe(document.body)
})

// Re-report height on navigation and once branding (and thus content) loads.
watch([() => route.fullPath, () => branding.loaded], () => {
    maybeRedirectFrontDoor()
    if (isEmbed.value) setTimeout(postEmbedHeight, 50)
}, { immediate: true })

// Relative luminance (WCAG) of a #rrggbb color, 0 (black) .. 1 (white).
function relLuminance(hex: string): number {
    const m = /^#?([0-9a-f]{6})$/i.exec(hex.trim())
    if (!m) return 0
    const n = parseInt(m[1], 16)
    const chan = (c: number) => {
        const s = c / 255
        return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4)
    }
    const r = chan((n >> 16) & 0xff), g = chan((n >> 8) & 0xff), b = chan(n & 0xff)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

// Pick the foreground text color (black or white) that has the higher WCAG contrast
// against the given background. Vuetify's own on-color heuristic can return white for
// mid-tone brand colors (e.g. #0288D1 -> white is only 3.86:1, below AA 4.5:1); choosing
// the higher-contrast option keeps text legible on whatever brand color a tenant sets.
function readableOn(bg: string): string {
    const l = relLuminance(bg)
    const contrastWhite = (1 + 0.05) / (l + 0.05)
    const contrastBlack = (l + 0.05) / 0.05
    return contrastBlack >= contrastWhite ? '#000000' : '#FFFFFF'
}

// Darken a color just enough that it meets WCAG AA (>= 4.5:1) as TEXT on a white
// surface. The tenant brand color is fine as a button/background fill, but a mid-tone
// brand hue used as colored link/button text on white can fall short (e.g. #0288D1 is
// only 3.86:1). Returns the original when it already passes; otherwise scales toward
// black until the contrast target is reached. Used only for primary-colored button text,
// so the brand fill color itself is untouched.
function darkenForTextOnWhite(hex: string, target = 4.5): string {
    const m = /^#?([0-9a-f]{6})$/i.exec(hex.trim())
    if (!m) return hex
    const maxL = (1 + 0.05) / target - 0.05   // max luminance that still meets target on white
    if (relLuminance(hex) <= maxL) return `#${m[1].toLowerCase()}`
    const n = parseInt(m[1], 16)
    const r = (n >> 16) & 0xff, g = (n >> 8) & 0xff, b = n & 0xff
    const toHex = (rr: number, gg: number, bb: number) =>
        '#' + [rr, gg, bb].map(c => Math.max(0, Math.round(c)).toString(16).padStart(2, '0')).join('')
    for (let k = 0.97; k > 0; k -= 0.03) {
        const cand = toHex(r * k, g * k, b * k)
        if (relLuminance(cand) <= maxL) return cand
    }
    return '#000000'
}

watchEffect(() => {
    if (!branding.loaded) return
    const themeName = branding.themeMode === 'dark' ? 'tenantDark' : 'tenant'
    const target = theme.themes.value[themeName]
    if (!target) return
    // Direct mutation: Vuetify's theme stylesheet recomputes via deep reactivity.
    // Reassigning the whole theme object with a plain POJO loses reactive wiring
    // and Vuetify may fall back to the initial config.
    target.colors.primary = branding.primaryColor
    target.colors.secondary = branding.secondaryColor
    target.colors.accent = branding.accentColor
    // Set the on-* text colors explicitly so buttons/chips using these brand colors
    // keep accessible contrast regardless of the tenant's chosen hue.
    target.colors['on-primary'] = readableOn(branding.primaryColor)
    target.colors['on-secondary'] = readableOn(branding.secondaryColor)
    target.colors['on-accent'] = readableOn(branding.accentColor)
    // Accessible shade of primary for use as TEXT on light surfaces (text/outlined/plain
    // buttons + links). Consumed by the global .v-btn.text-primary rule below.
    document.documentElement.style.setProperty(
        '--rp-primary-text-on-light', darkenForTextOnWhite(branding.primaryColor))
    theme.global.name.value = themeName
})
</script>

<style scoped>
/* Min content height = viewport minus the footer height, so on short pages the
   footer rests at the bottom without forcing a scrollbar. NOT a sticky footer:
   it still follows the content and scrolls off on tall pages. The footer height
   is a fixed estimate (it restacks taller on narrow widths, hence the
   breakpoint); tweak --footer-h if it looks off on your content. */
:deep(.v-main) {
    --footer-h: 360px;
    min-height: calc(100vh - var(--footer-h));
    min-height: calc(100dvh - var(--footer-h));
}
@media (max-width: 600px) {
    :deep(.v-main) {
        --footer-h: 620px;
    }
}

/* Embed mode: no footer, so drop the footer-based min-height and let the content
   size naturally (the iframe auto-resizes to it). */
.embed-mode :deep(.v-main) {
    min-height: 0;
}
.embed-blocked {
    padding: 32px 24px;
    text-align: center;
}

.branding-splash {
    position: fixed;
    inset: 0;
    background: #ffffff;
    z-index: 10000;
    display: flex;
    align-items: center;
    justify-content: center;
}
.branding-splash-content {
    text-align: center;
}
.branding-splash-icon {
    color: #888;
}
.branding-splash-img {
    width: 80px;
    max-width: 60vw;
    height: auto;
    display: block;
    margin: 0 auto;
    /* The helmet stays intentionally faint; the wordmark below carries legibility. */
    opacity: 0.3;
}
.branding-splash-text {
    font-size: 22px;
    /* #666 on white is 5.7:1 (passes WCAG AA). The old #888 under a 0.22 content
       opacity was far below AA; keep the splash light but the brand name legible. */
    color: #666666;
    margin-top: 6px;
    letter-spacing: 0.18em;
    font-weight: 500;
}
/* Fade out only — the splash never fades in (it's there from first paint).
   Long, gentle ease-out so the tenant colors don't rush in. */
.splash-fade-leave-active {
    transition: opacity 1200ms cubic-bezier(0.25, 0.1, 0.25, 1);
}
.splash-fade-leave-to {
    opacity: 0;
}

/* "Tenant not available" full-screen page (unpublished / inactive). */
.tenant-unavailable {
    position: fixed;
    inset: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    background: #ffffff;
    padding: 24px;
    z-index: 9999;
}
.tenant-unavailable-content {
    text-align: center;
    max-width: 420px;
}
.tenant-unavailable-img {
    width: 64px;
    height: auto;
    opacity: 0.5;
}
</style>

<!-- Unscoped: applies to Vuetify-generated button markup across the app. -->
<style>
/* Primary-colored button TEXT (text/outlined/plain variants get the `.text-primary`
   class; filled variants use `.bg-primary` instead and are unaffected) uses an
   accessibility-darkened shade of the tenant primary on light surfaces, so colored
   links/CTAs like "All events" meet WCAG AA against white. Falls back to the raw
   theme primary if the variable hasn't been set yet. */
.v-btn.text-primary {
    color: var(--rp-primary-text-on-light, rgb(var(--v-theme-primary))) !important;
}

/* ── Embedded widgets (html.rp-embed, stamped by the watchEffect in the script) ──
   Nothing inside a widget may size itself from the VIEWPORT. The host iframe's height
   is driven by this document's scrollHeight (postEmbedHeight), and inside an iframe
   100vh IS the iframe's own height, so a viewport-based min-height feeds back on
   itself: the reported height can never fall below the current iframe height, so every
   widget latches to a viewport-sized block (~1000px of dead space under short widgets)
   and a widget whose real content is taller ends up scrolling inside a too-short frame.
   Vuetify ships exactly that on .v-application__wrap, so neutralize it here and let the
   measurement be pure content. */
html.rp-embed,
html.rp-embed body {
    height: auto;
    min-height: 0;
    /* Body margin would be added to scrollHeight on every resize round-trip. */
    margin: 0;
}
html.rp-embed .v-application,
html.rp-embed .v-application__wrap {
    min-height: 0 !important;
}

/* No scrollbar inside the frame: the iframe auto-sizes to the content above, so the
   bar is redundant, and on Windows its arrows are an obvious "this is an iframe" tell
   on the track's own site. Wheel/touch/programmatic scrolling still work. Pairs with
   scrolling="no" in embed.js (some engines ignore one or the other). */
html.rp-embed,
html.rp-embed body {
    scrollbar-width: none;          /* Firefox */
    -ms-overflow-style: none;       /* legacy Edge */
}
html.rp-embed::-webkit-scrollbar,
html.rp-embed body::-webkit-scrollbar {
    width: 0;
    height: 0;
    display: none;                  /* Chrome / Safari */
}
</style>
