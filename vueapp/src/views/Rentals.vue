<template>
    <v-container class="rentals-page">
        <div class="d-flex align-center mb-1 ga-3 flex-wrap">
            <h1 class="text-h4">Rent a Bike</h1>
            <v-spacer />
            <v-btn v-if="cart.length" color="primary" prepend-icon="mdi-bike" @click="cartOpen = true">
                Booking ({{ cart.length }}) · {{ money(cartRentalTotal) }}
            </v-btn>
        </div>
        <p class="text-body-2 text-medium-emphasis mb-4">
            Reserve online, sign the waiver and collect your gear at the shop. A refundable deposit is
            held on your card and released at return.
        </p>

        <!-- Rental window -->
        <v-card variant="tonal" class="mb-5">
            <v-card-text class="d-flex ga-4 flex-wrap align-center">
                <div>
                    <div class="text-caption text-medium-emphasis mb-1">Pick up</div>
                    <input type="date" v-model="startDate" :min="today" class="rental-date" />
                </div>
                <v-icon icon="mdi-arrow-right" class="mt-4 text-medium-emphasis" />
                <div>
                    <div class="text-caption text-medium-emphasis mb-1">Return</div>
                    <input type="date" v-model="endDate" :min="startDate" class="rental-date" />
                </div>
                <v-chip color="primary" variant="flat" class="mt-4">{{ days }} day{{ days === 1 ? '' : 's' }}</v-chip>
            </v-card-text>
        </v-card>

        <v-card v-if="loading" class="pa-8 text-center"><v-progress-circular indeterminate color="primary" /></v-card>
        <v-alert v-else-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>
        <v-card v-else-if="products.length === 0" class="pa-8 text-center text-medium-emphasis">
            No rentals are listed right now. Check back soon.
        </v-card>

        <v-row v-else>
            <v-col v-for="p in products" :key="p.id" cols="12" sm="6" md="4">
                <v-card class="d-flex flex-column" height="100%">
                    <v-img v-if="p.imageUrl" :src="absoluteUrl(p.imageUrl)" height="180" cover />
                    <div v-else class="rental-ph"><v-icon size="40" color="grey-lighten-1">mdi-bike</v-icon></div>
                    <v-card-text class="flex-grow-1">
                        <div class="text-caption text-medium-emphasis">{{ p.brand || '' }}</div>
                        <div class="font-weight-medium text-body-1">{{ p.name }}</div>
                        <div v-if="p.description" class="text-caption text-medium-emphasis mt-1 rental-desc">{{ p.description }}</div>

                        <v-select v-if="p.variants.length > 1" v-model="picked[p.id]" :items="variantItems(p)"
                            item-title="title" item-value="id" density="compact" hide-details label="Size / option"
                            class="mt-3" />
                        <div class="d-flex align-center justify-space-between mt-3">
                            <div>
                                <span class="text-primary font-weight-bold">{{ money(rateFor(p)) }}</span>
                                <span class="text-caption text-medium-emphasis"> / day</span>
                            </div>
                            <span class="text-caption text-medium-emphasis">{{ availabilityLabel(p) }}</span>
                        </div>
                        <div v-if="depositFor(p) > 0" class="text-caption text-medium-emphasis mt-1">
                            Refundable deposit: {{ money(depositFor(p)) }}
                        </div>
                    </v-card-text>
                    <v-card-actions>
                        <v-spacer />
                        <v-btn color="primary" variant="tonal" size="small" prepend-icon="mdi-plus"
                            :disabled="!canAdd(p)" @click="addToCart(p)">
                            {{ availForSelected(p) === 0 ? 'Unavailable' : 'Add to booking' }}
                        </v-btn>
                    </v-card-actions>
                </v-card>
            </v-col>
        </v-row>

        <!-- Booking cart -->
        <v-dialog v-model="cartOpen" max-width="520">
            <v-card class="d-flex flex-column" style="max-height: 90vh">
                <v-card-title class="d-flex align-center" style="flex: 0 0 auto">
                    <span>Your booking</span>
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="booking" @click="cartOpen = false" />
                </v-card-title>
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                    <div class="text-caption text-medium-emphasis mb-2">
                        {{ formatDay(startDate) }} to {{ formatDay(endDate) }} · {{ days }} day{{ days === 1 ? '' : 's' }}
                    </div>
                    <div v-for="(l, i) in cart" :key="i" class="d-flex align-center ga-2 py-1">
                        <div class="flex-grow-1 text-body-2">
                            {{ l.name }}<span v-if="l.label" class="text-medium-emphasis"> ({{ l.label }})</span>
                            <span class="text-medium-emphasis"> x{{ l.qty }}</span>
                        </div>
                        <span class="text-body-2" style="min-width: 70px; text-align: right">{{ money(l.rateCents * days * l.qty) }}</span>
                        <v-btn icon="mdi-close" size="x-small" variant="text" @click="cart.splice(i, 1)" />
                    </div>
                    <v-divider class="my-3" />
                    <div class="d-flex justify-space-between text-body-2"><span>Rental</span><span>{{ money(cartRentalTotal) }}</span></div>
                    <div class="d-flex justify-space-between text-body-2 text-medium-emphasis mt-1">
                        <span>Refundable deposit (held, not charged)</span>
                        <span>
                            <span v-if="insurance" class="text-decoration-line-through mr-1">{{ money(cartDepositTotal) }}</span>
                            {{ insurance ? money(0) : money(cartDepositTotal) }}
                        </span>
                    </div>
                    <v-checkbox v-if="branding.rentalInsuranceEnabled && branding.rentalInsuranceBps > 0"
                        v-model="insurance" density="compact" hide-details class="mt-2">
                        <template #label>
                            <span class="text-body-2">{{ branding.rentalInsuranceLabel }} (+{{ money(insuranceCents) }})</span>
                        </template>
                    </v-checkbox>
                    <div v-if="insurance" class="text-caption text-medium-emphasis mt-1">
                        The refundable deposit is waived.
                    </div>
                    <p class="text-caption text-medium-emphasis mt-3 mb-0">
                        Taxes and any service fee are calculated at checkout. Your gear is reserved once payment
                        completes; sign the waiver and collect it at the shop.
                    </p>
                    <div v-if="bookError" class="text-error text-body-2 mt-2">{{ bookError }}</div>
                </v-card-text>
                <v-card-actions style="flex: 0 0 auto">
                    <v-spacer />
                    <v-btn :disabled="booking" @click="cartOpen = false">Keep browsing</v-btn>
                    <v-btn color="primary" :loading="booking" :disabled="cart.length === 0" @click="startBooking">
                        Reserve
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Payment (fee then deposit hold) -->
        <v-dialog v-model="payOpen" persistent max-width="480">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ payStage === 'fee' ? 'Payment' : 'Deposit hold' }}</span>
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="paying" @click="payOpen = false" />
                </v-card-title>
                <v-card-text>
                    <div v-if="payStage === 'fee'" class="text-h6 mb-1">{{ money(pendingTotal) }}</div>
                    <div v-else class="mb-2">
                        <div class="text-h6">{{ money(pendingDeposit) }}</div>
                        <div class="text-caption text-medium-emphasis">Refundable hold, released when you return the gear.</div>
                    </div>
                    <div id="rental-payment-element" class="mb-4"></div>
                    <div v-if="payError" class="text-error text-body-2 mb-2">{{ payError }}</div>
                    <v-btn block color="primary" size="large" :loading="paying" :disabled="!stripeReady" @click="payCurrent">
                        {{ payStage === 'fee' ? `Pay ${money(pendingTotal)}` : `Authorize ${money(pendingDeposit)} hold` }}
                    </v-btn>
                </v-card-text>
            </v-card>
        </v-dialog>

        <!-- Done -->
        <v-dialog v-model="doneOpen" max-width="440">
            <v-card class="text-center pa-4">
                <v-icon size="56" color="success" class="mx-auto">mdi-check-circle</v-icon>
                <div class="text-h6 mt-2">You're booked!</div>
                <p class="text-body-2 text-medium-emphasis mt-2">
                    Your gear is reserved for {{ formatDay(startDate) }}. We've emailed your confirmation.
                    Sign the waiver and collect it at the shop counter.
                </p>
                <v-btn color="primary" class="mt-2" @click="doneOpen = false">Done</v-btn>
            </v-card>
        </v-dialog>

        <InlineAuthDialog v-model="authOpen" @authed="onAuthed" />
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, nextTick, watch } from 'vue'
import dayjs from 'dayjs'
import authHelper from '@/helpers/AuthHelper'
import InlineAuthDialog from '@/components/InlineAuthDialog.vue'
import { getStripe } from '@/helpers/StripeHelper'
import { branding, loadBranding } from '@/stores/branding'
import { BikeShopService, type RentalCatalogProduct } from '@/services/BikeShopService'

