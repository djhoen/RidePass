<template>
    <v-container fluid class="pos">
        <div class="d-flex align-center mb-3 ga-3 flex-wrap">
            <v-btn variant="text" prepend-icon="mdi-arrow-left" to="/Admin/BikeShop">Bike Shop</v-btn>
            <h1 class="text-h5">Register</h1>
            <v-spacer></v-spacer>
            <v-chip size="small" variant="tonal" :color="readerColor" prepend-icon="mdi-contactless-payment">
                {{ readerStatus }}
            </v-chip>
            <v-btn v-if="readerState !== 'connected'" size="small" variant="text"
                :loading="readerConnecting" @click="connectReader">Connect reader</v-btn>
            <v-btn size="small" variant="text" prepend-icon="mdi-printer-settings" @click="printerDialog = true">Printer</v-btn>
            <ShopDisplayButton />
        </div>

        <v-row>
            <!-- ── Catalog ─────────────────────────────────────────────── -->
            <v-col cols="12" md="7">
                <v-text-field v-model="search" placeholder="Search, or scan a barcode and press Enter…" density="compact"
                    prepend-inner-icon="mdi-barcode-scan" clearable hide-details class="mb-3"
                    @keydown.enter.prevent="scanEnter"></v-text-field>

                <div v-if="loading" class="text-center py-8">
                    <v-progress-circular indeterminate color="primary"></v-progress-circular>
                </div>
                <v-alert v-else-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>
                <v-card v-else-if="sellableVariants.length === 0" class="pa-6 text-center text-medium-emphasis">
                    Nothing set up for sale yet. Add products with a sale price in the Bike Shop admin.
                </v-card>
                <div v-else class="pos-grid">
                    <v-card v-for="v in filteredVariants" :key="v.key" class="pos-tile" variant="outlined"
                        :disabled="v.availableCount <= 0" @click="addVariant(v)">
                        <div class="font-weight-medium text-truncate">{{ v.productName }}</div>
                        <div v-if="v.label" class="text-caption text-medium-emphasis text-truncate">{{ v.label }}</div>
                        <div class="d-flex align-center justify-space-between mt-1">
                            <span class="font-weight-bold">{{ money(v.salePriceCents!) }}</span>
                            <span class="text-caption" :class="v.availableCount <= 0 ? 'text-error' : 'text-medium-emphasis'">
                                {{ v.availableCount <= 0 ? 'Out' : v.availableCount + ' left' }}
                            </span>
                        </div>
                    </v-card>
                </div>
            </v-col>

            <!-- ── Cart ────────────────────────────────────────────────── -->
            <v-col cols="12" md="5">
                <v-card>
                    <v-card-text>
                        <div class="text-h6 mb-2">Cart</div>
                        <div v-if="cart.length === 0" class="text-medium-emphasis text-center py-6">
                            Tap a product to add it.
                        </div>
                        <div v-for="(line, i) in cart" :key="i" class="d-flex align-center ga-2 py-1">
                            <div class="flex-grow-1">
                                <div class="text-body-2">{{ line.productName }}<span v-if="line.label" class="text-medium-emphasis"> · {{ line.label }}</span></div>
                                <div v-if="line.itemLabel" class="text-caption text-medium-emphasis">{{ line.itemLabel }}</div>
                            </div>
                            <template v-if="!line.itemId">
                                <v-btn icon="mdi-minus" size="x-small" variant="tonal" @click="bump(i, -1)"></v-btn>
                                <span class="pos-qty">{{ line.qty }}</span>
                                <v-btn icon="mdi-plus" size="x-small" variant="tonal" :disabled="line.qty >= line.availableCount" @click="bump(i, 1)"></v-btn>
                            </template>
                            <span v-else class="pos-qty">1</span>
                            <span class="text-body-2" style="min-width:64px; text-align:right">{{ money(line.salePriceCents * line.qty) }}</span>
                            <v-btn icon="mdi-close" size="x-small" variant="text" @click="cart.splice(i, 1)"></v-btn>
                        </div>

                        <v-divider class="my-3"></v-divider>
                        <div class="d-flex justify-space-between text-body-2 text-medium-emphasis">
                            <span>Subtotal</span><span>{{ money(subtotal) }}</span>
                        </div>
                        <div class="text-caption text-medium-emphasis mb-3">Tax is added at checkout.</div>

                        <v-text-field v-model="buyerName" label="Customer name (optional)" density="compact" class="mt-4" hide-details></v-text-field>
                        <v-text-field v-model="buyerEmail" type="email" label="Member email (auto-applies pass perks) / receipt" density="compact" class="mt-4" hide-details></v-text-field>
                        <v-row dense class="mt-2">
                            <v-col cols="12"><v-text-field v-model="couponCode" label="Promo code" density="compact" hide-details></v-text-field></v-col>
                        </v-row>
                        <!-- Staff-applied discount, distinct from the promo code above: the customer
                             brings a code, the cashier chooses one of these. Some need a manager PIN. -->
                        <v-select v-if="discounts.length" v-model="discountPresetId" :items="discountItems"
                            label="Discount (optional)" density="compact" variant="outlined" hide-details
                            clearable class="mt-4"></v-select>
                        <v-text-field v-if="selectedDiscountNeedsPin" v-model="managerPin" type="password"
                            label="Manager PIN" density="compact" variant="outlined" hide-details class="mt-4"
                            prepend-inner-icon="mdi-shield-key"
                            hint="This discount needs a manager's approval." persistent-hint></v-text-field>
                        <v-text-field v-model="giftCardCode" label="Gift card code" density="compact" hide-details
                            class="mt-4" prepend-inner-icon="mdi-gift"></v-text-field>

                        <CreditLookupField v-model="creditAccount" class="mt-4" />

                        <div class="d-flex ga-2 mt-4">
                            <v-btn color="secondary" size="large" class="flex-grow-1" :disabled="cart.length === 0 || busy"
                                :loading="busy && payMethod === 'cash'" @click="checkout('cash')">Cash</v-btn>
                            <v-btn color="primary" size="large" class="flex-grow-1" :disabled="cart.length === 0 || busy"
                                :loading="busy && payMethod === 'card'" @click="checkout('card')">Card</v-btn>
                        </div>
                        <div v-if="checkoutError" class="text-error text-body-2 mt-2">{{ checkoutError }}</div>
                    </v-card-text>
                </v-card>
            </v-col>
        </v-row>

        <!-- ── Serialized unit picker ──────────────────────────────────── -->
        <v-dialog v-model="unitPickerOpen" max-width="440">
            <v-card v-if="pickerVariant">
                <v-card-title class="d-flex align-center">
                    <span>Choose a unit — {{ pickerVariant.productName }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="unitPickerOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div v-if="pickerLoading" class="text-center py-4"><v-progress-circular indeterminate></v-progress-circular></div>
                    <v-list v-else lines="one">
                        <v-list-item v-for="it in pickerItems" :key="it.id" :title="it.label"
                            :subtitle="it.serial || undefined" @click="addSerialized(it)">
                            <template #append><v-icon>mdi-plus</v-icon></template>
                        </v-list-item>
                        <div v-if="pickerItems.length === 0" class="text-medium-emphasis text-center py-3">No available units.</div>
                    </v-list>
                </v-card-text>
            </v-card>
        </v-dialog>

        <!-- ── Card payment ────────────────────────────────────────────── -->
        <v-dialog v-model="payOpen" persistent max-width="480">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Card payment</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="paying" @click="cancelCard"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div class="text-h6 mb-3">{{ money(pendingTotal) }}</div>
                    <div id="shop-payment-element" class="mb-4"></div>
                    <div v-if="payError" class="text-error text-body-2 mb-2">{{ payError }}</div>
                    <v-btn block color="primary" size="large" :loading="paying" :disabled="!stripeReady" @click="payCard">
                        Charge {{ money(pendingTotal) }}
                    </v-btn>
                </v-card-text>
            </v-card>
        </v-dialog>

        <!-- ── Sale complete ───────────────────────────────────────────── -->
        <v-dialog v-model="doneOpen" max-width="380">
            <v-card class="text-center pa-4">
                <v-icon color="success" size="56">mdi-check-circle</v-icon>
                <div class="text-h6 mt-2">Sale complete</div>
                <div v-if="lastOrderNumber != null" class="text-body-1 mt-1">Sale #{{ lastOrderNumber }}</div>
                <div class="text-body-2 text-medium-emphasis mt-1">{{ money(lastTotal) }} · {{ lastMethod }}</div>
                <v-btn v-if="printerUrl" variant="tonal" class="mt-4" prepend-icon="mdi-printer"
                    :disabled="!lastReceipt" @click="printLast">Print receipt</v-btn>
                <v-btn color="primary" class="mt-4" @click="newSale">New sale</v-btn>
            </v-card>
        </v-dialog>

        <!-- ── Receipt printer setup (per tablet, shared with the F&B POS default) ── -->
        <v-dialog v-model="printerDialog" max-width="440">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Receipt printer</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="printerDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div class="text-body-2 text-medium-emphasis mb-3">
                        Enter this tablet's Epson receipt printer address (e.g. <code>https://192.168.1.50</code>).
                        Receipts then print automatically after each sale. The printer must be reachable over HTTPS;
                        leave blank to disable printing.
                    </div>
                    <v-text-field v-model="printerUrl" label="Printer URL" placeholder="https://192.168.1.50"
                        density="compact" hide-details></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-btn v-if="printerUrl" variant="text" size="small" :disabled="!lastReceipt" @click="printLast">Test print last</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="printerDialog = false">Cancel</v-btn>
                    <v-btn color="primary" variant="text" @click="savePrinter">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- ── Scanned a barcode the shared parts library recognises but this shop doesn't stock ──
             The value here is that the cashier learns WHAT the thing is instead of "no match".
             Everything shown is identity: no price, because the library holds none by design. -->
        <v-dialog :model-value="!!unknownPart" max-width="480" @update:model-value="unknownPart = null">
            <v-card v-if="unknownPart?.suggestion">
                <v-card-title class="d-flex align-center">
                    <span>Not in your catalog</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="unknownPart = null"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 text-medium-emphasis mb-3">
                        You scanned <strong>{{ unknownPart.scanned }}</strong>. Your shop doesn't carry
                        it, but other shops on RidePass have identified it:
                    </p>

                    <div class="text-h6">{{ unknownPart.suggestion.name }}</div>
                    <div v-if="unknownPart.suggestion.brand" class="text-body-2">
                        {{ unknownPart.suggestion.brand }}
                    </div>
                    <div v-if="unknownPart.suggestion.mpn" class="text-caption text-medium-emphasis">
                        Part number {{ unknownPart.suggestion.mpn }}
                    </div>
                    <div v-if="unknownPart.suggestion.categoryHint" class="text-caption text-medium-emphasis">
                        Usually filed under {{ unknownPart.suggestion.categoryHint }}
                    </div>

                    <v-alert type="info" variant="tonal" density="compact" class="mt-4">
                        {{ suggestionConfidence }}
                    </v-alert>

                    <p class="text-caption text-medium-emphasis mt-3">
                        No price is shown because what a part sells for is each shop's own decision.
                        Add it to your catalog to set yours.
                    </p>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="unknownPart = null">Close</v-btn>
                    <!-- Adding a product is a catalog write, so a cashier without catalog rights
                         gets the identification but not the button. The server enforces this too. -->
                    <v-btn v-if="canManageCatalog" color="primary" variant="text"
                        @click="addSuggestedToCatalog">Add to catalog</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackColor" :timeout="3500">{{ snackText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { BikeShopService, type ShopProduct, type ShopVariant, type ShopItem, type ShopScanResolution, type ShopDisplayState } from '@/services/BikeShopService'
import ShopDisplayButton from '@/components/bikeshop/ShopDisplayButton.vue'
import { shopDisplayPaired, pushShopDisplayState, idleShopDisplayState } from '@/helpers/ShopDisplay'
import { useRouter } from 'vue-router'
import authHelper from '@/helpers/AuthHelper'
import { Perm } from '@/helpers/TenantPermissions'
import { type CreditLookupResult } from '@/services/CreditService'
import CreditLookupField from '@/components/CreditLookupField.vue'
import { branding } from '@/stores/branding'
import { DiscountService, type DiscountPreset } from '@/services/DiscountService'
import { getStripe } from '@/helpers/StripeHelper'
import { getTerminal, discoverAndConnect, collectAndProcess } from '@/helpers/TerminalHelper'
import { printReceipt, type Receipt } from '@/helpers/ReceiptPrinter'

const service = new BikeShopService()

interface Tile {
    key: string
    variantId: string
    productName: string
    label: string
    salePriceCents: number | null
    availableCount: number
    trackingKind: 'pool' | 'serialized'
    sku: string | null
    barcode: string | null
}
interface CartLine {
    variantId: string
    productName: string
    label: string
    salePriceCents: number
    qty: number
    availableCount: number
    trackingKind: 'pool' | 'serialized'
    itemId?: string
    itemLabel?: string
}

const loading = ref(false)
const loadError = ref('')
const products = ref<ShopProduct[]>([])
const search = ref('')
const cart = ref<CartLine[]>([])
const buyerName = ref('')
const buyerEmail = ref('')
const couponCode = ref('')
const giftCardCode = ref('')

// Tenant discounts scoped to this counter. Fetched server-side already filtered, so the list can
// never offer one the sale would then refuse.
const discountService = new DiscountService()
const discounts = ref<DiscountPreset[]>([])
const discountPresetId = ref<string | null>(null)
const managerPin = ref('')
const discountItems = computed(() => discounts.value.map(d => ({
    // The lock tells the cashier a PIN is coming before they pick it, not after.
    title: `${d.name} · ${d.label}${d.requiresManager ? '  (needs manager)' : ''}`,
    value: d.id,
})))
const selectedDiscountNeedsPin = computed(() =>
    !!discounts.value.find(d => d.id === discountPresetId.value)?.requiresManager)

// ── Card reader (WisePOS E, same SDK flow as the F&B POS) ──────────────────
const readerState = ref<'idle' | 'connecting' | 'connected' | 'error'>('idle')
const readerConnecting = ref(false)
const readerLabel = ref('')
const readerStatus = computed(() =>
    readerState.value === 'connected' ? `Reader: ${readerLabel.value}`
    : readerState.value === 'connecting' ? 'Connecting reader…'
    : readerState.value === 'error' ? 'Reader not connected' : 'No reader')
const readerColor = computed(() =>
    readerState.value === 'connected' ? 'success' : readerState.value === 'error' ? 'error' : 'grey')

async function fetchTerminalToken(): Promise<string> {
    const r = await service.shopTerminalToken()
    return (r.data as any).data.secret
}
async function connectReader() {
    readerConnecting.value = true
    readerState.value = 'connecting'
    try {
        const terminal = await getTerminal(fetchTerminalToken)
        if (!terminal) throw new Error('Card reader SDK unavailable.')
        // Simulated reader against Stripe test keys; the real WisePOS E in production.
        readerLabel.value = await discoverAndConnect(terminal, import.meta.env.MODE !== 'production')
        readerState.value = 'connected'
    } catch {
        readerState.value = 'error'
    } finally { readerConnecting.value = false }
}

const busy = ref(false)
const payMethod = ref<'cash' | 'card' | null>(null)
const checkoutError = ref('')

const snackbar = ref(false); const snackText = ref(''); const snackColor = ref<'success' | 'error'>('success')
function flash(t: string, c: 'success' | 'error' = 'success') { snackText.value = t; snackColor.value = c; snackbar.value = true }

function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }
function variantLabel(v: ShopVariant): string { return [v.size, v.color, v.gender].filter(Boolean).join(' / ') }

