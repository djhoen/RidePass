<template>
    <!-- In-place sign in / sign up for checkout flows (works inside embed iframes:
         no navigation, the parent flow resumes via @authed when a token lands). -->
    <v-dialog :model-value="modelValue" max-width="520" @update:model-value="v => emit('update:modelValue', v)">
        <v-card>
            <v-card-title class="d-flex align-center">
                {{ tab === 'signin' ? 'Sign in to continue' : 'Create your account' }}
                <v-spacer />
                <v-btn icon="mdi-close" variant="text" size="small" @click="emit('update:modelValue', false)" />
            </v-card-title>
            <v-card-text>
                <v-tabs v-model="tab" :height="40" class="mb-4 sub-tabs" hide-slider selected-class="sub-tab-active">
                    <v-tab value="signin" class="sub-tab">Sign in</v-tab>
                    <v-tab value="signup" class="sub-tab">Create account</v-tab>
                </v-tabs>

                <template v-if="tab === 'signin'">
                    <v-alert v-if="signinError" type="error" variant="tonal" density="compact" class="mb-4">
                        {{ signinError }}
                    </v-alert>
                    <v-text-field v-model="email" label="Email" type="email" density="compact"
                        autocomplete="email" @keyup.enter="signIn" />
                    <v-text-field v-model="password" label="Password" type="password" density="compact" class="mt-4"
                        autocomplete="current-password" @keyup.enter="signIn" />
                    <v-btn color="primary" block class="mt-4" :loading="signingIn"
                        :disabled="!email.includes('@') || password.length === 0" @click="signIn">
                        Sign in
                    </v-btn>
                </template>

                <template v-else>
                    <AccountSignupForm @created="onCreated" />
                </template>
            </v-card-text>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import AccountSignupForm from '@/components/AccountSignupForm.vue'
import authHelper from '@/helpers/AuthHelper'
import { UserService } from '@/services/UserService'

defineProps<{ modelValue: boolean }>()
const emit = defineEmits<{
    (e: 'update:modelValue', v: boolean): void
    // Fired once a valid session token is stored; the parent resumes its flow.
    (e: 'authed'): void
}>()

const userService = new UserService()
const tab = ref<'signin' | 'signup'>('signin')
const email = ref('')
const password = ref('')
const signingIn = ref(false)
const signinError = ref('')

async function signIn() {
    if (!email.value.includes('@') || !password.value) return
    signingIn.value = true
    signinError.value = ''
    try {
        const r = await userService.login({ email: email.value.trim(), password: password.value })
        const token = (r.data as any).data?.token
        if (!token) throw new Error('no token')
        authHelper.setToken(token)
        emit('update:modelValue', false)
        emit('authed')
    } catch (err: any) {
        signinError.value = err.response?.data?.error
            ?? 'Could not sign you in. Check your email and password and try again.'
    } finally {
        signingIn.value = false
    }
}

function onCreated(payload: any) {
    // The signup endpoint signs the new account straight in (token in the response)
    // so checkout continues without a login round-trip. If the token is ever absent
    // (older backend), fall back to the sign-in tab with the email prefilled.
    const token = payload?.token
    if (token) {
        authHelper.setToken(token)
        emit('update:modelValue', false)
        emit('authed')
    } else {
        email.value = payload?.email ?? email.value
        tab.value = 'signin'
    }
}
</script>

<style scoped>
/* Sub-tabs: pills on a tinted rail (house style). */
.sub-tabs {
    background: rgba(var(--v-theme-on-surface), 0.04);
    border-radius: 4px;
    padding: 4px;
    display: inline-flex;
    flex: 0 0 auto;
}
.sub-tabs :deep(.v-slide-group__content) { gap: 4px; align-items: center; }
.sub-tabs :deep(.v-tab) {
    border-radius: 4px;
    height: 32px;
    min-height: 32px;
    min-width: 0;
    padding: 0 18px;
    font-size: 13px;
    letter-spacing: 0.01em;
    text-transform: none;
    opacity: 0.75;
}
.sub-tabs :deep(.sub-tab-active) {
    background: rgb(var(--v-theme-surface));
    opacity: 1;
    font-weight: 600;
    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.12);
}
</style>
