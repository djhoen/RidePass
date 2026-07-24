<template>
    <v-container>
        <div class="d-flex align-center mb-4 ga-3 flex-wrap">
            <h1 class="text-h4">Shop</h1>
            <v-spacer></v-spacer>
            <v-btn v-if="cartCount > 0" color="primary" prepend-icon="mdi-cart" @click="cartOpen = true">
                Cart ({{ cartCount }}) · {{ money(cartTotal) }}
            </v-btn>
        </div>

        <v-card v-if="loading" class="pa-8 text-center"><v-progress-circular indeterminate color="primary" /></v-card>
        <v-alert v-else-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>
        <v-card v-else-if="catalog.products.length === 0" class="pa-8 text-center text-medium-emphasis">
            The shop shelves are being stocked. Check back soon!
        </v-card>
        <template v-else>
            <v-chip-group v-model="categoryFilter" class="mb-3">
                <v-chip v-for="c in visibleCategories" :key="c.id" :value="c.id" filter variant="tonal">{{ c.name }}</v-chip>
            </v-chip-group>

            <!-- Embedded widget: the detail REPLACES the grid (see isEmbed in the script). -->
            <v-card v-if="isEmbed && detailProduct" variant="outlined" class="mb-4">
                <ProductDetail :product="detailProduct" @add="addFromDetail" @close="detailProduct = null" />
            </v-card>

            <v-row v-if="!(isEmbed && detailProduct)">
                <v-col v-for="p in filteredProducts" :key="p.id" cols="12" sm="6" md="4" lg="3">
                    <v-card class="d-flex flex-column shop-card" height="100%" @click="openDetail(p)">
                        <v-img v-if="p.imageUrl" :src="absoluteUrl(p.imageUrl)!" height="160" cover></v-img>
                        <v-card-text class="flex-grow-1">
                            <div class="text-caption text-medium-emphasis">{{ p.brand || '' }}</div>
                            <div class="font-weight-medium">{{ p.name }}</div>
                            <div class="text-primary font-weight-bold mt-1">{{ priceRange(p) }}</div>
                            <div v-if="p.description" class="text-caption text-medium-emphasis mt-1 shop-desc">{{ p.description }}</div>
                        </v-card-text>
                        <v-card-actions>
                            <template v-if="isSerialized(p)">
                                <span class="text-caption text-medium-emphasis px-2">Available in store: ask our staff.</span>
                            </template>
                            <template v-else>
                                <v-select v-if="p.variants.length > 1" v-model="picked[p.id]" :items="variantItems(p)"
                                    item-title="title" item-value="id" density="compact" hide-details
                                    label="Option" class="mr-2" style="max-width: 170px"
                                    @click.stop></v-select>
                                <v-spacer></v-spacer>
                                <v-btn color="primary" variant="tonal" size="small" prepend-icon="mdi-cart-plus"
                                    :disabled="!canAdd(p)" @click.stop="addToCart(p)">
                                    {{ canAdd(p) ? 'Add' : 'Out of stock' }}
                                </v-btn>
                            </template>
                        </v-card-actions>
                    </v-card>
                </v-col>
            </v-row>
        </template>

        <!-- ── Cart / checkout ──────────────────────────────────────────── -->
        <v-dialog v-model="cartOpen" max-width="520">
            <v-card class="d-flex flex-column" style="max-height: 90vh">
                <v-card-title class="d-flex align-center" style="flex: 0 0 auto">
                    <span>Your cart</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="ordering" @click="cartOpen = false"></v-btn>
                </v-card-title>
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                    <div v-for="(l, i) in cart" :key="l.variantId" class="d-flex align-center ga-2 py-1">
                        <div class="flex-grow-1 text-body-2">
                            {{ l.name }}<span v-if="l.label" class="text-medium-emphasis"> ({{ l.label }})</span>
                        </div>
                        <v-btn icon="mdi-minus" size="x-small" variant="text" @click="changeQty(i, -1)"></v-btn>
                        <span>{{ l.qty }}</span>
                        <v-btn icon="mdi-plus" size="x-small" variant="text" :disabled="l.qty >= l.available" @click="changeQty(i, 1)"></v-btn>
                        <span class="text-body-2" style="min-width: 70px; text-align: right">{{ money(l.priceCents * l.qty) }}</span>
                        <v-btn icon="mdi-close" size="x-small" variant="text" @click="cart.splice(i, 1)"></v-btn>
                    </div>
                    <v-divider class="my-3"></v-divider>
                    <div class="d-flex justify-space-between text-body-2 text-medium-emphasis">
                        <span>Subtotal (tax added at checkout)</span><span>{{ money(cartTotal) }}</span>
                    </div>

                    <template v-if="isAuthed">
                        <v-text-field v-model="couponCode" label="Promo code (optional)" density="compact"
                            hide-details class="mt-4"></v-text-field>
                        <v-checkbox v-if="myCreditCents > 0" v-model="useCredit" color="primary" density="compact"
                            hide-details class="mt-1"
                            :label="`Use my store credit (${money(myCreditCents)} available)`"></v-checkbox>
                        <p class="text-caption text-medium-emphasis mt-3 mb-0">
                            Pay now, pick up at the shop counter. You'll get an order number by email.
                        </p>
                    </template>
                    <v-alert v-else type="info" variant="tonal" density="compact" class="mt-4">
                        Sign in to check out. Your cart will be waiting.
                    </v-alert>
                    <div v-if="orderError" class="text-error text-body-2 mt-2">{{ orderError }}</div>
                </v-card-text>
                <v-card-actions style="flex: 0 0 auto">
                    <v-spacer></v-spacer>
                    <v-btn :disabled="ordering" @click="cartOpen = false">Keep shopping</v-btn>
                    <v-btn v-if="!isAuthed" color="primary" @click="goLogin">Sign in</v-btn>
                    <v-btn v-else color="primary" :loading="ordering" :disabled="cart.length === 0" @click="placeOrder">
                        Check out
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- ── Card payment ─────────────────────────────────────────────── -->
        <v-dialog v-model="payOpen" persistent max-width="480">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Payment</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="paying" @click="payOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div class="text-h6 mb-3">{{ money(pendingDue) }}</div>
                    <div id="store-payment-element" class="mb-4"></div>
                    <div v-if="payError" class="text-error text-body-2 mb-2">{{ payError }}</div>
                    <v-btn block color="primary" size="large" :loading="paying" :disabled="!stripeReady" @click="confirmPay">
                        Pay {{ money(pendingDue) }}
                    </v-btn>
                </v-card-text>
            </v-card>
        </v-dialog>

        <!-- ── Done ─────────────────────────────────────────────────────── -->
        <v-dialog v-model="doneOpen" max-width="420">
            <v-card class="text-center pa-4">
                <v-icon size="56" color="success" class="mx-auto">mdi-check-circle</v-icon>
                <div class="text-h6 mt-2">Order placed!</div>
                <div v-if="doneOrderNumber != null" class="text-h4 font-weight-bold my-2">#{{ doneOrderNumber }}</div>
                <p class="text-body-2 text-medium-emphasis">
                    {{ doneOrderNumber != null
                        ? 'Show this number at the shop counter to pick up your order. A confirmation is on its way to your email.'
                        : 'Payment received. Your order number is on its way to your email; show it at the shop counter.' }}
                </p>
                <v-btn color="primary" class="mt-2" @click="doneOpen = false">Done</v-btn>
            </v-card>
        </v-dialog>

        <!-- Hosted site: the detail opens as a modal. -->
        <v-dialog v-if="!isEmbed" v-model="detailOpen" max-width="900" scrollable>
            <v-card v-if="detailProduct">
                <ProductDetail :product="detailProduct" @add="addFromDetail" @close="detailProduct = null" />
            </v-card>
        </v-dialog>

        <InlineAuthDialog v-model="authDialogOpen" @authed="onAuthed" />
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
import { useRoute } from 'vue-router'
import { BikeShopService, type StoreCatalog, type StoreCatalogProduct, type StoreCatalogVariant } from '@/services/BikeShopService'
import InlineAuthDialog from '@/components/InlineAuthDialog.vue'
import ProductDetail from '@/components/bikeshop/ProductDetail.vue'
import { absoluteUrl } from '@/helpers/ImageUrl'
import { CreditService } from '@/services/CreditService'
import { branding } from '@/stores/branding'
import authHelper from '@/helpers/AuthHelper'
import { getStripe } from '@/helpers/StripeHelper'