// Flatten to sellable variants (active product + active variant with a price).
const sellableVariants = computed<Tile[]>(() =>
    products.value.filter(p => p.isActive && p.isSellable).flatMap(p =>
        p.variants.filter(v => v.isActive && v.salePriceCents != null).map(v => ({
            key: v.id,
            variantId: v.id,
            productName: p.name,
            label: variantLabel(v),
            salePriceCents: v.salePriceCents,
            availableCount: v.availableCount,
            trackingKind: v.trackingKind,
            sku: v.sku,
            barcode: v.barcode,
        }))))

// Scanner input: an Enter in the search box resolves the code SERVER-side and adds the match to
// the cart, so a USB scanner just works.
//
// Resolved on the server rather than by filtering the loaded catalog, because the old client-side
// match had two failure modes a shop would hit daily: it could only find products already
// downloaded into this browser, and it compared the code as a raw string, so a part stored as a
// 12-digit UPC-A did not match when the scanner emitted the 13-digit EAN form. The server
// normalises both to a GTIN-14 first.
const scanning = ref(false)
const router = useRouter()

// ── Scanned something the shared library knows but this shop doesn't stock ────────────────
// Held rather than flashed: it carries a name the cashier may want to read out, and on a shop
// with catalog rights it is the start of adding the product.
const unknownPart = ref<Extract<ShopScanResolution, { found: false }> | null>(null)
const canManageCatalog = computed(() => authHelper.hasPermission(Perm.CatalogManage))

