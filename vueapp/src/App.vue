<template>
    <v-app :class="{ 'embed-mode': isEmbed }">
        <!-- Pre-branding splash. Sits above everything in a neutral white state with a
             faint logo so the user doesn't see the default Vuetify theme flash before
             the tenant colors arrive. Vue's <transition> handles the fade-out the
             moment branding.loaded flips true. -->
        <transition name="splash-fade">
            <div v-if="!branding.loaded" class="branding-splash">
                <div class="branding-splash-content">
                    <img :src="splashLogo" alt="RidePass" class="branding-splash-img" />
                    <div class="branding-splash-text">RidePass</div>
                </div>
            </div>
        </transition>

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
            <ImpersonationBanner />
            <NavBar v-if="!$route.meta.hideNav" />
            <v-main>
                <div v-if="embedBlocked" class="embed-blocked">
                    <p class="text-body-2 text-medium-emphasis">{{ embedBlockedMessage }}</p>
                </div>
                <router-view v-else />
            </v-main>
            <Footer v-if="!$route.meta.hideFooter" />
            <ConfirmDialog />
        </template>
    </v-app>
</template>

<script setup lang="ts">
import { onMounted, watch, watchEffect, computed } from 'vue'
import { useRoute } from 'vue-router'
import { useTheme } from 'vuetify'
import NavBar from './components/NavBar.vue'
import Footer from './components/Footer.vue'
import ImpersonationBanner from './components/ImpersonationBanner.vue'
import ConfirmDialog from './components/ConfirmDialog.vue'
import { branding, loadBranding } from './stores/branding'
import tenantHelper from './helpers/TenantHelper'
import splashLogo from './assets/helmet.png'

const theme = useTheme()
const route = useRoute()

// Embed mode: chromeless widget framed on a track's own site (via embed.js).
const isEmbed = computed(() => !!route.meta.embed)

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
    const ao = (window.location as any).ancestorOrigins as DOMStringList | undefined
    let parentOrigin: string | null = ao && ao.length ? ao[0] : null
    if (!parentOrigin && document.referrer) {
        try { parentOrigin = new URL(document.referrer).origin } catch { parentOrigin = null }
    }
    if (!parentOrigin) return true            // not framed (direct load) — allow
    // Fail closed: framed but on no allowed origin is blocked. (The authoritative
    // control is the server-stamped frame-ancestors CSP; this is the friendly fallback.)
    return allowed.some(a => originMatches(parentOrigin as string, a))
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
function postEmbedHeight() {
    if (!isEmbed.value) return
    window.parent?.postMessage(
        { type: 'ridepass:resize', height: document.documentElement.scrollHeight, frameId: embedFrameId }, '*')
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
    opacity: 0.22;
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
}
.branding-splash-text {
    font-size: 22px;
    color: #888;
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