type CatalogProduct = StoreCatalogProduct

const service = new BikeShopService()
const route = useRoute()

// Embedded widget: the iframe is sized to its content and cannot scroll itself, so a
// fixed-position dialog would center against the full iframe height and land off the
// visitor's screen. Show the detail in place of the grid instead.
const isEmbed = computed(() => !!route.meta.embed)

const catalog = ref<StoreCatalog>({ categories: [], products: [] })
const loading = ref(true)
const loadError = ref('')
const categoryFilter = ref<string | null>(null)
const picked = ref<Record<string, string>>({})
const isAuthed = ref(authHelper.isAuthenticated())

function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }

const visibleCategories = computed(() => {
    const used = new Set(catalog.value.products.map(p => p.categoryId))
    return catalog.value.categories.filter(c => used.has(c.id)).sort((a, b) => a.sortOrder - b.sortOrder)
})
const filteredProducts = computed(() =>
    catalog.value.products
        .filter(p => !categoryFilter.value || p.categoryId === categoryFilter.value)
        .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name)))

function isSerialized(p: CatalogProduct): boolean {
    return p.variants.every(v => v.trackingKind === 'serialized')
}
function priceRange(p: CatalogProduct): string {
    const prices = p.variants.map(v => v.salePriceCents)
    const min = Math.min(...prices)
    const max = Math.max(...prices)
    return min === max ? money(min) : `${money(min)} to ${money(max)}`
}
function variantItems(p: CatalogProduct) {
    return p.variants.filter(v => v.trackingKind === 'pool').map(v => ({
        id: v.id,
        title: `${[v.size, v.color].filter(Boolean).join(' / ') || 'Standard'}${v.available <= 0 ? ' (out)' : ''}`,
    }))
}
function pickedVariant(p: CatalogProduct) {
    const pool = p.variants.filter(v => v.trackingKind === 'pool')
    if (pool.length === 0) return null
    return pool.find(v => v.id === picked.value[p.id]) ?? (pool.length === 1 ? pool[0] : null)
}
function canAdd(p: CatalogProduct): boolean {
    const v = pickedVariant(p)
    return v != null && v.available > 0
}