// Hands the identity to the catalog page, which opens its new-product dialog already filled in.
// Only identity travels: the shop still sets its own price, category and stock. The name is the
// MANUFACTURER'S, used as a starting point for the shop's own product name, which they can change
// freely afterwards without that ever being visible to another track.
function addSuggestedToCatalog() {
    const s = unknownPart.value?.suggestion
    if (!s) return
    const gtin = unknownPart.value?.gtin14 ?? unknownPart.value?.scanned ?? ''
    unknownPart.value = null
    router.push({
        path: '/Admin/BikeShop',
        query: { newName: s.name, newBrand: s.brand ?? undefined, newBarcode: gtin || undefined },
    })
}

// "1 shop" is honest about being one shop's word; nine shops agreeing is worth saying out loud.
const suggestionConfidence = computed(() => {
    const n = unknownPart.value?.suggestion?.timesConfirmed ?? 0
    if (n <= 1) return 'Identified by 1 shop on RidePass. Worth a second look before you trust it.'
    return `Independently confirmed by ${n} shops on RidePass.`
})
async function scanEnter() {
    const code = search.value?.trim()
    if (!code || scanning.value) return
    scanning.value = true
    try {
        const r = await service.resolveScan(code)
        const res = r.data.data
        if (!res.found) {
            // The shared parts library may still know what this barcode IS even though this shop
            // has never carried it. Saying "this is a Bontrager tube, you just don't stock it" is a
            // different and far more actionable message than "no match", so it gets its own panel
            // rather than a snackbar that vanishes while the cashier is still reading it.
            if (res.suggestion) { unknownPart.value = res; return }
            flash(res.isValidBarcode
                ? `Barcode ${res.scanned} isn't in your catalog yet. Add it as a product first.`
                : `Nothing matches "${res.scanned}". Check the code, or search by name.`, 'error')
            return
        }
        if (res.salePriceCents == null) {
            flash(`${res.productName} has no sale price set, so it can't be rung up.`, 'error')
            return
        }
        if (res.availableCount <= 0) {
            flash(`${res.productName} is out of stock.`, 'error')
            return
        }
        addVariant({
            key: res.variantId,
            variantId: res.variantId,
            productName: res.productName,
            label: [res.size, res.color].filter(Boolean).join(' / '),
            salePriceCents: res.salePriceCents,
            availableCount: res.availableCount,
            trackingKind: res.trackingKind,
            sku: res.sku,
            barcode: res.barcode,
        })
        search.value = ''
    } catch (e: any) {
        flash(e.response?.data?.error
            || 'Could not look that code up. Check the connection and scan again.', 'error')
    } finally {
        scanning.value = false
    }
}

