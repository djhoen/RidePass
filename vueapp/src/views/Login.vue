<template>
    <div class="login-page" :style="brandStyle">
        <!-- Full-bleed tenant hero behind a legibility gradient; the form floats on a
             glass card in the middle. No side panel = nothing to clip at any size. -->
        <div class="login-overlay"></div>

        <div class="login-form-wrap">
            <!-- Brand block above the card: always visible, photo gets the full stage. -->
            <div class="login-brand-block text-center mb-5">
                <img v-if="branding.logoUrl" :src="branding.logoUrl" :alt="branding.displayName"
                    class="login-brand-logo" />
                <div v-else class="login-brand-name font-display">{{ branding.displayName }}</div>
                <p v-if="branding.tagline" class="login-brand-tagline">{{ branding.tagline }}</p>
            </div>

            <div class="login-card">
                <h1 class="text-h5 font-weight-bold mb-1">Welcome back</h1>
                <p class="text-body-2 text-medium-emphasis mb-6">Sign in to your account to continue.</p>

                <v-form @submit.prevent="login">
                    <v-text-field v-model="form.email" label="Email" type="email" autocomplete="email"
                        prepend-inner-icon="mdi-email-outline" variant="outlined" density="comfortable"></v-text-field>
                    <v-text-field v-model="form.password" :type="showPassword ? 'text' : 'password'"
                        label="Password" autocomplete="current-password" class="mt-4"
                        prepend-inner-icon="mdi-lock-outline" variant="outlined" density="comfortable"
                        :append-inner-icon="showPassword ? 'mdi-eye-off-outline' : 'mdi-eye-outline'"
                        @click:append-inner="showPassword = !showPassword"></v-text-field>

                    <div class="d-flex align-center justify-space-between mb-4">
                        <v-checkbox v-model="form.rememberMe" color="primary" density="compact"
                            hide-details label="Keep me signed in"
                            hint="Skip this on a shared counter machine." persistent-hint></v-checkbox>
                        <router-link to="/ResetPassword" class="login-link text-body-2">Forgot password?</router-link>
                    </div>

                    <v-btn type="submit" color="primary" block size="large" :loading="loading"
                        class="text-none" rounded="lg">Sign in</v-btn>
                </v-form>

                <!-- Shown only after a login attempt fails because the email is not yet
                     verified. Lets the rider request a fresh verification link inline. -->
                <v-alert v-if="showResend" type="info" variant="tonal" density="compact" class="mt-4">
                    <div class="text-body-2 mb-2">Your email isn't verified yet.</div>
                    <v-btn color="primary" variant="text" size="small" class="text-none px-0"
                        :loading="resending" @click="resendVerification">Resend verification email</v-btn>
                </v-alert>

                <div class="text-center text-body-2 text-medium-emphasis mt-6">
                    No account yet? <router-link to="/SignUp" class="login-link">Create one</router-link>.
                </div>
            </div>
        </div>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000" location="top">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { UserService } from '@/services/UserService'
import authHelper from '@/helpers/AuthHelper'
import tenantHelper from '@/helpers/TenantHelper'
import { branding } from '@/stores/branding'

const router = useRouter()
const route = useRoute()
const userService = new UserService()

const loading = ref(false)
const showPassword = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('error')
const showResend = ref(false)
const resending = ref(false)

const form = ref({
    email: '',
    password: '',
    // Extends the SESSION (21 days) rather than storing any credential. Off by default: the
    // shared machine behind a counter should stay short-lived unless someone opts in.
    rememberMe: false,
})

const brandStyle = computed(() =>
    branding.heroImageUrl ? { backgroundImage: `url(${branding.heroImageUrl})` } : {})

function isTenantStaffRole(role: string): boolean {
    return ['tenant_admin', 'tenant_manager', 'tenant_cashier', 'tenant_shop_cashier',
            'tenant_scanner', 'tenant_accountant', 'tenant_staff'].includes(role)
}