// ── Cart ───────────────────────────────────────────────────────────────────
const cart = ref<{ variantId: string; name: string; label: string; priceCents: number; qty: number; available: number }[]>([])
const cartOpen = ref(false)
const cartCount = computed(() => cart.value.reduce((s, l) => s + l.qty, 0))
const cartTotal = computed(() => cart.value.reduce((s, l) => s + l.priceCents * l.qty, 0))

// One cart-add path for both the card's quick Add and the detail view's quantity stepper,
// so the two can never drift on clamping or de-duplication.
function addLine(p: CatalogProduct, v: StoreCatalogVariant | null, qty: number) {
    if (!v || qty < 1) return
    const existing = cart.value.find(l => l.variantId === v.id)
    if (existing) {
        existing.qty = Math.min(existing.qty + qty, v.available)
    } else {
        cart.value.push({
            variantId: v.id,
            name: p.name,
            label: [v.size, v.color].filter(Boolean).join(' / '),
            priceCents: v.salePriceCents,
            qty: Math.min(qty, v.available),
            available: v.available,
        })
    }
    cartOpen.value = true
}
function addToCart(p: CatalogProduct) {
    addLine(p, pickedVariant(p), 1)
}

// ── Product detail ─────────────────────────────────────────────────────────
const detailProduct = ref<CatalogProduct | null>(null)
const detailOpen = computed({
    get: () => detailProduct.value !== null,
    set: (v: boolean) => { if (!v) detailProduct.value = null },
})
function openDetail(p: CatalogProduct) { detailProduct.value = p }
function addFromDetail(payload: { variantId: string; qty: number }) {
    const p = detailProduct.value
    if (!p) return
    addLine(p, p.variants.find(v => v.id === payload.variantId) ?? null, payload.qty)
    detailProduct.value = null
}
function changeQty(i: number, delta: number) {
    const l = cart.value[i]
    l.qty += delta
    if (l.qty <= 0) cart.value.splice(i, 1)
}

// ── Checkout ───────────────────────────────────────────────────────────────
const couponCode = ref('')
const useCredit = ref(false)
const myCreditCents = ref(0)
const ordering = ref(false)
const orderError = ref('')
const payOpen = ref(false)
const paying = ref(false)
const payError = ref('')
const stripeReady = ref(false)
const pendingDue = ref(0)
// Kept from the order response so the card path can look the order number up after the
// payment settles, and can decrement the displayed credit balance by what was actually used.
const pendingSaleId = ref<string | null>(null)
const pendingCredit = ref(0)
const doneOpen = ref(false)
const doneOrderNumber = ref<number | null>(null)
let stripe: any = null
let elements: any = null