const filteredVariants = computed(() => {
    const q = search.value.trim().toLowerCase()
    if (!q) return sellableVariants.value
    return sellableVariants.value.filter(v =>
        v.productName.toLowerCase().includes(q) || v.label.toLowerCase().includes(q))
})

const subtotal = computed(() => cart.value.reduce((s, l) => s + l.salePriceCents * l.qty, 0))

// ── Customer-facing display: mirror the cart as it's rung up ──────────────────
const displaySnapshot = computed<ShopDisplayState>(() => ({
    // doneOpen is declared below in the checkout section; the computed only reads it lazily.
    status: doneOpen.value ? 'done' : cart.value.length > 0 ? 'charges' : 'idle',
    lines: cart.value.map(l => ({
        name: l.productName,
        detail: [l.label, l.itemLabel].filter(Boolean).join(' · ') || null,
        qty: l.qty,
        lineTotal: l.salePriceCents * l.qty,
    })),
    subtotalCents: subtotal.value,
    note: cart.value.length > 0 ? 'Tax is added at checkout.' : null,
    sign: null,
}))
let displayPushTimer: number | undefined
// Pairing while a cart is already rung up: push right away instead of waiting for the next edit.
watch(shopDisplayPaired, p => {
    if (p) pushShopDisplayState(displaySnapshot.value).catch(() => { /* next change retries */ })
})
watch(displaySnapshot, () => {
    if (!shopDisplayPaired.value) return
    if (displayPushTimer) window.clearTimeout(displayPushTimer)
    // Mirror sync is best-effort: a failed push must never interrupt ringing up the sale.
    displayPushTimer = window.setTimeout(() =>
        pushShopDisplayState(displaySnapshot.value).catch(() => { /* next change retries */ }), 250)
})
onUnmounted(() => {
    if (displayPushTimer) window.clearTimeout(displayPushTimer)
    // Leaving the register mid-cart: welcome screen beats a frozen cart.
    if (shopDisplayPaired.value) pushShopDisplayState(idleShopDisplayState()).catch(() => { /* best-effort */ })
})