const service = new BikeShopService()

const tz = () => branding.timezone || 'UTC'
const today = dayjs().tz(tz()).format('YYYY-MM-DD')
const startDate = ref(dayjs().tz(tz()).add(1, 'day').format('YYYY-MM-DD'))
const endDate = ref(dayjs().tz(tz()).add(1, 'day').format('YYYY-MM-DD'))
const days = computed(() => Math.max(1, dayjs(endDate.value).diff(dayjs(startDate.value), 'day') + 1))

const products = ref<RentalCatalogProduct[]>([])
const loading = ref(false)
const loadError = ref('')
const picked = ref<Record<string, string>>({})
// variantId -> available count for the current window (filled on demand)
const availByVariant = ref<Record<string, number>>({})

interface CartLine { variantId: string; name: string; label: string; rateCents: number; depositCents: number; qty: number }
const cart = ref<CartLine[]>([])
const cartOpen = ref(false)

function money(c: number) { return '$' + (c / 100).toFixed(2) }
function absoluteUrl(u: string) { return u.startsWith('http') ? u : u }
function formatDay(d: string) { return dayjs(d).format('ddd, MMM D') }

function selectedVariant(p: RentalCatalogProduct) {
    const vid = picked.value[p.id] ?? p.variants[0]?.id
    return p.variants.find(v => v.id === vid) ?? p.variants[0]
}
function variantItems(p: RentalCatalogProduct) {
    return p.variants.map(v => ({
        id: v.id,
        title: [v.size, v.color].filter(Boolean).join(' / ') || 'Standard',
    }))
}
function rateFor(p: RentalCatalogProduct) { return selectedVariant(p)?.dailyRateCents ?? 0 }
function depositFor(p: RentalCatalogProduct) { return selectedVariant(p)?.depositCents ?? 0 }
function availForSelected(p: RentalCatalogProduct): number {
    const v = selectedVariant(p)
    if (!v) return 0
    // Fall back to the coarse on-floor count until the per-window check resolves.
    return availByVariant.value[v.id] ?? v.onFloor
}
function availabilityLabel(p: RentalCatalogProduct) {
    const n = availForSelected(p)
    return n > 0 ? `${n} available` : 'None available for these dates'
}
function inCartQty(variantId: string) {
    return cart.value.filter(l => l.variantId === variantId).reduce((s, l) => s + l.qty, 0)
}
function canAdd(p: RentalCatalogProduct) {
    const v = selectedVariant(p)
    return !!v && availForSelected(p) - inCartQty(v.id) > 0
}
function addToCart(p: RentalCatalogProduct) {
    const v = selectedVariant(p)
    if (!v) return
    const existing = cart.value.find(l => l.variantId === v.id)
    if (existing) existing.qty += 1
    else cart.value.push({
        variantId: v.id, name: p.name,
        label: [v.size, v.color].filter(Boolean).join(' / '),
        rateCents: v.dailyRateCents, depositCents: v.depositCents, qty: 1,
    })
    cartOpen.value = true
}

