<template>
    <v-container style="max-width: 880px">
        <h1 class="text-h4 mb-4">Counter Sale</h1>
        <p class="text-body-2 text-medium-emphasis mb-4">
            For walk-ins without a device. Look up the rider, build their cart, capture waiver and payment.
        </p>

        <v-stepper v-model="step" :items="stepLabels" hide-actions>
        <v-stepper-window>
            <!-- Step 1: Rider -->
            <v-stepper-window-item :value="1">
                <v-card class="mb-4 pa-4">
                    <v-card-title>Rider</v-card-title>
                    <v-card-text>
                        <div v-if="!rider" class="d-flex ga-2 align-end">
                            <v-text-field v-model="riderEmail" type="email" label="Rider email"
                                density="compact" hide-details style="max-width: 360px"
                                @keyup.enter="findRider"></v-text-field>
                            <v-btn :loading="findingRider" @click="findRider">Find</v-btn>
                        </div>

                        <v-alert v-if="lookupError" type="info" variant="tonal" class="mt-3">
                            {{ lookupError }}
                            <div class="mt-2">
                                <v-btn size="small" color="primary" @click="showCreate = true">Create new rider</v-btn>
                            </div>
                        </v-alert>

                        <v-card v-if="showCreate && !rider" class="mt-3 pa-3" variant="outlined">
                            <div class="text-subtitle-2 mb-2">New rider</div>
                            <v-row>
                                <v-col cols="12" md="6">
                                    <v-text-field v-model="newRider.firstName" label="First name" density="compact"></v-text-field>
                                </v-col>
                                <v-col cols="12" md="6">
                                    <v-text-field v-model="newRider.lastName" label="Last name" density="compact"></v-text-field>
                                </v-col>
                            </v-row>
                            <v-text-field v-model="newRider.email" type="email" label="Email" density="compact"></v-text-field>
                            <v-btn color="primary" :loading="creatingRider" :disabled="!canCreateRider" @click="createRider">
                                Create rider
                            </v-btn>
                            <p class="text-caption text-medium-emphasis mt-2">
                                Account is created without a password. Rider can claim it later via password reset.
                            </p>
                        </v-card>

                        <div v-if="rider" class="mt-2">
                            <v-alert type="success" variant="tonal">
                                <strong>{{ rider.firstName }} {{ rider.lastName }}</strong> &lt;{{ rider.email }}&gt;
                                <span v-if="rider.hasSignedCurrentWaiver" class="text-caption ml-2">
                                    — waiver signed
                                </span>
                                <span v-else class="text-caption ml-2">— waiver not signed yet</span>
                            </v-alert>
                            <div class="mt-3">
                                <v-btn variant="text" size="small" @click="resetRider">Pick a different rider</v-btn>
                                <v-btn color="primary" class="ml-2" @click="step = 2">Continue to cart</v-btn>
                            </div>
                        </div>
                    </v-card-text>
                </v-card>
            </v-stepper-window-item>

            <!-- Step 2: Cart -->
            <v-stepper-window-item :value="2">
                <v-card class="mb-4 pa-4">
                    <v-card-title>Cart</v-card-title>
                    <v-card-text>
                        <v-tabs v-model="catalogTab" density="compact" class="mb-3">
                            <v-tab value="passes">Day Passes</v-tab>
                            <v-tab value="tickets">Event Tickets</v-tab>
                        </v-tabs>

                        <!-- Day passes -->
                        <div v-if="catalogTab === 'passes'">
                            <div v-if="loadingProducts" class="text-center py-4">
                                <v-progress-circular indeterminate></v-progress-circular>
                            </div>
                            <div v-else-if="products.length === 0" class="text-medium-emphasis">
                                No day pass products. Add some on the Day Passes admin page.
                            </div>
                            <v-table v-else density="compact">
                                <thead>
                                    <tr><th>Pass</th><th style="width: 100px">Price</th><th style="width: 160px">In cart</th></tr>
                                </thead>
                                <tbody>
                                    <tr v-for="p in products" :key="p.id">
                                        <td>
                                            <strong>{{ p.name }}</strong>
                                            <div v-if="p.description" class="text-caption text-medium-emphasis">{{ p.description }}</div>
                                        </td>
                                        <td>${{ (p.priceCents / 100).toFixed(2) }}</td>
                                        <td>
                                            <div class="d-flex align-center ga-1">
                                                <v-btn size="x-small" icon="mdi-minus" :disabled="qtyOf('day_pass', p.id) === 0"
                                                    @click="addToCart('day_pass', p.id, p.name, p.priceCents, -1)"></v-btn>
                                                <span style="min-width: 24px; text-align: center">{{ qtyOf('day_pass', p.id) }}</span>
                                                <v-btn size="x-small" icon="mdi-plus"
                                                    @click="addToCart('day_pass', p.id, p.name, p.priceCents, 1)"></v-btn>
                                            </div>
                                        </td>
                                    </tr>
                                </tbody>
                            </v-table>
                        </div>

                        <!-- Event tickets -->
                        <div v-else-if="catalogTab === 'tickets'">
                            <div class="d-flex align-center ga-2 mb-3">
                                <v-select v-model="selectedEventId" :items="eventOptions" item-title="title" item-value="id"
                                    label="Event" density="compact" hide-details style="max-width: 360px"
                                    @update:model-value="loadTiersForSelectedEvent"></v-select>
                            </div>
                            <div v-if="loadingTiers" class="text-center py-4">
                                <v-progress-circular indeterminate></v-progress-circular>
                            </div>
                            <v-table v-else-if="tiers.length > 0" density="compact">
                                <thead>
                                    <tr><th>Tier</th><th style="width: 100px">Price</th><th style="width: 100px">Left</th><th style="width: 160px">In cart</th></tr>
                                </thead>
                                <tbody>
                                    <tr v-for="t in tiers" :key="t.id">
                                        <td>{{ t.name }}</td>
                                        <td>${{ (t.priceCents / 100).toFixed(2) }}</td>
                                        <td>
                                            <span v-if="t.inventory !== null">
                                                {{ Math.max(0, t.inventory - (t.sold ?? 0)) }}
                                            </span>
                                            <span v-else class="text-medium-emphasis">∞</span>
                                        </td>
                                        <td>
                                            <div class="d-flex align-center ga-1">
                                                <v-btn size="x-small" icon="mdi-minus" :disabled="qtyOf('event_ticket', t.id) === 0"
                                                    @click="addToCart('event_ticket', t.id, `${selectedEventTitle}: ${t.name}`, t.priceCents, -1)"></v-btn>
                                                <span style="min-width: 24px; text-align: center">{{ qtyOf('event_ticket', t.id) }}</span>
                                                <v-btn size="x-small" icon="mdi-plus" :disabled="ticketAtCap(t)"
                                                    @click="addToCart('event_ticket', t.id, `${selectedEventTitle}: ${t.name}`, t.priceCents, 1)"></v-btn>
                                            </div>
                                        </td>
                                    </tr>
                                </tbody>
                            </v-table>
                            <div v-else-if="selectedEventId" class="text-medium-emphasis">No tiers configured for that event.</div>
                            <div v-else class="text-medium-emphasis">Pick an event to see tiers.</div>
                        </div>

                        <v-divider class="my-4"></v-divider>

                        <div class="text-subtitle-2 mb-2">In cart</div>
                        <v-table density="compact" v-if="cart.length > 0">
                            <tbody>
                                <tr v-for="(c, i) in cart" :key="i">
                                    <td>{{ c.displayName }}</td>
                                    <td style="width: 60px">×{{ c.quantity }}</td>
                                    <td style="width: 100px" class="text-right">${{ ((c.unitPriceCents * c.quantity) / 100).toFixed(2) }}</td>
                                </tr>
                                <tr>
                                    <td colspan="2" class="text-right"><strong>Total</strong></td>
                                    <td class="text-right"><strong>${{ totalDollars }}</strong></td>
                                </tr>
                            </tbody>
                        </v-table>
                        <div v-else class="text-medium-emphasis">Cart is empty.</div>

                        <div class="d-flex mt-4 ga-2">
                            <v-btn variant="text" @click="step = 1">Back</v-btn>
                            <v-spacer></v-spacer>
                            <v-btn color="primary" :disabled="cart.length === 0" @click="advanceFromCart">Continue</v-btn>
                        </div>
                    </v-card-text>
                </v-card>
            </v-stepper-window-item>

            <!-- Step 3: Waiver -->
            <v-stepper-window-item :value="3">
                <v-card class="mb-4 pa-4">
                    <v-card-title>Waiver</v-card-title>
                    <v-card-text>
                        <div v-if="!activeWaiver" class="text-medium-emphasis">
                            No active waiver configured for this tenant — proceed to payment.
                        </div>
                        <template v-else-if="rider?.hasSignedCurrentWaiver">
                            <v-alert type="success" variant="tonal" class="mb-3">
                                <div class="d-flex align-center">
                                    <v-icon class="mr-2">mdi-file-sign</v-icon>
                                    <div>
                                        <div><strong>{{ rider.firstName }} {{ rider.lastName }}</strong> already signed this waiver.</div>
                                        <div v-if="rider.waiverSignedAtUtc" class="text-caption">
                                            Signed {{ formatSignedAt(rider.waiverSignedAtUtc) }}
                                        </div>
                                    </div>
                                </div>
                            </v-alert>
                        </template>
                        <template v-else>
                            <p class="text-body-2 text-medium-emphasis mb-2">
                                Hand the device to the rider. The rider must read &amp; agree to:
                            </p>
                            <v-card variant="outlined" class="pa-3 waiver-body mb-3">
                                <div v-if="hasBody(activeWaiver.body)"><RichTextView :html="activeWaiver.body" /></div>
                                <div v-else class="text-medium-emphasis">
                                    (Tenant has not filled in waiver text yet.)
                                </div>
                            </v-card>
                            <v-checkbox v-model="riderAcknowledged" hide-details
                                label="The rider has read and agrees to this waiver"></v-checkbox>
                        </template>
                        <div class="d-flex mt-4 ga-2">
                            <v-btn variant="text" @click="step = 2">Back</v-btn>
                            <v-spacer></v-spacer>
                            <v-btn color="primary" :disabled="!canAdvanceFromWaiver" @click="advanceFromWaiver">Continue</v-btn>
                        </div>
                    </v-card-text>
                </v-card>
            </v-stepper-window-item>

            <!-- Step 4: Payment -->
            <v-stepper-window-item :value="4">
                <v-card class="mb-4 pa-4">
                    <v-card-title>Payment</v-card-title>
                    <v-card-text>
                        <div v-if="!branding.stripePublishableKey" class="text-error">
                            Stripe publishable key is not configured for this tenant.
                        </div>
                        <div v-else-if="!clientSecret" class="text-center py-4">
                            <v-progress-circular indeterminate></v-progress-circular>
                            <div class="text-caption mt-2">Preparing charge…</div>
                        </div>
                        <div v-else>
                            <div class="mb-3">
                                Total: <strong>${{ totalDollars }}</strong>
                            </div>
                            <div id="payment-element" class="mb-4"></div>
                            <v-btn color="primary" :loading="paying" :disabled="!stripeReady" @click="pay">
                                Charge ${{ totalDollars }}
                            </v-btn>
                            <div v-if="paymentError" class="text-error mt-3">{{ paymentError }}</div>
                        </div>
                    </v-card-text>
                </v-card>
            </v-stepper-window-item>

            <!-- Step 5: Receipt -->
            <v-stepper-window-item :value="5">
                <v-card class="mb-4 pa-4">
                    <v-card-title>Sale complete</v-card-title>
                    <v-card-text>
                        <v-alert type="success" variant="tonal" class="mb-3">
                            Charged ${{ totalDollars }}. Each line item below has its own QR for redemption.
                        </v-alert>
                        <div v-for="li in lineItems" :key="li.purchaseId" class="mb-4 d-flex align-center ga-3">
                            <QrCode :value="redeemUrl(li.redemptionToken)" :size="120" />
                            <div>
                                <div><strong>{{ li.displayName }}</strong> ×{{ li.quantity }}</div>
                                <div class="text-caption text-medium-emphasis">
                                    ${{ (li.lineAmountCents / 100).toFixed(2) }} · {{ li.kind === 'day_pass' ? 'Day Pass' : 'Event Ticket' }}
                                </div>
                                <div class="text-caption">
                                    <code>{{ li.redemptionToken }}</code>
                                </div>
                            </div>
                        </div>
                        <v-btn color="primary" class="mt-3" @click="reset">New sale</v-btn>
                    </v-card-text>
                </v-card>
            </v-stepper-window-item>
        </v-stepper-window>
        </v-stepper>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
