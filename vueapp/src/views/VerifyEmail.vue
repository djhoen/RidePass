<template>
    <div class="login-page">
        <!-- Brand panel (md+), matching the Login layout. -->
        <div class="login-brand d-none d-md-flex" :style="brandStyle">
            <div class="login-brand-overlay"></div>
            <div class="login-brand-content rp-on-photo">
                <img v-if="branding.logoUrl" :src="branding.logoUrl" :alt="branding.displayName"
                    class="login-brand-logo" />
                <BrandLogo v-else-if="isApex" :height="48" class="login-brand-svg" />
                <div v-else class="login-brand-name font-display">{{ branding.displayName }}</div>
                <p v-if="branding.tagline" class="login-brand-tagline">{{ branding.tagline }}</p>
                <p class="login-brand-welcome">
                    Verify your email to activate your account and start riding.
                </p>
            </div>
        </div>

        <!-- Content panel -->
        <div class="login-form-wrap">
            <div class="login-card">
                <!-- Logo shows on mobile where the brand panel is hidden. -->
                <div class="d-md-none text-center mb-4">
                    <img v-if="branding.logoUrl" :src="branding.logoUrl" :alt="branding.displayName"
                        class="login-mobile-logo" />
                    <div v-else class="text-h5 font-weight-bold font-display">{{ branding.displayName }}</div>
                </div>

                <!-- Loading -->
                <div v-if="state === 'loading'" class="text-center py-8">
                    <v-progress-circular indeterminate color="primary" size="48"></v-progress-circular>
                    <p class="text-body-1 mt-4">Verifying your email...</p>
                </div>

                <!-- Success -->
                <div v-else-if="state === 'success'" class="text-center">
                    <v-icon color="success" size="64" class="mb-4">mdi-check-circle-outline</v-icon>
                    <h1 class="login-title font-display mb-1">Your email is verified</h1>
                    <p class="text-body-2 text-medium-emphasis mb-6">
                        You're all set. Sign in to start managing your passes.
                    </p>
                    <v-btn color="primary" variant="flat" block size="large" to="/Login">
                        Go to sign in
                    </v-btn>
                </div>

                <!-- Error (missing token or failed verification) -->
                <div v-else>
                    <div class="text-center mb-6">
                        <v-icon color="error" size="64" class="mb-4">mdi-alert-circle-outline</v-icon>
                        <h1 class="login-title font-display mb-1">Verification failed</h1>
                        <p class="text-body-2 text-medium-emphasis">{{ errorMessage }}</p>
                    </div>

                    <!-- Inline resend form, only when we have a token-style failure.
                         Hidden once a resend has been sent. -->
                    <template v-if="resendSent">
                        <v-alert type="success" variant="tonal" density="compact" class="mb-2">
                            If that account needs verification, we've sent a new link.
                        </v-alert>
                    </template>
                    <template v-else>
                        <p class="text-body-2 text-medium-emphasis mb-3">
                            Enter your email and we'll send a fresh verification link.
                        </p>
                        <v-form @submit.prevent="resend">
                            <v-text-field v-model="resendEmail" label="Email" type="email" autocomplete="email"
                                prepend-inner-icon="mdi-email-outline" variant="outlined" density="compact"></v-text-field>
                            <v-btn type="submit" color="primary" variant="flat" block size="large" :loading="resending"
                                class="mt-4">Resend link</v-btn>
                        </v-form>
                    </template>

                    <div class="text-center text-body-2 text-medium-emphasis mt-6">
                        <router-link to="/Login" class="login-link">Back to sign in</router-link>
                    </div>
                </div>
            </div>
        </div>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000" location="top">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { UserService } from '@/services/UserService'
import { branding } from '@/stores/branding'
import tenantHelper from '@/helpers/TenantHelper'
import BrandLogo from '@/components/BrandLogo.vue'

// Apex (no tenant subdomain) shows the RidePass badge; tenant sites show their own logo/name.
const isApex = computed(() => !tenantHelper.getSubdomain())

const route = useRoute()
const userService = new UserService()

type State = 'loading' | 'success' | 'error'
const state = ref<State>('loading')
const errorMessage = ref('')

const resendEmail = ref('')
const resending = ref(false)
const resendSent = ref(false)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('error')

const brandStyle = computed(() =>
    branding.heroImageUrl ? { backgroundImage: `url(${branding.heroImageUrl})` } : {})

onMounted(async () => {
    const token = route.query.token
    const tokenStr = Array.isArray(token) ? token[0] : token
    if (!tokenStr) {
        state.value = 'error'
        errorMessage.value = 'This link is missing its verification token.'
        return
    }
    try {
        await userService.verifyEmail(tokenStr as string)
        state.value = 'success'
    } catch (error: any) {
        state.value = 'error'
        errorMessage.value = error.response?.data?.error
            || 'This verification link is invalid or has expired.'
    }
})

async function resend() {
    if (!resendEmail.value.trim()) {
        snackbarText.value = 'Please enter your email.'
        snackbarColor.value = 'error'
        snackbar.value = true
        return
    }
    try {
        resending.value = true
        await userService.resendVerification(resendEmail.value.trim())
        resendSent.value = true
    } catch {
        // The endpoint always returns 200, but guard the UX just in case.
        resendSent.value = true
    } finally {
        resending.value = false
    }
}
</script>

<style scoped>
.login-page {
    display: flex;
    min-height: 100vh;
    background: var(--rp-bg);
}

/* Brand panel */
.login-brand {
    position: relative;
    flex: 1 1 50%;
    background-size: cover;
    background-position: center;
    background-color: var(--rp-panel);
    display: flex;
    align-items: flex-end;
    overflow: hidden;
    border-right: 1px solid var(--rp-line);
}
.login-brand-overlay {
    position: absolute;
    inset: 0;
    background:
        linear-gradient(180deg, transparent 40%, rgb(var(--rp-scrim)) 100%),
        linear-gradient(105deg, rgba(var(--rp-scrim), 0.9) 20%, rgba(var(--rp-scrim), 0.55) 55%, rgba(var(--rp-scrim), 0.35));
}
.login-brand-content {
    position: relative;
    color: var(--rp-photo-ink);
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
    font-size: 3.25rem;
    margin-bottom: 1rem;
    padding: 0 0.1em;
}
.login-brand-tagline {
    font-size: 1.1rem;
    font-weight: 500;
    margin-bottom: 0.75rem;
    color: var(--rp-photo-muted);
}
.login-brand-welcome {
    font-size: 1rem;
    color: var(--rp-photo-muted);
    margin-bottom: 0;
    max-width: 420px;
}
.login-title { font-size: 2rem; }

/* Content panel */
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
    background: var(--rp-panel);
    color: var(--rp-ink);
    border: 1px solid var(--rp-line2);
    border-radius: 18px;
    padding: 30px 26px;
}
.login-mobile-logo {
    max-height: 56px;
    max-width: 200px;
    width: auto;
}
.login-link {
    color: var(--rp-ink);
    text-decoration: none;
    font-weight: 700;
}
.login-link:hover { text-decoration: underline; }

@media (max-width: 600px) {
    .login-card { padding: 24px 18px; }
}
</style>