function addVariant(v: Tile) {
    if (v.availableCount <= 0 || v.salePriceCents == null) return
    if (v.trackingKind === 'serialized') { openUnitPicker(v); return }
    const existing = cart.value.find(l => l.variantId === v.variantId && !l.itemId)
    if (existing) {
        if (existing.qty < existing.availableCount) existing.qty++
        else flash('No more of that in stock.', 'error')
        return
    }
    cart.value.push({
        variantId: v.variantId, productName: v.productName, label: v.label,
        salePriceCents: v.salePriceCents, qty: 1, availableCount: v.availableCount, trackingKind: 'pool',
    })
}
function bump(i: number, d: number) {
    const l = cart.value[i]
    l.qty = Math.max(1, Math.min(l.availableCount, l.qty + d))
}

// Serialized unit picker
const unitPickerOpen = ref(false)
const pickerVariant = ref<Tile | null>(null)
const pickerItems = ref<ShopItem[]>([])
const pickerLoading = ref(false)
async function openUnitPicker(v: Tile) {
    pickerVariant.value = v
    unitPickerOpen.value = true
    pickerLoading.value = true
    try {
        const all = (await service.listItems(v.variantId)).data.data
        const inCart = new Set(cart.value.filter(l => l.itemId).map(l => l.itemId))
        pickerItems.value = all.filter(it => it.status === 'available' && !inCart.has(it.id))
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not load units.', 'error')
        unitPickerOpen.value = false
    } finally { pickerLoading.value = false }
}
function addSerialized(it: ShopItem) {
    const v = pickerVariant.value!
    cart.value.push({
        variantId: v.variantId, productName: v.productName, label: v.label,
        salePriceCents: v.salePriceCents!, qty: 1, availableCount: 1, trackingKind: 'serialized',
        itemId: it.id, itemLabel: `${it.label}${it.serial ? ' · ' + it.serial : ''}`,
    })
    unitPickerOpen.value = false
}

