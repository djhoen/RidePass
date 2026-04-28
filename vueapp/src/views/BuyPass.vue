<template>
    <v-container style="max-width: 720px">
        <h1 class="text-h4 mb-4">Buy a Day Pass</h1>

        <v-stepper v-model="step" :items="stepLabels" hide-actions>
            <!-- Step 1: Waiver -->
            <v-stepper-window-item :value="1">
                <v-card class="mb-4 pa-4" v-if="waiver">
                    <v-card-title>{{ waiver.title }}</v-card-title>
                    <v-card-subtitle class="mb-2">Version {{ waiver.version }}</v-card-subtitle>
                    <v-card-text>
                        <div v-if="sigStatus?.hasSignedCurrent" class="mb-4">
                            <v-icon color="success" class="mr-2">mdi-check-circle</v-icon>
                            You signed this waiver on {{ formatDate(sigStatus.signedAt) }}.
                        </div>
                        <template v-else>
                            <div v-if="hasBody(waiver.body)" class="waiver-body">
                                <RichTextView :html="waiver.body" />
                            </div>
                            <div v-else class="text-medium-emphasis">
                                (Tenant has not filled in waiver text yet. Ask them to do so.)
                            </div>
                        </template>
                        <v-btn v-if="!sigStatus?.hasSignedCurrent" color="primary" :loading="signing" class="mt-4" @click="sign">
                            I agree & sign
                        </v-btn>
                        <v-btn v-else color="primary" class="mt-2" @click="advanceFromWaiver">Continue</v-btn>
                    </v-card-text>
                </v-card>
            </v-stepper-window-item>

            <!-- Step 2 (optional): Reserve event (only when tenant requires reservations) -->
            <v-stepper-window-item v-if="requiresReservation" :value="2">
                <v-card class="mb-4 pa-4">
                    <v-card-title>Reserve your ride day</v-card-title>
                    <v-card-text>
                        <p class="text-body-2 text-medium-emphasis mb-3">
                            Pick an event with available spots. Your pass will be valid on that day.
                        </p>
                        <v-progress-circular v-if="loadingEvents" indeterminate color="primary"></v-progress-circular>
                        <v-radio-group v-else v-model="selectedEventId">
                            <v-radio v-for="e in reservableEvents" :key="e.id" :value="e.id" :disabled="spotsLeft(e) <= 0">
                                <template #label>
                                    <div>
                                        <strong>{{ e.title }}</strong> — {{ formatDateShort(e.startsAtUtc) }}
                                        <div class="text-caption text-medium-emphasis">
                                            {{ spotsLeft(e) }} of {{ e.capacity }} spots left
                                            <span v-if="spotsLeft(e) <= 0" class="text-error"> — SOLD OUT</span>
                                        </div>
                                    </div>
                                </template>
                            </v-radio>
                        </v-radio-group>
                        <div v-if="!loadingEvents && reservableEvents.length === 0" class="text-medium-emphasis">
                            No ride days with open spots in the next 60 days.
                        </div>
                        <v-btn color="primary" class="mt-4" :disabled="!selectedEventId" @click="step = 3">Continue</v-btn>
                    </v-card-text>
                </v-card>
            </v-stepper-window-item>

            <!-- Step N: Select Pass -->
            <v-stepper-window-item :value="selectPassStep">
                <v-card class="mb-4 pa-4">
                    <v-card-title>Choose a pass</v-card-title>
                    <v-card-text>
                        <v-radio-group v-model="selectedProductId">
                            <v-radio v-for="p in products" :key="p.id" :value="p.id">
                                <template #label>
                                    <div>
                                        <strong>{{ p.name }}</strong> — ${{ (p.priceCents / 100).toFixed(2) }}
                                        <div v-if="p.description" class="text-caption text-medium-emphasis">{{ p.description }}</div>
                                    </div>
                                </template>
                            </v-radio>
                        </v-radio-group>
                        <v-row>
                            <v-col cols="12" md="6">
                                <v-text-field v-model.number="quantity" type="number" min="1" :max="maxQuantity"
                                    label="Quantity" density="compact"
                                    :hint="requiresReservation && selectedEvent ? `Up to ${maxQuantity} spots available` : 'How many passes'"
                                    persistent-hint></v-text-field>
                            </v-col>
                            <v-col v-if="!requiresReservation" cols="12" md="6">
                                <v-text-field v-model="validOnDate" type="date" label="Valid on (optional)" density="compact"></v-text-field>
                            </v-col>
                        </v-row>
                        <div class="text-body-1 mt-3" v-if="selectedProduct">
                            Total: <strong>${{ ((selectedProduct.priceCents * quantity) / 100).toFixed(2) }}</strong>
                        </div>
                        <v-btn color="primary" class="mt-4" :loading="creating" :disabled="!canProceedToPayment"
                            @click="createIntent">Continue to Payment</v-btn>
                    </v-card-text>
                </v-card>
            </v-stepper-window-item>

            <!-- Final step: Payment -->
            <v-stepper-window-item :value="paymentStep">
                <v-card class="mb-4 pa-4">
                    <v-card-title>Payment</v-card-title>
                    <v-card-text>
                        <div v-if="!branding.stripePublishableKey" class="text-error">
                            Stripe publishable key is not configured.
                        </div>
                        <div v-else>
                            <div id="payment-element" class="mb-4"></div>
                            <v-btn color="primary" :loading="paying" :disabled="!stripeReady" @click="pay">Pay ${{ displayAmount() }}</v-btn>
                            <div v-if="paymentError" class="text-error mt-3">{{ paymentError }}</div>
                        </div>
                    </v-card-text>
                </v-card>
            </v-stepper-window-item>
        </v-stepper>

        <v-card v-if="completed" variant="outlined" class="mt-6 pa-4 text-center">
            <v-alert type="success" class="mb-4">
                Purchase complete! Show this QR at the gate.
            </v-alert>
            <QrCode v-if="redemptionToken" :value="redeemUrl(redemptionToken)" :size="260" />
            <div class="text-caption text-medium-emphasis mt-3">
                Status will show as "paid" once Stripe confirms. Find it later on
                <router-link to="/User/MyPasses">My Passes</router-link>.
            </div>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted, nextTick, watch, computed } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import { DayPassService, type WaiverDto, type WaiverSignatureStatus, type DayPassProduct } from '@/services/DayPassService'
