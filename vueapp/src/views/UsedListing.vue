<template>
    <v-container class="ul-page">
        <div class="d-flex align-center mb-3">
            <v-btn variant="text" size="small" prepend-icon="mdi-arrow-left" to="/Shop">Back to the shop</v-btn>
        </div>

        <v-card v-if="loading" class="pa-8 text-center"><v-progress-circular indeterminate color="primary" /></v-card>

        <v-card v-else-if="gone" class="pa-8 text-center ul-card">
            <v-icon size="40" class="text-muted2">mdi-bike</v-icon>
            <div class="text-h6 mt-2">{{ loadError || 'This bike has been sold or is no longer listed.' }}</div>
            <p class="text-body-2 text-muted mt-1 mb-4">Other used and ex-rental bikes may still be available.</p>
            <v-btn color="primary" to="/Shop">See what's for sale</v-btn>
        </v-card>

        <v-card v-else-if="listing" class="ul-card">
            <v-row class="pa-4" no-gutters>
                <!-- Gallery: cover first, then the listing photos, de-duplicated. -->
                <v-col cols="12" md="7" class="pr-md-6">
                    <v-img v-if="currentImage" :src="currentImage" :aspect-ratio="4 / 3" cover rounded class="ul-hero"
                        tabindex="0" @keydown.left.prevent="step(-1)" @keydown.right.prevent="step(1)"></v-img>
                    <div v-else class="ul-noimage d-flex align-center justify-center rounded">
                        <v-icon size="48" class="text-muted2">mdi-image-off</v-icon>
                    </div>
                    <div v-if="currentCaption" class="text-caption text-muted mt-1">{{ currentCaption }}</div>
                    <div v-if="gallery.length > 1" class="d-flex ga-2 mt-2 ul-thumbs">
                        <v-img v-for="(g, i) in gallery" :key="i" :src="g.url" :aspect-ratio="1" cover rounded class="ul-thumb"
                            :class="{ 'ul-thumb--active': i === index }" role="button" :aria-label="`Photo ${i + 1}`" @click="index = i"></v-img>
                    </div>
                </v-col>

                <!-- Detail + buy -->
                <v-col cols="12" md="5" class="mt-4 mt-md-0">
                    <div class="d-flex align-center ga-2 flex-wrap">
                        <span class="rp-pill rp-pill--solid">Used</span>
                        <span class="rp-pill" :class="gradePillClass">{{ gradeLabel(listing.conditionGrade) }} condition</span>
                    </div>
                    <div v-if="listing.categoryName" class="text-caption text-muted mt-3">{{ listing.categoryName }}</div>
                    <h1 class="font-display text-h4 mt-1">{{ listing.title }}</h1>
                    <div class="font-display text-h3 mt-2">{{ money(listing.askingPriceCents) }}</div>

                    <div v-if="specs.length" class="d-flex ga-4 flex-wrap mt-4">
                        <div v-for="s in specs" :key="s.label">
                            <div class="text-caption text-muted">{{ s.label }}</div>
                            <div class="text-body-2 font-weight-medium">{{ s.value }}</div>
                        </div>
                    </div>

                    <div v-if="listing.conditionNotes" class="mt-4">
                        <div class="text-caption text-muted">Condition notes</div>
                        <p class="text-body-2 mb-0" style="white-space: pre-line">{{ listing.conditionNotes }}</p>
                    </div>

                    <!-- Their own checkout holds it: Buy resumes that checkout (the Order call answers
                         409 held_by_you with the same PaymentIntent), so it stays enabled. -->
                    <v-alert v-if="heldByYou" type="info" variant="tonal" density="compact" class="mt-4">
                        You started checking out this bike. Finish paying to make it yours.
                    </v-alert>
                    <v-alert v-else-if="listing.reserved" type="info" variant="tonal" density="compact" class="mt-4">
                        Someone else is checking out this bike right now; try again in a few minutes.
                    </v-alert>

                    <v-btn color="primary" block size="large" class="mt-5"
                        :prepend-icon="heldByYou ? 'mdi-credit-card-outline' : 'mdi-storefront-outline'"
                        :loading="ordering" :disabled="listing.reserved && !heldByYou" @click="buy">
                        {{ heldByYou ? 'Finish paying' : 'Buy, pick up at the shop' }}
                    </v-btn>
                    <p class="text-caption text-muted mt-2 mb-0">
                        Pay now, pick up at the shop counter. You'll get an order number by email.
                        One of a kind, so it's held for you only while you check out. Promo codes and store credit don't apply to used bikes.
                    </p>
                    <div v-if="orderError" class="text-error text-body-2 mt-2">{{ orderError }}</div>
                </v-col>
            </v-row>
        </v-card>

        <!-- ── Card payment: same Stripe Elements flow as the store ───────── -->
        <v-dialog v-model="payOpen" persistent max-width="480">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Payment</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="paying" @click="closePay"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div class="text-h6 mb-3">{{ money(pendingDue) }}</div>
                    <div id="used-payment-element" class="mb-4"></div>
                    <div v-if="payError" class="text-error text-body-2 mb-2">{{ payError }}</div>
                    <v-btn block color="primary" size="large" :loading="paying" :disabled="!stripeReady" @click="confirmPay">
                        Pay {{ money(pendingDue) }}
                    </v-btn>
                    <p class="text-caption text-muted mt-3 mb-0">This bike is held for you for 20 minutes while you pay.</p>
                </v-card-text>
            </v-card>
        </v-dialog>

        <!-- ── Done ─────────────────────────────────────────────────────── -->
        <v-dialog v-model="doneOpen" max-width="420" persistent>
            <v-card class="text-center pa-4">
                <v-icon size="56" color="success" class="mx-auto">mdi-check-circle</v-icon>
                <div class="text-h6 mt-2">It's yours!</div>
                <div v-if="doneOrderNumber != null" class="text-h4 font-weight-bold my-2">#{{ doneOrderNumber }}</div>
                <p class="text-body-2 text-medium-emphasis">
                    {{ doneOrderNumber != null
                        ? 'Show this number at the shop counter to pick up your bike. A confirmation is on its way to your email.'
                        : 'Payment received. Your order number is on its way to your email; show it at the shop counter.' }}
                </p>
                <v-btn color="primary" class="mt-2" to="/Shop">Done</v-btn>
            </v-card>
        </v-dialog>

        <InlineAuthDialog v-model="authDialogOpen" @authed="onAuthed" />
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
import { useRoute } from 'vue-router'
import { ShopListingService, gradeLabel, type PublicListing, type UsedOrderConflict } from '@/services/ShopListingService'
import { BikeShopService } from '@/services/BikeShopService'
import InlineAuthDialog from '@/components/InlineAuthDialog.vue'
import { absoluteUrl } from '@/helpers/ImageUrl'
import { branding } from '@/stores/branding'
import authHelper from '@/helpers/AuthHelper'
import { getStripe } from '@/helpers/StripeHelper'

