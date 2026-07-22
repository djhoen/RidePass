<template>
    <v-container style="max-width: 720px">
        <h1 class="text-h4 mb-2">Send a Gift Card</h1>
        <p class="text-body-2 text-medium-emphasis mb-4">
            Pick a denomination, add the recipient's info, and we'll email them a code they can use
            on any purchase at {{ branding.displayName }} until the balance runs out.
        </p>

        <v-alert v-if="branding.loaded && !branding.giftCardsEnabled" type="info" variant="tonal">
            This tenant doesn't sell gift cards right now.
        </v-alert>

        <template v-else>
            <v-card v-if="!completed" class="pa-4 mb-4">
                <v-card-title>Gift card details</v-card-title>
                <v-card-text>
                    <div class="text-subtitle-2 mb-2">Amount</div>
                    <div class="d-flex flex-wrap ga-2 mb-3">
                        <v-btn v-for="amt in presetAmounts" :key="amt"
                            :variant="amountCents === amt * 100 ? 'flat' : 'outlined'"
                            :color="amountCents === amt * 100 ? 'primary' : undefined"
                            density="comfortable" @click="amountCents = amt * 100">
                            ${{ amt }}
                        </v-btn>
                    </div>
                    <v-text-field v-model.number="customAmount" type="number"
                        :min="branding.giftCardMinCents / 100" :max="branding.giftCardMaxCents / 100"
                        label="Custom amount ($)" density="compact" :hint="`Between $${(branding.giftCardMinCents/100).toFixed(0)} and $${(branding.giftCardMaxCents/100).toFixed(0)}`"
                        persistent-hint class="mb-2"
                        :error-messages="amountError ? [amountError] : []"></v-text-field>

                    <v-divider class="my-3"></v-divider>
                    <div class="text-subtitle-2 mb-2">Recipient</div>
                    <v-text-field v-model="recipientName" label="Recipient name" density="compact" class="mb-2"></v-text-field>
                    <v-text-field v-model="recipientEmail" type="email" label="Recipient email" density="compact" class="mb-2"></v-text-field>
                    <v-textarea v-model="personalNote" label="Note to recipient (optional)"
                        rows="2" auto-grow density="compact" maxlength="500" counter></v-textarea>

                    <v-checkbox v-model="scheduleDelivery" label="Schedule delivery for later"
                        density="compact" hide-details class="mt-2"></v-checkbox>
                    <v-text-field v-if="scheduleDelivery" v-model="scheduledLocal" type="datetime-local"
                        label="Send on" density="compact" class="mt-2"
                        :hint="`Times in ${branding.timezone || 'UTC'}`" persistent-hint></v-text-field>

                    <div v-if="amountCents >= branding.giftCardMinCents" class="mt-4 text-body-2">
                        <div class="d-flex justify-space-between text-medium-emphasis">
                            <span>Gift card value</span><span>${{ (amountCents / 100).toFixed(2) }}</span>
                        </div>
                        <div v-if="estServiceChargeCents > 0" class="d-flex justify-space-between text-medium-emphasis">
                            <span>Service charge</span><span>${{ (estServiceChargeCents / 100).toFixed(2) }}</span>
                        </div>
                        <div class="d-flex justify-space-between font-weight-bold mt-1">
                            <span>Total today</span><span>${{ (estTotalCents / 100).toFixed(2) }}</span>
                        </div>
                    </div>

                    <v-btn color="primary" class="mt-4" :loading="creating" :disabled="!canContinue" @click="createIntent">
                        Continue to Payment
                    </v-btn>
                </v-card-text>
            </v-card>

            <v-card v-if="step === 'payment' && !completed" class="pa-4 mb-4">
                <v-card-title>Payment</v-card-title>
                <v-card-text>
                    <div v-if="!branding.stripePublishableKey" class="text-error">
                        Stripe publishable key is not configured.
                    </div>
                    <template v-else>
                        <v-table density="compact" class="mb-3">
                            <tbody>
                                <tr><td>Gift card value</td><td class="text-right">${{ (giftValueCents / 100).toFixed(2) }}</td></tr>
                                <tr v-if="serviceChargeCents > 0"><td>Service charge</td><td class="text-right">${{ (serviceChargeCents / 100).toFixed(2) }}</td></tr>
                                <tr><td><strong>Total</strong></td><td class="text-right"><strong>${{ (totalCents / 100).toFixed(2) }}</strong></td></tr>
                            </tbody>
                        </v-table>
                        <div id="gift-payment-element" class="mb-4"></div>
                        <v-btn color="primary" :loading="paying" :disabled="!stripeReady" @click="pay">
                            Pay ${{ (totalCents / 100).toFixed(2) }}
                        </v-btn>
                        <div v-if="paymentError" class="text-error mt-3">{{ paymentError }}</div>
                    </template>
                </v-card-text>
            </v-card>

            <v-card v-if="completed" variant="outlined" class="pa-4 text-center">
                <v-alert type="success" class="mb-3">
                    Gift card paid for!
                </v-alert>
                <p class="mb-2">
                    {{ scheduleDelivery
                        ? `We'll email ${recipientEmail} on ${scheduledLocal} with their code.`
                        : `We've emailed ${recipientEmail} their code.` }}
                </p>
                <p class="text-caption text-medium-emphasis">
                    They can apply it on any purchase until the balance runs out.
                </p>
                <div class="d-flex justify-center ga-2 mt-4">
                    <v-btn color="primary" @click="sendAnother">Send another gift card</v-btn>
                    <v-btn variant="text" to="/">Back to home</v-btn>
                </div>
            </v-card>
        </template>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, nextTick, watch, onMounted } from 'vue'