function goLogin() {
    // Inline sign in / sign up: no navigation, so the cart survives and the flow
    // works identically inside the embedded storefront widget.
    authDialogOpen.value = true
}

const authDialogOpen = ref(false)
async function onAuthed() {
    isAuthed.value = true
    // Mirror the authed part of mount: the credit offer only shows for accounts.
    try { myCreditCents.value = (await new CreditService().mine()).data.data.balanceCents }
    catch { /* offer stays hidden */ }
}

async function placeOrder() {
    orderError.value = ''
    ordering.value = true
    try {
        const r = await service.storeOrder({
            lines: cart.value.map(l => ({ variantId: l.variantId, quantity: l.qty })),
            couponCode: couponCode.value.trim() || null,
            creditCents: useCredit.value ? myCreditCents.value : 0,
        })
        const data = r.data.data
        pendingSaleId.value = data.saleId ?? null
        pendingCredit.value = data.creditAppliedCents ?? 0
        if (data.status === 'paid') {
            // Store credit covered the whole order.
            finishOrder(data.orderNumber ?? null, data.creditAppliedCents ?? 0)
        } else {
            pendingDue.value = data.dueCents ?? data.totalCents
            cartOpen.value = false
            payOpen.value = true
            await nextTick()
            await mountPayment(data.clientSecret!)
        }
    } catch (e: any) {
        orderError.value = e.response?.data?.error || 'Could not place the order. Nothing was charged.'
    } finally { ordering.value = false }
}

async function mountPayment(clientSecret: string) {
    payError.value = ''
    stripeReady.value = false
    const account = branding.stripeChargeMode === 'direct' ? branding.stripeConnectAccountId : null
    stripe = await getStripe(branding.stripePublishableKey, account)
    if (!stripe) { payError.value = 'Payments are unavailable right now. Please try again later.'; return }
    elements = stripe.elements({ clientSecret })
    elements.create('payment').mount('#store-payment-element')
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
            try { await service.confirmIntent(paymentIntent.id) } catch { /* webhook is the backstop */ }
            payOpen.value = false
            // The order number is assigned when the sale is finalized, which may land a beat
            // after the card confirms. Poll briefly so the customer sees the number they are
            // told to show at the counter, instead of only getting it by email.
            const num = await pollOrderNumber(pendingSaleId.value)
            finishOrder(num, pendingCredit.value)
        } else {
            payError.value = 'The payment has not settled yet. It will complete shortly; watch your email for the order number.'
        }
    } catch (e: any) {
        payError.value = e?.message || 'Payment failed. Please try again.'
    } finally { paying.value = false }
}

// Up to ~8s of polling: the webhook usually settles in well under a second, and a null
// return simply falls back to the "watch your email" copy in the success dialog.
async function pollOrderNumber(saleId: string | null): Promise<number | null> {
    if (!saleId) return null
    for (let i = 0; i < 8; i++) {
        try {
            const r = await service.storeOrderStatus(saleId)
            const o = r.data.data
            if (o.status === 'paid' && o.orderNumber != null) return o.orderNumber
        } catch { /* keep polling; the email carries the number regardless */ }
        await new Promise(res => setTimeout(res, 1000))
    }
    return null
}

function finishOrder(orderNumber: number | null, creditUsed: number) {
    doneOrderNumber.value = orderNumber
    doneOpen.value = true
    cartOpen.value = false
    cart.value = []
    couponCode.value = ''
    if (creditUsed > 0) myCreditCents.value = Math.max(0, myCreditCents.value - creditUsed)
    useCredit.value = false
    reload()   // refresh availability
}

async function reload() {
    try {
        catalog.value = (await service.storeCatalog()).data.data
    } catch (e: any) {
        loadError.value = e.response?.data?.error || 'Could not load the shop. Refresh to try again.'
    } finally { loading.value = false }
}

onMounted(async () => {
    await reload()
    if (isAuthed.value) {
        // Best-effort: no balance just means the credit offer doesn't show.
        try { myCreditCents.value = (await new CreditService().mine()).data.data.balanceCents }
        catch { /* offer stays hidden */ }
    }
})
</script>

<style scoped>
.shop-desc {
    display: -webkit-box;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    overflow: hidden;
}
/* The whole card opens the detail view; the option select and Add button stop the click. */
.shop-card { cursor: pointer; }
</style>