const route = useRoute()
const service = new ShopListingService()
const shop = new BikeShopService()   // ConfirmIntent + order-status polling are the store's, shared.

const listingId = computed(() => String(route.params.id ?? ''))
const listing = ref<PublicListing | null>(null)
const loading = ref(true)
const gone = ref(false)
const loadError = ref('')
const isAuthed = ref(authHelper.isAuthenticated())
// The viewer's own checkout holds the bike: from the server (heldByYou on the GET, when signed in),
// or known here because they just closed the payment dialog without paying.
const heldLocally = ref(false)
const heldByYou = computed(() => listing.value?.heldByYou === true || heldLocally.value)

function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }

const gradePillClass = computed(() => {
    const g = listing.value?.conditionGrade
    return g === 'excellent' ? 'rp-pill--green' : g === 'fair' ? 'rp-pill--red' : ''
})
const specs = computed(() => {
    const l = listing.value
    if (!l) return []
    return [
        { label: 'Model', value: [l.productName, l.variantLabel].filter(Boolean).join(' ') },
        { label: 'Size', value: l.specs?.size ?? '' },
        { label: 'Color', value: l.specs?.color ?? '' },
    ].filter(s => s.value)
})

// Cover first, then the gallery, de-duplicated (the cover usually IS the first photo).
const gallery = computed(() => {
    const l = listing.value
    if (!l) return []
    const seen = new Set<string>()
    const out: { url: string; caption: string | null }[] = []
    for (const p of [{ url: l.coverUrl, caption: null as string | null }, ...(l.photos ?? [])]) {
        const abs = absoluteUrl(p.url)
        if (abs && !seen.has(abs)) { seen.add(abs); out.push({ url: abs, caption: p.caption ?? null }) }
    }
    return out
})
const index = ref(0)
const currentImage = computed(() => gallery.value[index.value]?.url ?? null)
const currentCaption = computed(() => gallery.value[index.value]?.caption ?? null)
function step(delta: number) {
    if (gallery.value.length === 0) return
    index.value = (index.value + delta + gallery.value.length) % gallery.value.length
}

