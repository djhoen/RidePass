<template>
    <div class="login-page">
        <!-- Brand panel (md+), matching the Login layout. -->
        <div class="login-brand d-none d-md-flex" :style="brandStyle">
            <div class="login-brand-overlay"></div>
            <div class="login-brand-content">
                <img v-if="branding.logoUrl" :src="branding.logoUrl" :alt="branding.displayName"
                    class="login-brand-logo" />
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
                    <h1 class="text-h5 font-weight-bold mb-1">Your email is verified</h1>
                    <p class="text-body-2 text-medium-emphasis mb-6">
                        You're all set. Sign in to start managing your passes.
                    </p>
                    <v-btn color="primary" block size="large" class="text-none" rounded="lg" to="/Login">
                        Go to sign in
                    </v-btn>
                </div>

                <!-- Error (missing token or failed verification) -->
                <div v-else>
                    <div class="text-center mb-6">
                        <v-icon color="error" size="64" class="mb-4">mdi-alert-circle-outline</v-icon>
                        <h1 class="text-h5 font-weight-bold mb-1">Verification failed</h1>
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
                            <v-btn type="submit" color="primary" block size="large" :loading="resending"
                                class="text-none mt-4" rounded="lg">Resend link</v-btn>
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
    /* Theme-aware so the panel background follows light/dark mode. */
    background: rgb(var(--v-theme-background));
}

/* Brand panel */
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
    /* Surface (not hardcoded white) so text stays legible in dark mode. */
    background: rgb(var(--v-theme-surface));
    color: rgb(var(--v-theme-on-surface));
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
    .login-form-wrap { background: rgb(var(--v-theme-surface)); }
}
</style>