import dayjs from 'dayjs'
import { CounterService, type CounterRider } from '@/services/CounterService'
import { DayPassService, type DayPassProduct, type WaiverDto } from '@/services/DayPassService'
import { EventService, type EventDto } from '@/services/EventService'
import { TicketService, type TicketTier } from '@/services/TicketService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'
import RichTextView from '@/components/RichTextView.vue'
import QrCode from '@/components/QrCode.vue'

interface CartLine {
    kind: 'day_pass' | 'event_ticket'
    itemId: string
    displayName: string
    unitPriceCents: number
    quantity: number
}

const counter = new CounterService()
const passService = new DayPassService()
const eventService = new EventService()
const ticketService = new TicketService()

const stepLabels = ['Rider', 'Cart', 'Waiver', 'Payment', 'Receipt']
const step = ref(1)

// Rider step
const riderEmail = ref('')
const findingRider = ref(false)
const lookupError = ref<string | null>(null)
const showCreate = ref(false)
const creatingRider = ref(false)
const newRider = ref({ firstName: '', lastName: '', email: '' })
const rider = ref<CounterRider | null>(null)
const canCreateRider = computed(() => !!newRider.value.firstName && !!newRider.value.lastName && /\S+@\S+/.test(newRider.value.email))

// Cart step
const catalogTab = ref('passes')
const products = ref<DayPassProduct[]>([])
const loadingProducts = ref(false)
const events = ref<EventDto[]>([])
const selectedEventId = ref<string | null>(null)
const tiers = ref<TicketTier[]>([])
const loadingTiers = ref(false)
const cart = ref<CartLine[]>([])
const eventOptions = computed(() => events.value.map(e => ({
    id: e.id,
    title: `${e.title} — ${dayjs.utc(e.startsAtUtc).tz(branding.timezone || 'UTC').format('MMM D')}`,
})))
const selectedEventTitle = computed(() => events.value.find(e => e.id === selectedEventId.value)?.title ?? '')