async function login() {
    try {
        loading.value = true
        const response = await userService.login(form.value)
        const payload = response.data.data
        authHelper.setToken(payload.token)
        authHelper.setUserId(payload.userId)
        authHelper.setRole(payload.role)
        if (payload.role === 'super_admin') {
            router.push('/SuperAdmin/Analytics')
        } else if (isTenantStaffRole(payload.role)) {
            router.push('/Admin/Dashboard')
        } else {
            // If we were sent here to finish a gated task (waiver, waitlist confirm,
            // membership), return there. Only same-origin relative paths are honored,
            // so a crafted ?next=//evil.com can't redirect off-site.
            const raw = route.query.next
            const next = typeof raw === 'string' && raw.startsWith('/') && !raw.startsWith('//') ? raw : null
            if (next) {
                router.push(next)
            } else {
                // Riders signing in on the apex (no tenant subdomain) land on
                // the cross-tenant Upcoming feed. Same role signing in on a
                // tenant subdomain still goes to the tenant home (existing flow).
                router.push(tenantHelper.getSubdomain() ? '/' : '/User/Upcoming')
            }
        }
    } catch (error: any) {
        const message = error.response?.data?.error || 'Login failed.'
        snackbarText.value = message
        snackbarColor.value = 'error'
        snackbar.value = true
        // Surface a resend action when the failure is specifically an unverified email.
        showResend.value = message.toLowerCase().includes('verify your email')
    } finally {
        loading.value = false
    }
}

async function resendVerification() {
    try {
        resending.value = true
        await userService.resendVerification(form.value.email)
    } catch { /* endpoint always returns 200; ignore */ }
    finally {
        resending.value = false
    }
    snackbarText.value = 'Verification link sent.'
    snackbarColor.value = 'success'
    snackbar.value = true
}
</script>

<style scoped>
/* Full-bleed hero: the tenant photo covers the page; a brand-tinted gradient keeps the
   card and brand block legible over any photo. Falls back to the gradient alone when no
   hero image is set (brandStyle adds background-image only when one exists). */
.login-page {
    position: relative;
    min-height: 100vh;
    display: flex;
    flex-direction: column;
    background-size: cover;
    background-position: center;
    background-color: rgb(var(--v-theme-secondary));
}
.login-overlay {
    position: absolute;
    inset: 0;
    background:
        radial-gradient(ellipse at center, rgba(14, 17, 24, 0.35) 0%, rgba(14, 17, 24, 0.72) 100%),
        linear-gradient(160deg,
            color-mix(in srgb, rgb(var(--v-theme-primary)) 45%, transparent) 0%,
            rgba(14, 17, 24, 0.35) 60%);
}

/* ── Brand block (above the card) ───────────────────────────────────────── */
.login-brand-block {
    color: #fff;
    text-shadow: 0 2px 14px rgba(0, 0, 0, 0.55);
    max-width: 460px;
}
.login-brand-logo {
    max-height: 72px;
    max-width: min(260px, 100%);
    width: auto;
    filter: drop-shadow(0 2px 10px rgba(0, 0, 0, 0.45));
}
.login-brand-name {
    font-size: clamp(2rem, 5vw, 2.75rem);
    font-weight: 700;
    line-height: 1.05;
    overflow-wrap: break-word;
    padding: 0 0.1em;   /* slanted display fonts clip their edge glyphs without this */
}
.login-brand-tagline {
    font-size: clamp(1rem, 2vw, 1.2rem);
    font-weight: 500;
    opacity: 0.92;
    margin: 0.5rem 0 0;
    overflow-wrap: break-word;
}

/* ── Floating glass card ────────────────────────────────────────────────── */
.login-form-wrap {
    position: relative;   /* above the overlay */
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    padding: 2.5rem 1.25rem;
}
.login-card {
    width: 100%;
    max-width: 420px;
    /* Theme surface at ~86% over a blur: light theme reads as frosted white, dark theme
       as smoked glass — Vuetify inputs inherit the right on-surface text either way. */
    background: color-mix(in srgb, rgb(var(--v-theme-surface)) 86%, transparent);
    color: rgb(var(--v-theme-on-surface));
    backdrop-filter: blur(14px);
    -webkit-backdrop-filter: blur(14px);
    border: 1px solid color-mix(in srgb, rgb(var(--v-theme-surface)) 40%, transparent);
    border-radius: 18px;
    box-shadow: 0 18px 50px rgba(0, 0, 0, 0.35);
    padding: 2.5rem 2.25rem;
}
.login-link {
    color: rgb(var(--v-theme-primary));
    text-decoration: none;
    font-weight: 600;
}
.login-link:hover { text-decoration: underline; }

@media (max-width: 600px) {
    .login-form-wrap { padding: 1.75rem 1rem; }
    .login-card { padding: 1.75rem 1.25rem; }
}
</style>
