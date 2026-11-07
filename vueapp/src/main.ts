import { createApp } from 'vue'
import App from './App.vue'
import router from './router/router'
import { registerKioskRoutes } from './router/kioskRoutes'
import axios from 'axios'
import mitt from 'mitt'
import dayjs from 'dayjs'
import utcPlugin from 'dayjs/plugin/utc'
import timezonePlugin from 'dayjs/plugin/timezone'
import tenantHelper from './helpers/TenantHelper'
import authHelper from './helpers/AuthHelper'

dayjs.extend(utcPlugin)
dayjs.extend(timezonePlugin)

// Vuetify
import 'vuetify/styles'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import '@mdi/font/css/materialdesignicons.css'

// Styles
import './styles/index.scss'

const emitter = mitt()

// Design-system palettes. The dark set IS the mockup (near-black surfaces, white
// action color); the light set is its mirror for tenants whose Branding → Theme is
// "light": white cards on a pale grey page, near-black action color. Both mirror the
// --rp-* tokens in styles/index.scss (`:root` dark, `[data-rp-theme="light"]` light).
//
// primary / secondary / accent here are the APEX defaults. Tenant subdomains overwrite
// them at runtime from their Branding settings (see the watchEffect in App.vue).
function rpDarkColors() {
    return {
        background: '#0a0a0a',
        surface: '#131313',
        'surface-bright': '#1e1e1e',
        'surface-light': '#191919',
        'surface-variant': '#1e1e1e',
        'on-surface-variant': '#a6a6a6',
        'on-background': '#f5f5f5',
        'on-surface': '#f5f5f5',
        primary: '#f5f5f5',
        'on-primary': '#111111',
        secondary: '#1a1a1a',
        'on-secondary': '#f5f5f5',
        accent: '#e6e6e6',
        'on-accent': '#111111',
        error: '#ff6b6b',
        'on-error': '#160606',
        info: '#5aa8ff',
        'on-info': '#06101a',
        success: '#5dd39e',
        'on-success': '#06160e',
        warning: '#f0b35a',
        'on-warning': '#1a1206',
    }
}
function rpLightColors() {
    return {
        background: '#f6f6f6',
        surface: '#ffffff',
        'surface-bright': '#ffffff',
        'surface-light': '#f0f0f0',
        'surface-variant': '#e9e9e9',
        'on-surface-variant': '#5f5f5f',
        'on-background': '#111111',
        'on-surface': '#111111',
        primary: '#111111',
        'on-primary': '#ffffff',
        secondary: '#f0f0f0',
        'on-secondary': '#111111',
        accent: '#333333',
        'on-accent': '#ffffff',
        error: '#d64545',
        'on-error': '#ffffff',
        info: '#2a78d6',
        'on-info': '#ffffff',
        success: '#178a52',
        'on-success': '#ffffff',
        warning: '#b7791f',
        'on-warning': '#ffffff',
    }
}
function rpVariables(dark: boolean) {
    return {
        'border-color': dark ? '#ffffff' : '#000000',
        'border-opacity': 0.08,
        'high-emphasis-opacity': 1,
        'medium-emphasis-opacity': 0.65,
        'disabled-opacity': 0.4,
        'idle-opacity': 0.06,
        'hover-opacity': 0.06,
        'focus-opacity': 0.1,
        'selected-opacity': 0.1,
        'activated-opacity': 0.12,
        'pressed-opacity': 0.14,
        'dragged-opacity': 0.1,
        'theme-kbd': dark ? '#191919' : '#f0f0f0',
        'theme-on-kbd': dark ? '#f5f5f5' : '#111111',
        'theme-code': dark ? '#191919' : '#f0f0f0',
        'theme-on-code': dark ? '#f5f5f5' : '#111111',
    }
}

const vuetify = createVuetify({
    components,
    directives,
    // Match the LoamPass-style compact-outlined look on every form field by default.
    // Per-field overrides still work, e.g. <v-text-field variant="filled" :hide-details="false" />.
    //
    // hideDetails is 'auto', NOT true. With `true` Vuetify does not render the details block at
    // all, which silently swallowed two things the author explicitly asked for: every `hint` /
    // `persistent-hint` in the app (25+ files write one and none of them appeared), and every
    // validation message from `:rules`. It also removed the space under a field, so a stack of
    // inputs sat flush against each other and read as cramped, worst on a tall control like a
    // chips combobox.
    //
    // 'auto' keeps the tight look exactly where there is nothing to say: no hint and no error
    // still means no details block. It only takes up room when there is a message to show, which
    // is the point of having written one.
    defaults: {
        VTextField:    { variant: 'outlined', density: 'compact', hideDetails: 'auto' },
        VTextarea:     { variant: 'outlined', density: 'compact', hideDetails: 'auto' },
        VSelect:       { variant: 'outlined', density: 'compact', hideDetails: 'auto' },
        VAutocomplete: { variant: 'outlined', density: 'compact', hideDetails: 'auto' },
        VCombobox:     { variant: 'outlined', density: 'compact', hideDetails: 'auto' },
        // Toasts at the top of the viewport — buttons can be anywhere on a long page.
        // Per-snackbar overrides (e.g. <v-snackbar location="bottom">) still take precedence.
        VSnackbar:     { location: 'top', timeout: 4000 },
    },
    theme: {
        // `tenant` = light, `tenantDark` = dark. App.vue picks one from the tenant's
        // Branding → Theme setting (the apex site is always dark, matching the mockup)
        // and writes the tenant's primary/secondary/accent into it.
        defaultTheme: 'tenantDark',
        themes: {
            tenant: {
                dark: false,
                colors: rpLightColors(),
                variables: rpVariables(false),
            },
            tenantDark: {
                dark: true,
                colors: rpDarkColors(),
                variables: rpVariables(true),
            }
        }
    }
})