// Waiver step
const activeWaiver = ref<WaiverDto | null>(null)
const riderAcknowledged = ref(false)
const willSignWaiver = computed(() => activeWaiver.value !== null && rider.value?.hasSignedCurrentWaiver === false)
const canAdvanceFromWaiver = computed(() => !willSignWaiver.value || riderAcknowledged.value)

// Payment step
const clientSecret = ref<string | null>(null)
const totalAmountCents = ref(0)
const lineItems = ref<any[]>([])
const stripeReady = ref(false)
const paying = ref(false)
const paymentError = ref<string | null>(null)
let stripe: any = null
let elements: any = null

const totalDollars = computed(() => {
    const cents = clientSecret.value ? totalAmountCents.value : cart.value.reduce((sum, c) => sum + c.unitPriceCents * c.quantity, 0)
    return (cents / 100).toFixed(2)
})

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(async () => {
    loadingProducts.value = true
    try {
        const [p, w, e] = await Promise.all([
            passService.listActive(),
            passService.getWaiver().catch(() => ({ data: { data: null } })),
            eventService.list(dayjs().subtract(1, 'day').utc().toISOString(), dayjs().add(60, 'day').utc().toISOString()),
        ])
        products.value = (p.data as any).data
        activeWaiver.value = (w.data as any).data ?? null
        events.value = ((e.data as any).data as EventDto[]).filter(ev => ev.status === 'scheduled' && ev.hasActiveTiers)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load catalog.', 'error')
    } finally {
        loadingProducts.value = false
    }
})