// ── Checkout ──────────────────────────────────────────────────────────────
const doneOpen = ref(false)
const lastOrderNumber = ref<number | null>(null)
const lastTotal = ref(0)
const lastMethod = ref('')

function buildLines() {
    return cart.value.map(l => ({ variantId: l.variantId, quantity: l.qty, itemId: l.itemId ?? null }))
}

async function checkout(method: 'cash' | 'card') {
    checkoutError.value = ''
    payMethod.value = method
    busy.value = true
    try {
        const useReader = method === 'card' && readerState.value === 'connected'
        const r = await service.ringUp({
            lines: buildLines(),
            paymentMethod: method,
            buyerName: buyerName.value.trim() || null,
            buyerEmail: buyerEmail.value.trim() || null,
            couponCode: couponCode.value.trim() || null,
            giftCardCode: giftCardCode.value.trim() || null,
            discountPresetId: discountPresetId.value || null,
            managerPin: managerPin.value || null,
            creditAccountId: creditAccount.value?.id ?? null,
            creditCents: creditAccount.value?.balanceCents ?? 0,
            cardPresent: useReader,
        })
        const data = r.data.data
        lastRingUp.value = data
        const credited = data.creditAppliedCents ?? 0
        const gifted = data.giftCardAppliedCents ?? 0
        const tenderNote = [credited > 0 ? `${money(credited)} credit` : '', gifted > 0 ? `${money(gifted)} gift card` : '']
            .filter(Boolean).join(' + ')
        if (data.status === 'paid') {
            // Cash, or gift card / credit covered the whole sale.
            finishSale(data.orderNumber ?? null, data.totalCents,
                tenderNote ? `${method === 'cash' ? 'Cash' : 'Card'} + ${tenderNote}` : 'Cash')
        } else if (useReader) {
            // Card-present: collect on the reader, then finalize immediately (webhook backs it up).
            const terminal = await getTerminal(fetchTerminalToken)
            await collectAndProcess(terminal!, data.clientSecret!)
            try { await service.confirmIntent(data.paymentIntentId!) } catch { /* webhook finalizes */ }
            finishSale(null, data.totalCents, tenderNote ? `Card (reader) + ${tenderNote}` : 'Card (reader)')
        } else {
            currentSaleId.value = data.saleId
            pendingTotal.value = data.dueCents ?? data.totalCents
            clientSecret.value = data.clientSecret ?? null
            payOpen.value = true
            await nextTick()
            await mountPayment()
        }
    } catch (e: any) {
        checkoutError.value = e.response?.data?.error || 'Could not ring up the sale. Please try again.'
    } finally { busy.value = false }
}

