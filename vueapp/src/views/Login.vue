<template>
    <div class="login-page">
        <!-- Brand panel (md+). Tenant hero photo behind a brand-colored gradient,
             with the logo / name / tagline. Falls back to a flat gradient when no
             hero image is set. -->
        <div class="login-brand d-none d-md-flex" :style="brandStyle">
            <div class="login-brand-overlay"></div>
            <div class="login-brand-content">
                <img v-if="branding.logoUrl" :src="branding.logoUrl" :alt="branding.displayName"
                    class="login-brand-logo" />
                <div v-else class="login-brand-name font-display">{{ branding.displayName }}</div>
                <p v-if="branding.tagline" class="login-brand-tagline">{{ branding.tagline }}</p>
                <p class="login-brand-welcome">
                    Sign in to manage your passes, reserve your spot, and check in fast at the gate.
                </p>
            </div>
        </div>

        <!-- Form panel -->
        <div class="login-form-wrap">
            <div class="login-card">
                <!-- Logo shows on mobile where the brand panel is hidden. -->
                <div class="d-md-none text-center mb-4">
                    <img v-if="branding.logoUrl" :src="branding.logoUrl" :alt="branding.displayName"
                        class="login-mobile-logo" />
                    <div v-else class="text-h5 font-weight-bold font-display">{{ branding.displayName }}</div>
                </div>

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

                    <div class="d-flex justify-end mb-4">
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
    password: ''
})

const brandStyle = computed(() =>
    branding.heroImageUrl ? { backgroundImage: `url(${branding.heroImageUrl})` } : {})

function isTenantStaffRole(role: string): boolean {
    return ['tenant_admin', 'tenant_manager', 'tenant_cashier', 'tenant_scanner',
            'tenant_accountant', 'tenant_staff'].includes(role)
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
.login-page {
    display: flex;
    min-height: 100vh;
    background: #f5f6f8;
}

/* ── Brand panel ────────────────────────────────────────────────────────── */
.login-brand {
    position: relative;
    flex: 1 1 50%;
    background-size: cover;
    background-position: center;
    background-color: rgb(var(--v-theme-secondary));
    display: flex;
    align-items: flex-end;
    overflow: hidden;
}
.login-brand-overlay {
    position: absolute;
    inset: 0;
    background: linear-gradient(150deg,
        color-mix(in srgb, rgb(var(--v-theme-primary)) 78%, #000) 0%,
        rgba(18, 22, 30, 0.86) 55%,
        rgba(18, 22, 30, 0.74) 100%);
}
.login-brand-content {
    position: relative;
    color: #fff;
    padding: 3.5rem;
    max-width: 520px;
}
.login-brand-logo {
    max-height: 64px;
    max-width: 240px;
    width: auto;
    margin-bottom: 1.25rem;
}
.login-brand-name {
    font-size: 2.75rem;
    font-weight: 700;
    line-height: 1;
    margin-bottom: 1rem;
}
.login-brand-tagline {
    font-size: 1.25rem;
    font-weight: 500;
    margin-bottom: 0.75rem;
    opacity: 0.95;
}
.login-brand-welcome {
    font-size: 1rem;
    opacity: 0.8;
    margin-bottom: 0;
    max-width: 420px;
}

/* ── Form panel ─────────────────────────────────────────────────────────── */
.login-form-wrap {
    flex: 1 1 50%;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 2rem 1.5rem;
}
.login-card {
    width: 100%;
    max-width: 400px;
    background: #fff;
    border-radius: 16px;
    box-shadow: 0 10px 40px rgba(0, 0, 0, 0.08);
    padding: 2.5rem 2.25rem;
}
.login-mobile-logo {
    max-height: 56px;
    max-width: 200px;
    width: auto;
}
.login-link {
    color: rgb(var(--v-theme-primary));
    text-decoration: none;
    font-weight: 600;
}
.login-link:hover { text-decoration: underline; }

@media (max-width: 600px) {
    .login-card {
        box-shadow: none;
        background: transparent;
        padding: 1.5rem 0.5rem;
    }
    .login-form-wrap { background: #fff; }
}
</style>