const cartRentalTotal = computed(() => cart.value.reduce((s, l) => s + l.rateCents * days.value * l.qty, 0))
const cartDepositTotal = computed(() => cart.value.reduce((s, l) => s + l.depositCents * l.qty, 0))

// Optional damage-protection add-on. Preview only; the server computes the actual charge.
const insurance = ref(false)
const insuranceCents = computed(() => Math.round(cartRentalTotal.value * branding.rentalInsuranceBps / 10000))

const startUtc = computed(() => dayjs.tz(startDate.value, tz()).startOf('day').utc().toISOString())
// Whole-day rental: the window is [pickup 00:00, return 24:00) so an inclusive same-day
// booking is one day. Half-open end = return date + 1 day at midnight.
const endUtc = computed(() => dayjs.tz(endDate.value, tz()).add(1, 'day').startOf('day').utc().toISOString())

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        const r = await service.rentalCatalog()
        products.value = r.data.data.products
        await refreshAvailability()
    } catch (e: any) {
        loadError.value = e.response?.data?.error || 'Could not load rentals. Refresh the page to try again.'
    } finally {
        loading.value = false
    }
}

// Per-window availability for every listed variant. Serialized bikes have real per-window
// counts; pool gear too. Best-effort: a failed probe leaves the coarse on-floor count.
async function refreshAvailability() {
    const variantIds = products.value.flatMap(p => p.variants.map(v => v.id))
    const results = await Promise.allSettled(
        variantIds.map(id => service.storeRentalAvailability(id, startUtc.value, endUtc.value)))
    const next: Record<string, number> = {}
    results.forEach((res, i) => {
        if (res.status === 'fulfilled') next[variantIds[i]] = res.value.data.data.available
    })
    availByVariant.value = next
}