// Axios interceptor: attach JWT and tenant subdomain
axios.interceptors.request.use(
    (config) => {
        // Prefer the in-memory token over localStorage. In a third-party iframe (the embed
        // widgets on a track's own site) storage writes can be blocked or partitioned, so an
        // inline login sets authHelper's reactive state.token but the localStorage write is
        // dropped. Reading localStorage-only here would then send no Authorization header and
        // the request 401s ("Could not start checkout"). getToken() is seeded from localStorage
        // on load, so the persisted-session case is unchanged; localStorage stays as a fallback.
        const token = authHelper.getToken() ?? localStorage.getItem('token')
        if (token) {
            config.headers.Authorization = `Bearer ${token}`
        }
        const subdomain = tenantHelper.getSubdomain()
        if (subdomain) {
            config.headers['X-Tenant-Subdomain'] = subdomain
        }
        return config
    },
    (error) => {
        return Promise.reject(error)
    }
)

// Sliding sessions: the API re-issues the JWT on activity via this header; adopting
// it keeps an active user signed in indefinitely, while idle sessions expire.
function adoptRefreshedToken(headers: Record<string, unknown> | undefined) {
    const refreshed = headers?.['x-refreshed-token']
    if (typeof refreshed === 'string' && refreshed) authHelper.setToken(refreshed)
    // During impersonation the impersonator's own stashed token slides too, so
    // "stop impersonation" restores a live session no matter how long it lasted.
    const original = headers?.['x-refreshed-original-token']
    if (typeof original === 'string' && original) authHelper.refreshStashedOriginal(original)
}

// Axios response interceptor for token refresh + 401 handling
axios.interceptors.response.use(
    (response) => {
        adoptRefreshedToken(response.headers)
        return response
    },
    (error) => {
        if (error.response) {
            // Even a failed action (400/403/etc.) proves the user is active, so keep sliding.
            adoptRefreshedToken(error.response.headers)
            if (error.response.status === 401) {
                console.error('[RidePass] 401 Unauthorized:', {
                    url: error.config?.url,
                    method: error.config?.method,
                    hadAuthHeader: !!error.config?.headers?.Authorization,
                    tenantSubdomainHeader: error.config?.headers?.['X-Tenant-Subdomain'],
                    responseBody: error.response.data,
                })
                // Full logout so the NavBar's reactive isAuthenticated/isAdmin computed values update.
                authHelper.logout()
                // Preserve where they were so Login can return them after re-auth (skip when
                // already on Login to avoid a self-referential next).
                const current = router.currentRoute.value
                router.push(current.path !== '/Login'
                    ? { path: '/Login', query: { next: current.fullPath } }
                    : '/Login')
            } else if (error.response.status === 403) {
                console.warn('[RidePass] 403 Forbidden:', {
                    url: error.config?.url,
                    method: error.config?.method,
                    responseBody: error.response.data,
                })
            }
        }
        return Promise.reject(error)
    }
)

// Super-admin "Preview" bridge: a JWT handed to a tenant subdomain via the URL
// fragment (#preview_token=...). Adopt it into this origin's session and strip
// it from the URL so it doesn't linger in the address bar / history. Runs before
// mount so the first API calls (branding, etc.) already carry the token.
const previewMatch = window.location.hash.match(/[#&]preview_token=([^&]+)/)
if (previewMatch) {
    // Optional label so the impersonation banner can name who we're acting as.
    const labelMatch = window.location.hash.match(/[#&]preview_label=([^&]+)/)
    try {
        const label = labelMatch ? decodeURIComponent(labelMatch[1]) : null
        authHelper.adoptToken(decodeURIComponent(previewMatch[1]), label)
    } catch { /* ignore a malformed token */ }
    history.replaceState(null, '', window.location.pathname + window.location.search)
}

const app = createApp(App)

app.config.globalProperties.emitter = emitter

app.use(vuetify)
registerKioskRoutes(router)
app.use(router)

app.mount('#app')
