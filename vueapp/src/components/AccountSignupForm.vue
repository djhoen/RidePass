<template>
    <div class="acct-signup">
        <ul v-if="showBenefits" class="acct-benefits mb-4">
            <li v-for="(b, i) in benefits" :key="i">
                <v-icon icon="mdi-check-circle" size="18" color="success" class="acct-benefit-icon"></v-icon>
                <span>{{ b }}</span>
            </li>
        </ul>

        <!-- Success: CreateAccount returns no auth token (and may require email verification),
             so we don't auto-log-in — we confirm and point them to sign in. -->
        <v-alert v-if="created" type="success" variant="tonal" density="compact">
            {{ verifyEmail
                ? `Account created. Check ${form.email} for a link to verify your email, then sign in.`
                : 'Account created. You can sign in now.' }}
        </v-alert>

        <v-form v-else @submit.prevent="submit">
            <v-row dense>
                <v-col cols="12" sm="6">
                    <v-text-field v-model="form.firstName" label="First name" density="compact" hide-details="auto"></v-text-field>
                </v-col>
                <v-col cols="12" sm="6">
                    <v-text-field v-model="form.lastName" label="Last name" density="compact" hide-details="auto"></v-text-field>
                </v-col>
            </v-row>
            <v-text-field v-model="form.email" type="email" label="Email" density="compact" class="mt-4" hide-details="auto"></v-text-field>
            <v-text-field v-model="form.password" :type="showPassword ? 'text' : 'password'"
                label="Password (8+ characters)" density="compact" class="mt-4" hide-details="auto"
                :append-inner-icon="showPassword ? 'mdi-eye-off-outline' : 'mdi-eye-outline'"
                @click:append-inner="showPassword = !showPassword"></v-text-field>
            <v-text-field v-model="form.phone" type="tel" label="Mobile phone" density="compact" class="mt-4"
                hint="For race-day and waitlist alerts." persistent-hint></v-text-field>
            <v-text-field v-model="form.birthdate" type="date" label="Date of birth" :max="todayIso"
                density="compact" class="mt-4" hide-details="auto"></v-text-field>
            <v-row dense class="mt-2">
                <v-col cols="12" sm="6">
                    <v-text-field v-model="form.emergencyName" label="Emergency contact name" density="compact" hide-details="auto"></v-text-field>
                </v-col>
                <v-col cols="12" sm="6">
                    <v-text-field v-model="form.emergencyPhone" type="tel" label="Emergency contact phone" density="compact" hide-details="auto"></v-text-field>
                </v-col>
            </v-row>

            <!-- Notification choices for this track. All start unchecked: email, newsletter,
                 and texts are each an explicit opt-in the rider chooses. -->
            <div class="mt-4">
                <div class="text-body-2 font-weight-medium mb-1">Stay in the loop</div>
                <v-checkbox v-model="prefs.eventEmail" density="compact" hide-details
                    label="Email me when new events are posted"></v-checkbox>
                <v-checkbox v-model="prefs.newsletter" density="compact" hide-details
                    label="Subscribe to the newsletter"></v-checkbox>
                <v-checkbox v-model="prefs.eventSms" density="compact" hide-details
                    label="Text me when new events are posted (message & data rates may apply)"></v-checkbox>
            </div>

            <v-alert v-if="errorMessage" type="error" variant="tonal" density="compact" class="mt-4">{{ errorMessage }}</v-alert>

            <v-btn type="submit" color="primary" block size="large" :loading="loading" class="mt-4 text-none">
                Create account
            </v-btn>
        </v-form>
    </div>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import dayjs from 'dayjs'
import { UserService } from '@/services/UserService'

// Reusable rider signup form. Used inline after guest checkout and on the /SignUp page.
// CreateAccount requires all of these fields (validated server-side), so the form collects
// them all; callers prefill whatever they already know (name/email, and birthdate /
// emergency contact captured during event registration).
const props = withDefaults(defineProps<{
    prefill?: {
        email?: string
        firstName?: string
        lastName?: string
        birthdate?: string
        emergencyName?: string
        emergencyPhone?: string
    }
    showBenefits?: boolean
}>(), { showBenefits: true })

const emit = defineEmits<{ (e: 'created', payload: any): void }>()

const userService = new UserService()
const todayIso = dayjs().format('YYYY-MM-DD')

const showPassword = ref(false)
const loading = ref(false)
const errorMessage = ref('')
const created = ref(false)
const verifyEmail = ref(false)

const benefits = [
    'Check out faster next time with your details saved',
    'Find your tickets and gate QR codes in one place',
    'Use and track a season pass',
    'Get race-day and waitlist alerts by text',
]

const form = reactive({
    firstName: props.prefill?.firstName ?? '',
    lastName: props.prefill?.lastName ?? '',
    email: props.prefill?.email ?? '',
    password: '',
    phone: '',
    birthdate: props.prefill?.birthdate ?? '',
    emergencyName: props.prefill?.emergencyName ?? '',
    emergencyPhone: props.prefill?.emergencyPhone ?? '',
})

// Explicit opt-in across the board: every marketing / announcement choice starts unchecked,
// so subscribing is an affirmative action the rider takes, not a pre-ticked default.
const prefs = reactive({ eventEmail: false, newsletter: false, eventSms: false })

const digitCount = (s: string) => (s.match(/\d/g) || []).length

async function submit() {
    errorMessage.value = ''
    if (!form.firstName.trim() || !form.lastName.trim()) { errorMessage.value = 'Enter your first and last name.'; return }
    if (!/\S+@\S+\.\S+/.test(form.email.trim())) { errorMessage.value = 'Enter a valid email address.'; return }
    if (form.password.length < 8) { errorMessage.value = 'Password must be at least 8 characters.'; return }
    if (digitCount(form.phone) < 7) { errorMessage.value = 'Enter a valid mobile phone number. We use it for event alerts.'; return }
    if (!form.birthdate) { errorMessage.value = 'Enter your date of birth.'; return }
    if (!form.emergencyName.trim() || digitCount(form.emergencyPhone) < 7) {
        errorMessage.value = 'Enter an emergency contact name and a valid phone.'; return
    }

    loading.value = true
    try {
        const r = await userService.createAccount({
            email: form.email.trim(),
            password: form.password,
            firstName: form.firstName.trim(),
            lastName: form.lastName.trim(),
            birthdate: form.birthdate,
            phone: form.phone.trim(),
            emergencyContactName: form.emergencyName.trim(),
            emergencyContactPhone: form.emergencyPhone.trim(),
            notifyEventEmail: prefs.eventEmail,
            notifyEventSms: prefs.eventSms,
            subscribeNewsletter: prefs.newsletter,
        })
        const data = (r.data as any).data ?? r.data
        verifyEmail.value = !!data?.emailVerificationSent
        created.value = true
        emit('created', data)
    } catch (err: any) {
        errorMessage.value = err.response?.data?.error || 'Could not create your account. Please try again.'
    } finally {
        loading.value = false
    }
}

// Expose so a parent can read whether signup completed.
defineExpose({ created })
</script>

<style scoped>
.acct-benefits {
    list-style: none;
    padding: 0;
    margin: 0;
}
.acct-benefits li {
    display: flex;
    align-items: flex-start;
    gap: 6px;
    padding: 3px 0;
    font-size: 0.9rem;
}
.acct-benefit-icon {
    margin-top: 1px;
    flex: 0 0 auto;
}
</style>