// Re-check availability when the window changes, and drop cart lines that no longer fit.
watch([startDate, endDate], async () => {
    if (dayjs(endDate.value).isBefore(dayjs(startDate.value))) endDate.value = startDate.value
    await refreshAvailability()
    cart.value = cart.value.filter(l => (availByVariant.value[l.variantId] ?? 0) >= l.qty)
})

// ── Booking + payment ────────────────────────────────────────────────────────
const booking = ref(false)
const bookError = ref('')
const authOpen = ref(false)

const payOpen = ref(false)
const payStage = ref<'fee' | 'deposit'>('fee')
const paying = ref(false)
const payError = ref('')
const stripeReady = ref(false)
const pendingTotal = ref(0)
const pendingDeposit = ref(0)
const feeSecret = ref<string | null>(null)
const depositSecret = ref<string | null>(null)
let stripe: any = null
let elements: any = null
const doneOpen = ref(false)

function startBooking() {
    if (cart.value.length === 0) return
    if (!authHelper.isAuthenticated()) { authOpen.value = true; return }
    doBook()
}
function onAuthed() { doBook() }

async function doBook() {
    booking.value = true
    bookError.value = ''
    try {
        const r = await service.storeBookRental({
            lines: cart.value.map(l => ({ variantId: l.variantId, quantity: l.qty })),
            startsAt: startUtc.value,
            endsAt: endUtc.value,
            insurance: insurance.value,
        })
        const d = r.data.data
        pendingTotal.value = d.totalCents
        pendingDeposit.value = d.depositCents
        feeSecret.value = d.clientSecret
        depositSecret.value = d.depositClientSecret
        cartOpen.value = false
        payStage.value = 'fee'
        payOpen.value = true
        await nextTick()
        await mountPayment(feeSecret.value)
    } catch (e: any) {
        bookError.value = e.response?.data?.error || 'Could not reserve your gear. Nothing was charged.'
    } finally {
        booking.value = false
    }
}

async function mountPayment(secret: string | null) {
    payError.value = ''
    stripeReady.value = false
    if (!secret) { payError.value = 'Payment could not be started.'; return }
    const account = branding.stripeChargeMode === 'direct' ? branding.stripeConnectAccountId : null
    stripe = await getStripe(branding.stripePublishableKey, account)
    if (!stripe) { payError.value = 'Payments are unavailable right now.'; return }
    const host = document.getElementById('rental-payment-element')
    if (host) host.innerHTML = ''
    elements = stripe.elements({ clientSecret: secret })
    elements.create('payment').mount('#rental-payment-element')
    stripeReady.value = true
}

async function payCurrent() {
    if (!stripe || !elements) return
    paying.value = true
    payError.value = ''
    try {
        const { error, paymentIntent } = await stripe.confirmPayment({ elements, redirect: 'if_required' })
        if (error) {
            payError.value = error.message || 'Payment failed. Check the card and try again.'
        } else if (payStage.value === 'fee') {
            if (paymentIntent?.status === 'succeeded') {
                try { await service.confirmIntent(paymentIntent.id) } catch { /* webhook finalizes */ }
                if (depositSecret.value && pendingDeposit.value > 0) {
                    payStage.value = 'deposit'
                    await nextTick()
                    await mountPayment(depositSecret.value)
                } else {
                    finish()
                }
            } else {
                payError.value = 'The payment has not settled yet. It will complete shortly; watch your email.'
            }
        } else {
            // Deposit is a manual-capture hold: authorized = 'requires_capture'.
            if (paymentIntent?.status === 'requires_capture' || paymentIntent?.status === 'succeeded') {
                finish()
            } else {
                payError.value = 'The deposit hold did not authorize. Try a different card.'
            }
        }
    } catch (e: any) {
        payError.value = e?.message || 'Payment failed. Please try again.'
    } finally {
        paying.value = false
    }
}

function finish() {
    payOpen.value = false
    cart.value = []
    insurance.value = false
    doneOpen.value = true
    refreshAvailability()
}

onMounted(async () => {
    if (!branding.loaded) await loadBranding()
    await load()
})
</script>

<style scoped>
.rentals-page { max-width: 1100px; }
.rental-date {
    border: 1px solid rgba(var(--v-theme-on-surface), 0.3);
    border-radius: 6px;
    padding: 8px 10px;
    font-size: 14px;
    background: rgb(var(--v-theme-surface));
    color: rgb(var(--v-theme-on-surface));
}
.rental-ph {
    height: 180px;
    display: flex;
    align-items: center;
    justify-content: center;
    background: rgba(var(--v-theme-on-surface), 0.04);
}
.rental-desc {
    display: -webkit-box;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    overflow: hidden;
}
</style>