async function findRider() {
    if (!riderEmail.value.trim()) return
    findingRider.value = true
    lookupError.value = null
    try {
        const r = await counter.findRider(riderEmail.value.trim())
        rider.value = (r.data as any).data
        showCreate.value = false
    } catch (err: any) {
        if (err.response?.status === 404) {
            lookupError.value = `No rider found for "${riderEmail.value.trim()}".`
            newRider.value = { firstName: '', lastName: '', email: riderEmail.value.trim() }
        } else {
            flash(err.response?.data?.error || 'Lookup failed.', 'error')
        }
    } finally {
        findingRider.value = false
    }
}

async function createRider() {
    creatingRider.value = true
    try {
        const r = await counter.createRider({
            email: newRider.value.email.trim(),
            firstName: newRider.value.firstName.trim(),
            lastName: newRider.value.lastName.trim(),
        })
        rider.value = { ...(r.data as any).data, hasSignedCurrentWaiver: false }
        showCreate.value = false
        lookupError.value = null
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not create rider.', 'error')
    } finally {
        creatingRider.value = false
    }
}

function resetRider() {
    rider.value = null
    riderEmail.value = ''
    lookupError.value = null
    showCreate.value = false
    cart.value = []
}

async function loadTiersForSelectedEvent() {
    if (!selectedEventId.value) { tiers.value = []; return }
    loadingTiers.value = true
    try {
        const r = await ticketService.listActiveTiers(selectedEventId.value)
        tiers.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load tiers.', 'error')
    } finally {
        loadingTiers.value = false
    }
}