import { EventService, type EventDto } from '@/services/EventService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'
import RichTextView from '@/components/RichTextView.vue'
import QrCode from '@/components/QrCode.vue'

const route = useRoute()
const service = new DayPassService()
const eventService = new EventService()

const requiresReservation = computed(() => branding.requireReservationForPasses)
const stepLabels = computed(() => requiresReservation.value
    ? ['Waiver', 'Reserve', 'Select Pass', 'Payment']
    : ['Waiver', 'Select Pass', 'Payment'])

const selectPassStep = computed(() => requiresReservation.value ? 3 : 2)
const paymentStep = computed(() => requiresReservation.value ? 4 : 3)

const step = ref(1)

const waiver = ref<WaiverDto | null>(null)
const sigStatus = ref<WaiverSignatureStatus | null>(null)
const signing = ref(false)

const reservableEvents = ref<EventDto[]>([])
const selectedEventId = ref<string | null>(null)
const loadingEvents = ref(false)

const products = ref<DayPassProduct[]>([])
const selectedProductId = ref<string | null>(null)
const validOnDate = ref('')
const quantity = ref(1)
const creating = ref(false)

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

const selectedProduct = computed(() => products.value.find(p => p.id === selectedProductId.value) ?? null)
const selectedEvent = computed(() => reservableEvents.value.find(e => e.id === selectedEventId.value) ?? null)

const maxQuantity = computed(() => {
    if (!requiresReservation.value || !selectedEvent.value || !selectedEvent.value.capacity) return 50
    return Math.max(1, spotsLeft(selectedEvent.value))
})

const canProceedToPayment = computed(() => {
    if (!selectedProductId.value || quantity.value < 1) return false
    if (requiresReservation.value && !selectedEventId.value) return false
    return true
})