async function load() {
    loading.value = true
    gone.value = false
    loadError.value = ''
    try {
        listing.value = await service.publicListing(listingId.value)
        index.value = 0
    } catch (e: any) {
        gone.value = true
        if (e.response?.status !== 404) loadError.value = e.response?.data?.error || 'Could not load this listing. Refresh to try again.'
    } finally { loading.value = false }
}

// ── Checkout ───────────────────────────────────────────────────────────────
const ordering = ref(false)
const orderError = ref('')
const payOpen = ref(false)
const paying = ref(false)
const payError = ref('')
const stripeReady = ref(false)
const pendingDue = ref(0)
const pendingSaleId = ref<string | null>(null)
const doneOpen = ref(false)
const doneOrderNumber = ref<number | null>(null)
const authDialogOpen = ref(false)
let stripe: any = null
let elements: any = null

function buy() {
    orderError.value = ''
    if (!isAuthed.value) { authDialogOpen.value = true; return }
    placeOrder()
}
async function onAuthed() {
    isAuthed.value = true
    await placeOrder()
}

async function placeOrder() {
    if (!listing.value) return
    orderError.value = ''
    ordering.value = true
    try {
        const data = await service.order(listing.value.id)
        pendingSaleId.value = data.saleId ?? null
        if (data.status === 'paid') {
            finishOrder(data.orderNumber ?? null)
        } else {
            pendingDue.value = data.dueCents ?? data.totalCents
            payOpen.value = true
            await nextTick()
            await mountPayment(data.clientSecret!)
        }
    } catch (e: any) {
        const status = e.response?.status
        const conflict = e.response?.data?.data as UsedOrderConflict | undefined
        // Their own checkout is still open (they closed the payment dialog, or reloaded): pick it
        // back up on the same PaymentIntent instead of telling them a stranger has the bike.
        if (status === 409 && conflict?.reason === 'held_by_you' && conflict.clientSecret) {
            pendingSaleId.value = conflict.saleId ?? null
            pendingDue.value = conflict.dueCents ?? conflict.totalCents ?? listing.value?.askingPriceCents ?? 0
            payOpen.value = true
            await nextTick()
            await mountPayment(conflict.clientSecret)
            return
        }
        // Their checkout exists but can't be resumed from here: the card is already processing or
        // the sale has settled. Point them at the email rather than at a retry.
        const settling = conflict?.reason === 'held_by_you'
            && (conflict.saleStatus === 'paid' || conflict.paymentStatus === 'processing' || conflict.paymentStatus === 'succeeded')
        if (status === 409 && conflict?.reason === 'held_by_other' && listing.value) listing.value.reserved = true
        orderError.value = status === 409
            ? (settling
                ? 'Your payment for this bike is already going through. Watch your email for the order number.'
                : conflict?.reason === 'held_by_you'
                    ? (e.response?.data?.error || 'You already started checking out this bike. Finish that payment, or try again in a few minutes.')
                    : conflict?.reason === 'sold'
                        ? 'This bike was just sold.'
                        : 'Someone else is checking out this bike right now; try again in a few minutes.')
            : status === 404
                ? 'This bike has just been sold or taken off the floor.'
                : status === 401
                    ? 'Please sign in to buy this bike.'
                    : (e.response?.data?.error || 'Could not start the checkout. Nothing was charged.')
        if (status === 404) await load()
        if (status === 401) { isAuthed.value = false; authDialogOpen.value = true }
    } finally { ordering.value = false }
}