// Card payment
const payOpen = ref(false)
const paying = ref(false)
const payError = ref('')
const stripeReady = ref(false)
const clientSecret = ref<string | null>(null)
const pendingTotal = ref(0)
const currentSaleId = ref<string | null>(null)
let stripe: any = null
let elements: any = null

async function mountPayment() {
    payError.value = ''
    stripeReady.value = false
    if (!clientSecret.value) { payError.value = 'Payment could not be started.'; return }
    const account = branding.stripeChargeMode === 'direct' ? branding.stripeConnectAccountId : null
    stripe = await getStripe(branding.stripePublishableKey, account)
    if (!stripe) { payError.value = 'Payments are unavailable right now.'; return }
    elements = stripe.elements({ clientSecret: clientSecret.value })
    elements.create('payment').mount('#shop-payment-element')
    stripeReady.value = true
}

async function payCard() {
    if (!stripe || !elements) return
    paying.value = true
    payError.value = ''
    try {
        const { error, paymentIntent } = await stripe.confirmPayment({ elements, redirect: 'if_required' })
        if (error) {
            payError.value = error.message || 'Payment failed. Check the card and try again.'
        } else if (paymentIntent?.status === 'succeeded') {
            // Finalize now (idempotent with the webhook) so stock + ledger are booked immediately.
            try { await service.confirmIntent(paymentIntent.id) } catch { /* webhook will finalize */ }
            payOpen.value = false
            finishSale(null, pendingTotal.value, 'Card')
        } else {
            payError.value = paymentIntent?.status === 'processing'
                ? 'Payment is processing. It will complete shortly.'
                : "Couldn't confirm the payment. If the card was charged, it will settle shortly."
        }
    } catch (e: any) {
        payError.value = e?.message || 'Payment failed.'
    } finally { paying.value = false }
}

function cancelCard() {
    // The pending sale stays pending server-side (never paid); the reconciler expires it. The
    // cashier can start a new sale.
    payOpen.value = false
    stripe = null; elements = null; stripeReady.value = false
    flash('Card payment cancelled.', 'error')
}

