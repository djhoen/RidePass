<template>
    <v-container class="fill-height" fluid>
        <v-row align="center" justify="center">
            <v-col cols="12" sm="8" md="5">
                <v-card class="pa-4">
                    <!-- Verification-sent confirmation: shown instead of the form once the
                         rider has been emailed a verification link and must verify before login. -->
                    <template v-if="verificationSent">
                        <v-card-text class="text-center py-8">
                            <v-icon color="primary" size="64" class="mb-4">mdi-email-check-outline</v-icon>
                            <h2 class="text-h5 font-weight-bold mb-2">Almost there!</h2>
                            <p class="text-body-1 text-medium-emphasis mb-2">
                                We sent a verification link to <strong>{{ sentToEmail }}</strong>.
                                Click it to activate your account, then sign in.
                            </p>
                            <v-btn color="primary" size="large" class="text-none mt-4" rounded="lg"
                                to="/Login">Go to sign in</v-btn>
                            <div class="text-body-2 text-medium-emphasis mt-6">
                                Didn't get it?
                                <a href="#" class="resend-link ml-1" @click.prevent="resend">Resend</a>
                            </div>
                        </v-card-text>
                    </template>

                    <template v-else>
                    <v-card-title class="text-h5 text-center">Create Account</v-card-title>
                    <v-card-text>
                        <v-form @submit.prevent="createAccount">
                            <v-row>
                                <v-col cols="12" sm="6">
                                    <v-text-field v-model="form.firstName" label="First Name" required></v-text-field>
                                </v-col>
                                <v-col cols="12" sm="6">
                                    <v-text-field v-model="form.lastName" label="Last Name" required></v-text-field>
                                </v-col>
                            </v-row>
                            <v-text-field v-model="form.email" label="Email" type="email" required
                                class="mb-2 mt-4"></v-text-field>
                            <PhoneField v-model="form.phone" label="Mobile phone" required
                                hint="We text waitlist promotions and event-day alerts to this number." persistent-hint
                                class="mb-2 mt-4" />
                            <v-text-field v-model="form.birthdate" label="Birthdate" type="date" required
                                :max="todayIso" class="mb-2 mt-4"></v-text-field>
                            <v-row>
                                <v-col cols="12" sm="6">
                                    <v-text-field v-model="form.emergencyContactName" label="Emergency contact name" required class="mb-2 mt-4"
                                        hint="Someone to call if there's a problem at the track" persistent-hint></v-text-field>
                                </v-col>
                                <v-col cols="12" sm="6">
                                    <PhoneField v-model="form.emergencyContactPhone" label="Emergency contact phone" required class="mb-2 mt-4" />
                                </v-col>
                            </v-row>
                            <v-text-field v-model="form.password" label="Password" type="password" required
                                class="mb-2 mt-4"></v-text-field>
                            <v-text-field v-model="form.confirmPassword" label="Confirm Password" type="password"
                                required class="mb-2 mt-4"></v-text-field>
                            <v-checkbox v-if="!isApex" v-model="form.subscribeNewsletter" density="compact" hide-details
                                :label="`Email me event updates from ${branding.displayName}`" class="mb-2"></v-checkbox>
                            <v-btn type="submit" color="primary" block size="large"
                                :loading="loading">Create Account</v-btn>
                        </v-form>
                    </v-card-text>
                    <v-card-actions class="justify-center">
                        <router-link to="/Login">Already have an account? Login</router-link>
                    </v-card-actions>
                    </template>
                </v-card>
            </v-col>
        </v-row>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { UserService } from '@/services/UserService'
import { NewsletterService } from '@/services/NewsletterService'
import { branding } from '@/stores/branding'
import tenantHelper from '@/helpers/TenantHelper'
import PhoneField from '@/components/PhoneField.vue'

const router = useRouter()
const userService = new UserService()
const newsletterService = new NewsletterService()
const isApex = computed(() => !tenantHelper.getSubdomain())

const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('error')

// When the rider must verify their email before logging in, we swap the form for
// a confirmation message instead of redirecting to the login page.
const verificationSent = ref(false)
const sentToEmail = ref('')

const form = ref({
    firstName: '',
    lastName: '',
    email: '',
    phone: '',
    birthdate: '',
    emergencyContactName: '',
    emergencyContactPhone: '',
    password: '',
    confirmPassword: '',
    subscribeNewsletter: false,
})

const todayIso = new Date().toISOString().slice(0, 10)

async function createAccount() {
    if (form.value.password !== form.value.confirmPassword) {
        snackbarText.value = 'Passwords do not match.'
        snackbarColor.value = 'error'
        snackbar.value = true
        return
    }
    if (!form.value.birthdate || form.value.birthdate >= todayIso) {
        snackbarText.value = 'Please enter a valid birthdate.'
        snackbarColor.value = 'error'
        snackbar.value = true
        return
    }
    if (form.value.phone.replace(/\D/g, '').length < 7) {
        snackbarText.value = 'Please enter a valid mobile phone — we use it for waitlist and event alerts.'
        snackbarColor.value = 'error'
        snackbar.value = true
        return
    }
    if (!form.value.emergencyContactName.trim()
        || form.value.emergencyContactPhone.replace(/\D/g, '').length < 7) {
        snackbarText.value = 'Please enter an emergency contact name and phone.'
        snackbarColor.value = 'error'
        snackbar.value = true
        return
    }
    try {
        loading.value = true
        const response = await userService.createAccount(form.value)
        if (form.value.subscribeNewsletter && !isApex.value) {
            // Best-effort: a failed newsletter signup shouldn't block account creation success.
            try {
                await newsletterService.subscribe(form.value.email, `${form.value.firstName} ${form.value.lastName}`.trim() || null)
            } catch { /* ignore */ }
        }
        const sent = response.data?.data?.emailVerificationSent
        if (sent) {
            // The rider must click the emailed link before they can sign in, so hold
            // them on a confirmation screen rather than bouncing to the login page.
            sentToEmail.value = form.value.email
            verificationSent.value = true
            return
        }
        // No verification needed (SMTP not configured): the account is usable now.
        snackbarText.value = 'Account created successfully!'
        snackbarColor.value = 'success'
        snackbar.value = true
        setTimeout(() => router.push('/Login'), 1500)
    } catch (error: any) {
        snackbarText.value = error.response?.data?.message || 'Failed to create account.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        loading.value = false
    }
}

async function resend() {
    try {
        await userService.resendVerification(sentToEmail.value)
    } catch { /* endpoint always returns 200; ignore */ }
    snackbarText.value = "If that account needs verification, we've sent a new link."
    snackbarColor.value = 'success'
    snackbar.value = true
}
</script>

<style scoped>
.resend-link {
    color: rgb(var(--v-theme-primary));
    text-decoration: none;
    font-weight: 600;
}
.resend-link:hover { text-decoration: underline; }
</style>