async function mountPayment(clientSecret: string) {
    payError.value = ''
    stripeReady.value = false
    const account = branding.stripeChargeMode === 'direct' ? branding.stripeConnectAccountId : null
    stripe = await getStripe(branding.stripePublishableKey, account)
    if (!stripe) { payError.value = 'Payments are unavailable right now. Please try again later.'; return }
    elements = stripe.elements({ clientSecret })
    elements.create('payment').mount('#used-payment-element')
    stripeReady.value = true
}

async function confirmPay() {
    if (!stripe || !elements) return
    paying.value = true
    payError.value = ''
    try {
        const { error, paymentIntent } = await stripe.confirmPayment({ elements, redirect: 'if_required' })
        if (error) {
            payError.value = error.message || 'Payment failed. Check the card and try again.'
        } else if (paymentIntent?.status === 'succeeded') {
            try { await shop.confirmIntent(paymentIntent.id) } catch { /* webhook is the backstop */ }
            payOpen.value = false
            finishOrder(await pollOrderNumber(pendingSaleId.value))
        } else {
            payError.value = 'The payment has not settled yet. It will complete shortly; watch your email for the order number.'
        }
    } catch (e: any) {
        payError.value = e?.message || 'Payment failed. Please try again.'
    } finally { paying.value = false }
}

// Up to ~8s: the webhook usually settles in well under a second; null falls back to the email copy.
async function pollOrderNumber(saleId: string | null): Promise<number | null> {
    if (!saleId) return null
    for (let i = 0; i < 8; i++) {
        try {
            const o = (await shop.storeOrderStatus(saleId)).data.data
            if (o.status === 'paid' && o.orderNumber != null) return o.orderNumber
        } catch { /* keep polling */ }
        await new Promise(res => setTimeout(res, 1000))
    }
    return null
}

// Closing the dialog doesn't cancel anything: the bike stays held for them until the hold lapses,
// so the page switches to "Finish paying" and the next click resumes the same checkout.
function closePay() {
    payOpen.value = false
    if (pendingSaleId.value && !doneOpen.value) heldLocally.value = true
}

function finishOrder(orderNumber: number | null) {
    doneOrderNumber.value = orderNumber
    doneOpen.value = true
    payOpen.value = false
    heldLocally.value = false
}

onMounted(load)
</script>

<style scoped>
.ul-page { max-width: 1100px; }
.ul-card {
    border: 1px solid var(--rp-line);
    background: var(--rp-card);
    box-shadow: none;
}
.ul-hero { background: var(--rp-card2); }
.ul-noimage {
    aspect-ratio: 4 / 3;
    background: var(--rp-card2);
    border: 1px solid var(--rp-line);
}
.ul-thumbs { overflow-x: auto; }
.ul-thumb {
    width: 64px;
    flex: 0 0 64px;
    cursor: pointer;
    opacity: 0.6;
    border: 2px solid transparent;
}
.ul-thumb--active {
    opacity: 1;
    border-color: rgb(var(--v-theme-primary));
}
</style>
