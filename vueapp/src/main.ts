import { createApp } from 'vue'
import App from './App.vue'
import router from './router/router'
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

const vuetify = createVuetify({
    components,
    directives,
    // Match the LoamPass-style compact-outlined look on every form field by default.
    // Per-field overrides still work, e.g. <v-text-field variant="filled" :hide-details="false" />.
    defaults: {
        VTextField:    { variant: 'outlined', density: 'compact', hideDetails: true },
        VTextarea:     { variant: 'outlined', density: 'compact', hideDetails: true },
        VSelect:       { variant: 'outlined', density: 'compact', hideDetails: true },
        VAutocomplete: { variant: 'outlined', density: 'compact', hideDetails: true },
        VCombobox:     { variant: 'outlined', density: 'compact', hideDetails: true },
        // Toasts at the top of the viewport — buttons can be anywhere on a long page.
        // Per-snackbar overrides (e.g. <v-snackbar location="bottom">) still take precedence.
        VSnackbar:     { location: 'top', timeout: 4000 },
    },
    theme: {
        defaultTheme: 'tenant',
        themes: {
            tenant: {
                dark: false,
                colors: {
                    primary: '#1976D2',
                    secondary: '#424242',
                    accent: '#82B1FF',
                    error: '#FF5252',
                    info: '#2196F3',
                    success: '#4CAF50',
                    warning: '#FFC107',
                }
            },
            tenantDark: {
                dark: true,
                colors: {
                    primary: '#1976D2',
                    secondary: '#424242',
                    accent: '#82B1FF',
                    error: '#FF5252',
                    info: '#2196F3',
                    success: '#4CAF50',
                    warning: '#FFC107',
                }
            }
        }
    }
})

// Axios interceptor: attach JWT and tenant subdomain
axios.interceptors.request.use(
    (config) => {
        const token = localStorage.getItem('token')
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

// Axios response interceptor for 401 handling
axios.interceptors.response.use(
    (response) => response,
    (error) => {
        if (error.response) {
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
                router.push('/Login')
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

const app = createApp(App)

app.config.globalProperties.emitter = emitter

app.use(vuetify)
app.use(router)

app.mount('#app')