import dayjs from 'dayjs'
import { GiftCardService } from '@/services/GiftCardService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'

const service = new GiftCardService()

const presetAmounts = computed(() => {
    const min = branding.giftCardMinCents / 100
    const max = branding.giftCardMaxCents / 100
    const candidates = [25, 50, 100, 150, 250]
    return candidates.filter(a => a >= min && a <= max)
})

const customAmount = ref<number | null>(50)
const amountCents = computed({
    get: () => Math.round((customAmount.value ?? 0) * 100),
    set: (v: number) => { customAmount.value = v / 100 },
})
const amountError = ref('')
watch(customAmount, () => { amountError.value = '' })

const recipientName = ref('')
const recipientEmail = ref('')
const personalNote = ref('')
const scheduleDelivery = ref(false)
const scheduledLocal = ref('')

const step = ref<'compose' | 'payment'>('compose')
const creating = ref(false)
const completed = ref(false)
const giftValueCents = ref(0)
const serviceChargeCents = ref(0)
const totalCents = ref(0)
const clientSecret = ref<string | null>(null)
const stripeReady = ref(false)
const paying = ref(false)
const paymentError = ref<string | null>(null)
let stripe: any = null
let elements: any = null

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

// Estimated fee shown on the compose step so the total isn't a surprise on the next screen. The
// server is authoritative (it returns the exact serviceChargeCents on createIntent); this mirrors
// its per-value math (floor(value * tenantBps)).
const estServiceChargeCents = computed(() =>
    Math.floor((amountCents.value * (branding.serviceChargeBps ?? 0)) / 10000))
const estTotalCents = computed(() => amountCents.value + estServiceChargeCents.value)

const canContinue = computed(() => {
    if (!branding.giftCardsEnabled) return false
    if (amountCents.value < branding.giftCardMinCents || amountCents.value > branding.giftCardMaxCents) return false
    if (!recipientName.value.trim() || !recipientEmail.value.trim()) return false
    if (scheduleDelivery.value && !scheduledLocal.value) return false
    return true
})

function sendAnother() {
    completed.value = false
    step.value = 'compose'
    clientSecret.value = null
    recipientName.value = ''
    recipientEmail.value = ''
    personalNote.value = ''
    scheduleDelivery.value = false
    scheduledLocal.value = ''
    customAmount.value = 50
}

async function createIntent() {
    if (!canContinue.value) return
    if (amountCents.value < branding.giftCardMinCents || amountCents.value > branding.giftCardMaxCents) {
        amountError.value = `Amount must be between $${(branding.giftCardMinCents/100).toFixed(0)} and $${(branding.giftCardMaxCents/100).toFixed(0)}.`
        return
    }
    creating.value = true
    try {
        const scheduledUtc = scheduleDelivery.value && scheduledLocal.value
            ? dayjs(scheduledLocal.value).tz(branding.timezone || 'UTC', true).utc().toISOString()
            : null
        const r = await service.buy({
            amountCents: amountCents.value,
            recipientName: recipientName.value.trim(),
            recipientEmail: recipientEmail.value.trim(),
            personalNote: personalNote.value.trim() || null,
            scheduledDeliveryAtUtc: scheduledUtc,
        })
        const data = (r.data as any).data
        clientSecret.value = data.clientSecret
        totalCents.value = data.amountCents
        giftValueCents.value = amountCents.value
        serviceChargeCents.value = totalCents.value - giftValueCents.value
        step.value = 'payment'
        await nextTick()
        await mountStripe()
    } catch (err: any) {
        const message = err.response?.data?.error as string | undefined
        if (message && /amount/i.test(message)) {
            amountError.value = message
        } else {
            flash(message || 'Failed to start payment.', 'error')
        }
    } finally {
        creating.value = false
    }
}

async function mountStripe() {
    if (!clientSecret.value) return
    // Direct-charge tenants confirm on their own connected account; platform tenants pass no account.
    const stripeAccount = branding.stripeChargeMode === 'direct' ? branding.stripeConnectAccountId : null
    stripe = await getStripe(branding.stripePublishableKey, stripeAccount)
    if (!stripe) { paymentError.value = 'Stripe not available.'; return }
    elements = stripe.elements({ clientSecret: clientSecret.value })
    const pe = elements.create('payment')
    pe.mount('#gift-payment-element')
    stripeReady.value = true
}

async function pay() {
    if (!stripe || !elements) return
    paying.value = true
    paymentError.value = null
    try {
        const { error } = await stripe.confirmPayment({
            elements,
            confirmParams: { return_url: window.location.href },
            redirect: 'if_required',
        })
        if (error) paymentError.value = error.message || 'Payment failed.'
        else completed.value = true
    } catch (err: any) {
        paymentError.value = err?.message || 'Payment failed.'
    } finally {
        paying.value = false
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

onMounted(() => {
    // Returning from a redirect-based payment method (3DS / wallet): Stripe sends the browser back to
    // return_url (this page) with these params. Without handling them the buyer sees the untouched
    // form and assumes it failed, then buys a second card (the first already went through).
    const params = new URLSearchParams(window.location.search)
    const redirectStatus = params.get('redirect_status')
    if (params.get('payment_intent') && redirectStatus) {
        if (redirectStatus === 'succeeded') {
            completed.value = true
        } else {
            flash('Your payment was not completed. Please try again.', 'error')
        }
        history.replaceState(null, '', window.location.pathname)
    }
})
</script>
