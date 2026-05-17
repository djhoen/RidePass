<template>
    <v-container class="fill-height" fluid>
        <v-row align="center" justify="center">
            <v-col cols="12" sm="8" md="4">
                <v-card class="pa-4">
                    <!-- Confirm mode: token in URL -->
                    <template v-if="hasToken">
                        <v-card-title class="text-h5 text-center">Choose a new password</v-card-title>
                        <v-card-text>
                            <p v-if="!done" class="text-body-2 text-medium-emphasis mb-4">
                                Enter and confirm your new password. The link expires 60 minutes after it was sent.
                            </p>
                            <v-form v-if="!done" @submit.prevent="confirmReset">
                                <v-text-field v-model="newPassword" label="New password" type="password"
                                    prepend-inner-icon="mdi-lock" required :rules="[v => (v && v.length >= 8) || 'At least 8 characters']"
                                    class="mb-2"></v-text-field>
                                <v-text-field v-model="confirmPassword" label="Confirm password" type="password"
                                    prepend-inner-icon="mdi-lock-check" required
                                    :rules="[v => v === newPassword || 'Passwords do not match']" class="mb-4"></v-text-field>
                                <v-btn type="submit" color="primary" block size="large" :loading="loading"
                                    :disabled="!canSubmitConfirm">Set new password</v-btn>
                            </v-form>
                            <v-alert v-else type="success" variant="tonal" class="mt-2">
                                Password updated. Redirecting to sign-in…
                            </v-alert>
                        </v-card-text>
                    </template>

                    <!-- Request mode: just an email field -->
                    <template v-else>
                        <v-card-title class="text-h5 text-center">Reset Password</v-card-title>
                        <v-card-text>
                            <p class="text-body-2 text-medium-emphasis mb-4">
                                Enter your email address and we'll send you a link to set a new password.
                            </p>
                            <v-form @submit.prevent="requestReset">
                                <v-text-field v-model="email" label="Email" type="email" prepend-inner-icon="mdi-email"
                                    required class="mb-4"></v-text-field>
                                <v-btn type="submit" color="primary" block size="large" :loading="loading">
                                    Send reset link
                                </v-btn>
                            </v-form>
                        </v-card-text>
                    </template>

                    <v-card-actions class="justify-center">
                        <router-link to="/Login">Back to Login</router-link>
                    </v-card-actions>
                </v-card>
            </v-col>
        </v-row>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { UserService } from '@/services/UserService'

const userService = new UserService()
const route = useRoute()
const router = useRouter()

const token = computed(() => (route.query.token as string | undefined) || '')
const hasToken = computed(() => token.value.length > 0)

const email = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const loading = ref(false)
const done = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const canSubmitConfirm = computed(() =>
    newPassword.value.length >= 8 && newPassword.value === confirmPassword.value)

async function requestReset() {
    try {
        loading.value = true
        await userService.resetPassword({ email: email.value.trim() })
        flash('If that email exists, a reset link has been sent.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Something went wrong.', 'error')
    } finally {
        loading.value = false
    }
}

async function confirmReset() {
    if (!canSubmitConfirm.value) return
    try {
        loading.value = true
        await userService.confirmPasswordReset({ token: token.value, newPassword: newPassword.value })
        done.value = true
        flash('Password updated.', 'success')
        setTimeout(() => router.push('/Login'), 1500)
    } catch (err: any) {
        flash(err.response?.data?.error || 'This reset link is invalid or has expired.', 'error')
    } finally {
        loading.value = false
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