function spotsLeft(e: EventDto): number {
    if (!e.capacity) return 0
    return Math.max(0, e.capacity - (e.spotsReserved ?? 0))
}

function displayAmount() { return (amountCents.value / 100).toFixed(2) }

function formatDate(iso: string | null | undefined): string {
    if (!iso) return ''
    return dayjs.utc(iso).tz(branding.timezone || 'UTC').format('YYYY-MM-DD HH:mm')
}
function formatDateShort(iso: string): string {
    return dayjs.utc(iso).tz(branding.timezone || 'UTC').format('ddd, MMM D')
}

onMounted(async () => {
    try {
        const [w, s, p] = await Promise.all([
            service.getWaiver(),
            service.getMySignatureStatus(),
            service.listActive(),
        ])
        waiver.value = (w.data as any).data
        sigStatus.value = (s.data as any).data
        products.value = (p.data as any).data

        if (requiresReservation.value) {
            await loadReservableEvents()
            const queryEventId = route.query.eventId as string | undefined
            if (queryEventId && reservableEvents.value.some(e => e.id === queryEventId)) {
                selectedEventId.value = queryEventId
            }
        }

        if (sigStatus.value?.hasSignedCurrent) {
            step.value = requiresReservation.value ? 2 : 2
            // If event pre-selected via query param, auto-advance past reserve step.
            if (requiresReservation.value && selectedEventId.value) step.value = 3
        }
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load checkout.', 'error')
    }
})

async function loadReservableEvents() {
    loadingEvents.value = true
    try {
        const tz = branding.timezone || 'UTC'
        const fromUtc = dayjs().tz(tz).startOf('day').utc().toISOString()
        const toUtc = dayjs().tz(tz).startOf('day').add(60, 'day').utc().toISOString()
        const r = await eventService.list(fromUtc, toUtc)
        const all = (r.data as any).data as EventDto[]
        reservableEvents.value = all.filter(e => e.capacity && e.status !== 'cancelled')
    } finally {
        loadingEvents.value = false
    }
}

async function sign() {
    try {
        signing.value = true
        const r = await service.signWaiver()
        sigStatus.value = (r.data as any).data
        advanceFromWaiver()
        flash('Waiver signed.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Sign failed.', 'error')
    } finally {
        signing.value = false
    }
}

function advanceFromWaiver() {
    step.value = 2
}

async function createIntent() {
    if (!selectedProductId.value) return
    try {
        creating.value = true
        const body = {
            productId: selectedProductId.value,
            validOnDate: validOnDate.value ? dayjs(validOnDate.value).toISOString() : null,
            eventId: selectedEventId.value,
            quantity: quantity.value,
        }
        const r = await service.createPurchase(body)
        const data = (r.data as any).data
        purchaseId.value = data.purchaseId
        redemptionToken.value = data.redemptionToken
        clientSecret.value = data.clientSecret
        amountCents.value = data.amountCents
        step.value = paymentStep.value
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
    pe.mount('#payment-element')
    stripeReady.value = true
}

async function pay() {
    if (!stripe || !elements) return
    paying.value = true
    paymentError.value = null
    try {
        const { error } = await stripe.confirmPayment({
            elements,
            confirmParams: { return_url: window.location.origin + '/User/MyPasses' },
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

function hasBody(body: string | null | undefined): boolean {
    if (!body) return false
    return body.replace(/<[^>]+>/g, '').trim().length > 0
}

function redeemUrl(token: string): string {
    return `${window.location.protocol}//${window.location.host}/redeem/${token}`
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

watch(() => sigStatus.value?.hasSignedCurrent, (signed) => {
    if (signed && step.value === 1) advanceFromWaiver()
})

watch(quantity, (q) => {
    if (q > maxQuantity.value) quantity.value = maxQuantity.value
    if (q < 1) quantity.value = 1
})
</script>

<style scoped>
.waiver-body {
    background: rgba(0, 0, 0, 0.03);
    padding: 1rem;
    border-radius: 6px;
    max-height: 300px;
    overflow-y: auto;
}
</style>
