<template>
    <v-container style="max-width: 720px">
        <h1 class="text-h4 mb-4">Buy Event Ticket</h1>

        <v-card v-if="loading" class="pa-4">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </v-card>

        <template v-else>
            <v-card class="mb-4 pa-4">
                <v-card-title>Choose a tier</v-card-title>
                <v-card-text>
                    <v-radio-group v-model="selectedTierId">
                        <v-radio v-for="t in tiers" :key="t.id" :value="t.id" :disabled="isSoldOut(t)">
                            <template #label>
                                <div>
                                    <strong>{{ t.name }}</strong> — ${{ (t.priceCents / 100).toFixed(2) }}
                                    <span v-if="t.inventory" class="text-caption text-medium-emphasis">
                                        ({{ (t.inventory - (t.sold ?? 0)) }} of {{ t.inventory }} left)
                                    </span>
                                    <span v-if="isSoldOut(t)" class="text-error text-caption"> — SOLD OUT</span>
                                </div>
                            </template>
                        </v-radio>
                    </v-radio-group>
                    <div v-if="tiers.length === 0" class="text-medium-emphasis">
                        This event has no tickets available.
                    </div>

                    <!-- Guest checkout: collect email + name. Authenticated users skip this. -->
                    <template v-if="!isAuthenticated && selectedTierId">
                        <v-divider class="my-4"></v-divider>
                        <div class="text-subtitle-2 mb-2">Your contact info</div>
                        <p class="text-caption text-medium-emphasis mb-3">
                            No account needed. We'll email you a receipt, and the QR code appears here on confirmation.
                        </p>
                        <v-text-field v-model="guestName" label="Full name" density="compact" class="mb-2"></v-text-field>
                        <v-text-field v-model="guestEmail" type="email" label="Email" density="compact"></v-text-field>
                    </template>

                    <v-btn color="primary" class="mt-4" :loading="creating" :disabled="!canContinue" @click="createIntent">
                        Continue to Payment
                    </v-btn>
                </v-card-text>
            </v-card>

            <v-card v-if="step === 2 || completed" class="mb-4 pa-4">
                <v-card-title>Payment</v-card-title>
                <v-card-text>
                    <div v-if="!branding.stripePublishableKey" class="text-error">Stripe publishable key is not configured.</div>
                    <template v-else-if="!completed">
                        <div id="ticket-payment-element" class="mb-4"></div>
                        <v-btn color="primary" :loading="paying" :disabled="!stripeReady" @click="pay">Pay ${{ displayAmount() }}</v-btn>
                        <div v-if="paymentError" class="text-error mt-3">{{ paymentError }}</div>
                    </template>
                    <template v-else>
                        <v-alert type="success" class="mb-4">
                            Ticket purchased! Show this QR at the gate.
                        </v-alert>
                        <div class="d-flex justify-center mb-3">
                            <QrCode v-if="redemptionToken" :value="redeemUrl(redemptionToken)" :size="260" />
                        </div>
                        <div v-if="!isAuthenticated" class="text-center text-caption text-medium-emphasis">
                            Screenshot this QR or save the page — your ticket lives at this URL.
                        </div>
                        <div v-else class="text-center text-caption text-medium-emphasis">
                            Find it later on <router-link to="/User/MyPasses">My Passes</router-link>.
                        </div>
                    </template>
                </v-card-text>
            </v-card>
        </template>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted, nextTick, computed } from 'vue'
import { useRoute } from 'vue-router'
import { TicketService, type TicketTier } from '@/services/TicketService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'
import authHelper from '@/helpers/AuthHelper'
import QrCode from '@/components/QrCode.vue'

const route = useRoute()
const service = new TicketService()

const eventId = route.params.eventId as string
const tiers = ref<TicketTier[]>([])
const selectedTierId = ref<string | null>(null)
const loading = ref(true)
const creating = ref(false)

const isAuthenticated = computed(() => authHelper.isAuthenticated())
const guestName = ref('')
const guestEmail = ref('')

const step = ref(1)
const purchaseId = ref<string | null>(null)
const redemptionToken = ref<string | null>(null)
const clientSecret = ref<string | null>(null)
const amountCents = ref(0)
const stripeReady = ref(false)
const paying = ref(false)
const paymentError = ref<string | null>(null)
const completed = ref(false)

let stripe: any = null
let elements: any = null

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

function redeemUrl(token: string): string {
    return `${window.location.protocol}//${window.location.host}/redeem/${token}`
}

function displayAmount() { return (amountCents.value / 100).toFixed(2) }

function isSoldOut(t: TicketTier): boolean {
    return t.inventory !== null && (t.sold ?? 0) >= t.inventory
}

const canContinue = computed(() => {
    if (!selectedTierId.value) return false
    if (step.value === 2) return false
    if (!isAuthenticated.value) {
        return guestEmail.value.trim().length > 0 && guestName.value.trim().length > 0
    }
    return true
})

onMounted(async () => {
    loading.value = true
    try {
        const r = await service.listActiveTiers(eventId)
        tiers.value = (r.data as any).data
    } finally {
        loading.value = false
    }
})

async function createIntent() {
    if (!selectedTierId.value) return
    try {
        creating.value = true
        const req: { tierId: string; email?: string | null; name?: string | null } = { tierId: selectedTierId.value }
        if (!isAuthenticated.value) {
            req.email = guestEmail.value.trim()
            req.name = guestName.value.trim()
        }
        const r = await service.createTicketPurchase(req)
        const data = (r.data as any).data
        purchaseId.value = data.purchaseId
        redemptionToken.value = data.redemptionToken
        clientSecret.value = data.clientSecret
        amountCents.value = data.amountCents
        step.value = 2
        await nextTick()
        await mountPaymentElement()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to start payment.', 'error')
    } finally {
        creating.value = false
    }
}

async function mountPaymentElement() {
    if (!clientSecret.value) return
    stripe = await getStripe(branding.stripePublishableKey)
    if (!stripe) { paymentError.value = 'Stripe not available.'; return }
    elements = stripe.elements({ clientSecret: clientSecret.value })
    const pe = elements.create('payment')
    pe.mount('#ticket-payment-element')
    stripeReady.value = true
}

async function pay() {
    if (!stripe || !elements) return
    paying.value = true
    paymentError.value = null
    try {
        const returnUrl = isAuthenticated.value
            ? window.location.origin + '/User/MyPasses'
            : window.location.href
        const { error } = await stripe.confirmPayment({
            elements,
            confirmParams: { return_url: returnUrl },
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
</script>