function qtyOf(kind: 'day_pass' | 'event_ticket', itemId: string): number {
    return cart.value.find(c => c.kind === kind && c.itemId === itemId)?.quantity ?? 0
}

function ticketAtCap(t: TicketTier): boolean {
    if (t.inventory === null) return false
    const remaining = t.inventory - (t.sold ?? 0)
    return qtyOf('event_ticket', t.id) >= remaining
}

function addToCart(kind: 'day_pass' | 'event_ticket', itemId: string, displayName: string, unitPriceCents: number, delta: number) {
    const existing = cart.value.find(c => c.kind === kind && c.itemId === itemId)
    if (existing) {
        existing.quantity += delta
        if (existing.quantity <= 0) {
            cart.value = cart.value.filter(c => c !== existing)
        }
    } else if (delta > 0) {
        cart.value.push({ kind, itemId, displayName, unitPriceCents, quantity: delta })
    }
}

function advanceFromCart() {
    // Skip waiver step if there's no active waiver and rider has signed (or no waiver exists).
    if (!willSignWaiver.value) {
        step.value = 3   // still go through waiver step to show "already signed" then click continue
    } else {
        step.value = 3
    }
}

async function advanceFromWaiver() {
    step.value = 4
    await createSale()
}

async function createSale() {
    if (!rider.value) return
    paymentError.value = null
    try {
        const r = await counter.createSale({
            riderId: rider.value.id,
            items: cart.value.map(c => ({ kind: c.kind, itemId: c.itemId, quantity: c.quantity })),
            signWaiver: willSignWaiver.value && riderAcknowledged.value,
        })
        const data = (r.data as any).data
        clientSecret.value = data.clientSecret
        totalAmountCents.value = data.totalAmountCents
        lineItems.value = data.lineItems
        await nextTick()
        await mountPaymentElement()
    } catch (err: any) {
        paymentError.value = err.response?.data?.error || 'Could not start payment.'
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
            confirmParams: { return_url: window.location.href },
            redirect: 'if_required',
        })
        if (error) {
            paymentError.value = error.message || 'Payment failed.'
        } else {
            step.value = 5
        }
    } catch (err: any) {
        paymentError.value = err?.message || 'Payment failed.'
    } finally {
        paying.value = false
    }
}

function reset() {
    step.value = 1
    rider.value = null
    riderEmail.value = ''
    cart.value = []
    clientSecret.value = null
    totalAmountCents.value = 0
    lineItems.value = []
    stripeReady.value = false
    riderAcknowledged.value = false
    paymentError.value = null
}

function redeemUrl(token: string): string {
    return `${window.location.protocol}//${window.location.host}/redeem/${token}`
}

function formatSignedAt(utc: string): string {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('MMM D, YYYY [at] h:mm A')
}

function hasBody(body: string | null | undefined): boolean {
    if (!body) return false
    return body.replace(/<[^>]+>/g, '').trim().length > 0
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>

<style scoped>
.waiver-body {
    max-height: 280px;
    overflow-y: auto;
    background: rgba(0, 0, 0, 0.03);
}
</style>