function finishSale(orderNumber: number | null, totalCents: number, method: string) {
    lastOrderNumber.value = orderNumber
    lastTotal.value = totalCents
    lastMethod.value = method
    // Snapshot the receipt before the cart is cleared. Server-priced facts (tax, discounts, the
    // full total) come from the ring-up response; line detail from the cart.
    const facts = lastRingUp.value
    lastReceipt.value = {
        header: branding.displayName || 'Receipt',
        orderNumber,
        lines: cart.value.map(l => ({
            quantity: l.qty, name: l.productName,
            variantLabel: [l.label, l.itemLabel].filter(Boolean).join(' · ') || null,
            modifierLabels: [], notes: null, lineTotal: l.salePriceCents * l.qty,
        })),
        subtotalCents: facts?.subtotalCents ?? cart.value.reduce((s, l) => s + l.salePriceCents * l.qty, 0),
        discountCents: facts?.discountCents ?? 0, discountLabel: null,
        taxCents: facts?.taxCents ?? 0, pricesIncludeTax: false,
        totalCents: facts?.totalCents ?? totalCents, method,
    }
    doneOpen.value = true
    autoPrint()
    cart.value = []
    buyerName.value = ''
    buyerEmail.value = ''
    couponCode.value = ''
    discountPresetId.value = null
    managerPin.value = ''
    giftCardCode.value = ''
    clearCredit()
    reload()   // refresh stock counts
}

// ── Store credit tender (shared CreditLookupField drives the account selection) ───
const creditAccount = ref<CreditLookupResult | null>(null)
function clearCredit() { creditAccount.value = null }

function newSale() { doneOpen.value = false }

// ── Receipt printing (silent ePOS, same helper as the F&B POS). The URL is per tablet; a tablet
// already set up for the F&B register carries its printer over as the default here. ──────────────
const printerUrl = ref(localStorage.getItem('shopPrinterUrl') ?? localStorage.getItem('concessionPrinterUrl') ?? '')
const printerDialog = ref(false)
const lastRingUp = ref<any>(null)
const lastReceipt = ref<Receipt | null>(null)

function savePrinter() {
    localStorage.setItem('shopPrinterUrl', printerUrl.value.trim())
    printerDialog.value = false
    flash('Printer saved.', 'success')
}

// Auto-print on sale completion. A failure surfaces as a toast rather than blocking the line;
// the cashier can retry from the sale-complete dialog.
async function autoPrint() {
    if (!printerUrl.value || !lastReceipt.value) return
    try { await printReceipt(printerUrl.value, lastReceipt.value) }
    catch (err: any) { flash(err.message || 'Receipt did not print.', 'error') }
}

async function printLast() {
    if (!lastReceipt.value) return
    try { await printReceipt(printerUrl.value, lastReceipt.value); flash('Receipt sent.', 'success') }
    catch (err: any) { flash(err.message || 'Receipt did not print.', 'error') }
}

async function reload() {
    loading.value = cart.value.length === 0 && products.value.length === 0
    loadError.value = ''
    try { products.value = (await service.listProducts(true)).data.data }
    catch (e: any) { loadError.value = e.response?.data?.error || 'Could not load products. Refresh to try again.' }
    finally { loading.value = false }
}

async function loadDiscounts() {
    try {
        const r = await discountService.forSurface('shop_sale')
        discounts.value = r.data.data
    } catch {
        // The picker is optional; a sale can still be rung without it, so a failure here must not
        // block the register. It simply won't offer discounts this session.
        discounts.value = []
    }
}

onMounted(() => { reload(); loadDiscounts() })
</script>

<style scoped>
.pos-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
    gap: 10px;
}
.pos-tile {
    padding: 10px 12px;
    cursor: pointer;
    transition: box-shadow 0.12s ease, transform 0.12s ease;
}
.pos-tile:hover { box-shadow: 0 4px 14px rgba(0, 0, 0, 0.14); transform: translateY(-1px); }
.pos-qty { min-width: 24px; text-align: center; font-variant-numeric: tabular-nums; }
</style>
