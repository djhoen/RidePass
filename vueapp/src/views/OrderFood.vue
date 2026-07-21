<template>
    <v-container class="of-container">
        <h1 class="text-h4 mb-1 font-weight-bold">Order Food</h1>
        <p class="text-body-2 text-medium-emphasis mb-4">Order ahead and pay here; we'll call your number when it's ready.</p>

        <v-alert v-if="!branding.concessionsEnabled" type="info" variant="tonal" class="mb-4">
            Food ordering isn't available at this track right now.
        </v-alert>
        <v-alert v-else-if="!orderingOpenNow" type="info" variant="tonal" class="mb-4">
            {{ orderingReason || 'Online ordering is currently closed. Please check back during open hours.' }}
        </v-alert>
        <v-alert v-else-if="quoteMinutes !== null" type="success" variant="tonal" density="compact"
            class="mb-4" icon="mdi-clock-fast">
            Order now, ready in about {{ quoteMinutes }} to {{ quoteMinutes + 5 }} min.
        </v-alert>

        <!-- My recent orders -->
        <v-card v-if="myOrders.length" class="mb-4">
            <v-card-title class="text-subtitle-1">Your orders</v-card-title>
            <v-list density="compact">
                <v-list-item v-for="o in myOrders" :key="o.saleId">
                    <v-list-item-title>
                        Order #{{ o.orderNumber ?? '—' }} · {{ money(o.totalCents) }}
                    </v-list-item-title>
                    <template #append>
                        <v-chip size="small" :color="statusColor(o)">{{ statusLabel(o) }}</v-chip>
                    </template>
                </v-list-item>
            </v-list>
        </v-card>

        <v-row v-if="branding.concessionsEnabled && orderingOpenNow">
            <!-- Menu -->
            <v-col cols="12" md="8">
                <div v-if="loading" class="d-flex justify-center pa-8"><v-progress-circular indeterminate /></div>
                <template v-else>
                    <div v-for="g in categoryGroups" :key="g.key" class="mb-6">
                        <h2 class="text-h6 font-weight-bold mb-3">{{ g.name }}</h2>
                        <div class="of-grid">
                            <div v-for="p in g.items" :key="p.id" class="of-card" :class="{ 'of-card--out': p.soldOut }" @click="beginAdd(p)">
                                <div class="of-card__media">
                                    <v-img v-if="p.imageUrl" :src="p.imageUrl" cover height="100%" />
                                    <div v-else class="of-card__ph"><v-icon size="32" color="grey-lighten-1">mdi-silverware-fork-knife</v-icon></div>
                                    <div v-if="p.soldOut" class="of-card__out">Sold out</div>
                                    <v-btn icon="mdi-dots-vertical" size="x-small" variant="flat" class="of-card__info" @click.stop="openDetails(p)" />
                                    <v-btn v-if="!p.soldOut" icon="mdi-plus" size="small" color="primary" class="of-card__add" elevation="2" @click.stop="beginAdd(p)" />
                                </div>
                                <div class="of-card__body">
                                    <div class="of-card__name">{{ p.name }}</div>
                                    <div v-if="p.description" class="of-card__desc">{{ p.description }}</div>
                                    <div class="of-card__price">{{ priceLabel(p) }}</div>
                                </div>
                            </div>
                        </div>
                    </div>
                </template>
            </v-col>

            <!-- Cart (sticky on desktop) -->
            <v-col cols="12" md="4">
                <v-card class="of-cart">
                    <v-card-title class="text-subtitle-1 d-flex align-center">
                        Your order
                        <v-chip v-if="cartCount" size="small" class="ml-2" variant="tonal">{{ cartCount }}</v-chip>
                    </v-card-title>
                    <v-divider />
                    <v-card-text>
                        <div v-if="cart.length === 0" class="text-medium-emphasis text-center py-6">
                            <v-icon size="40" color="grey-lighten-1">mdi-cart-outline</v-icon>
                            <div class="mt-2">Tap items to add them.</div>
                        </div>
                        <div v-for="(line, i) in cart" :key="i" class="d-flex mb-3">
                            <div class="flex-grow-1">
                                <div class="text-body-2 font-weight-medium">{{ line.name }}</div>
                                <div v-if="line.variantLabel" class="text-caption text-medium-emphasis">{{ line.variantLabel }}</div>
                                <div v-for="m in line.modifierLabels" :key="m" class="text-caption text-medium-emphasis">+ {{ m }}</div>
                                <div v-if="line.notes" class="text-caption font-italic text-medium-emphasis">"{{ line.notes }}"</div>
                                <div class="d-flex align-center mt-1">
                                    <v-btn icon="mdi-minus" size="x-small" variant="tonal" @click="setLineQty(i, line.quantity - 1)" />
                                    <span class="mx-3">{{ line.quantity }}</span>
                                    <v-btn icon="mdi-plus" size="x-small" variant="tonal" @click="setLineQty(i, line.quantity + 1)" />
                                </div>
                            </div>
                            <div class="text-right">
                                <div class="text-body-2 font-weight-medium">{{ money(line.lineTotal) }}</div>
                                <v-btn icon="mdi-close" size="x-small" variant="text" @click="cart.splice(i, 1)" />
                            </div>
                        </div>
                    </v-card-text>
                    <template v-if="cart.length">
                        <v-divider />
                        <v-card-text>
                            <div class="d-flex justify-space-between"><span class="text-medium-emphasis">Subtotal</span><span>{{ money(pricesIncludeTax ? subtotal - taxCents : subtotal) }}</span></div>
                            <div v-if="taxCents" class="d-flex justify-space-between mt-1"><span class="text-medium-emphasis">Tax{{ pricesIncludeTax ? ' (incl.)' : '' }}</span><span>{{ money(taxCents) }}</span></div>
                            <template v-if="tipsEnabled">
                                <div class="text-medium-emphasis mt-3 mb-1">Add a tip?</div>
                                <div class="d-flex flex-wrap ga-2">
                                    <v-btn size="small" :variant="tipMode === 'none' ? 'flat' : 'outlined'" :color="tipMode === 'none' ? 'primary' : undefined" @click="tipMode = 'none'">No tip</v-btn>
                                    <v-btn v-for="pct in [15, 18, 20]" :key="pct" size="small"
                                        :variant="tipMode === 'pct' && tipPct === pct ? 'flat' : 'outlined'"
                                        :color="tipMode === 'pct' && tipPct === pct ? 'primary' : undefined"
                                        @click="tipMode = 'pct'; tipPct = pct">{{ pct }}%</v-btn>
                                    <v-btn size="small" :variant="tipMode === 'custom' ? 'flat' : 'outlined'" :color="tipMode === 'custom' ? 'primary' : undefined" @click="tipMode = 'custom'">Custom</v-btn>
                                </div>
                                <v-text-field v-if="tipMode === 'custom'" v-model.number="tipCustomDollars" type="number" min="0" step="0.50"
                                    prefix="$" label="Custom tip" density="compact" hide-details class="mt-2" style="max-width: 180px" />
                            </template>
                            <v-checkbox v-if="myCreditCents > 0" v-model="useStoreCredit" color="primary"
                                density="compact" hide-details class="mt-2"
                                :label="`Use my store credit (${money(myCreditCents)} available)`"></v-checkbox>
                            <div v-if="useStoreCredit && creditToApply > 0" class="d-flex justify-space-between mt-1 text-body-2 text-success">
                                <span>Store credit</span><span>-{{ money(creditToApply) }}</span>
                            </div>
                            <div class="d-flex justify-space-between mt-3 text-h6 font-weight-bold">
                                <span>{{ useStoreCredit && creditToApply > 0 ? 'Due' : 'Total' }}</span>
                                <span>{{ money(total - (useStoreCredit ? creditToApply : 0)) }}</span>
                            </div>
                        </v-card-text>
                        <v-card-actions class="pa-3">
                            <v-btn block color="primary" size="large" :loading="placing" @click="checkout">Place order &amp; pay</v-btn>
                        </v-card-actions>
                    </template>
                </v-card>
            </v-col>
        </v-row>

        <!-- Sticky mobile checkout bar -->
        <div v-if="branding.concessionsEnabled && orderingOpenNow && cart.length" class="of-bottombar d-md-none">
            <div class="flex-grow-1">
                <div class="text-h6 font-weight-bold">{{ money(total) }}</div>
                <div class="text-caption text-medium-emphasis">{{ cartCount }} item{{ cartCount === 1 ? '' : 's' }}</div>
            </div>
            <v-btn color="primary" size="large" :loading="placing" @click="checkout">Place order &amp; pay</v-btn>
        </div>

        <!-- Item details (description + what it comes with) -->
        <v-dialog v-model="detailsDialog" max-width="420">
            <v-card v-if="detailsProduct">
                <v-img v-if="detailsProduct.imageUrl" :src="detailsProduct.imageUrl" height="200" cover />
                <v-card-title class="d-flex align-center">
                    <span class="text-truncate">{{ detailsProduct.name }}</span>
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="detailsDialog = false" />
                </v-card-title>
                <v-card-text>
                    <div class="text-h6 mb-2">{{ priceLabel(detailsProduct) }}</div>
                    <p v-if="detailsProduct.description" class="mb-3">{{ detailsProduct.description }}</p>
                    <p v-else class="text-medium-emphasis mb-3">No description.</p>
                    <template v-if="detailDefaults(detailsProduct).length">
                        <div class="font-weight-medium">Comes with</div>
                        <div class="mb-3">{{ detailDefaults(detailsProduct).join(', ') }}</div>
                    </template>
                    <template v-if="detailsProduct.modifierGroups.length">
                        <div class="font-weight-medium">Options</div>
                        <div v-for="g in detailsProduct.modifierGroups" :key="g.id" class="text-body-2 text-medium-emphasis">
                            {{ g.name }}: {{ g.options.map(o => o.name).join(', ') }}
                        </div>
                    </template>
                </v-card-text>
                <v-card-actions v-if="!detailsProduct.soldOut">
                    <v-spacer />
                    <v-btn color="primary" variant="flat" @click="addFromDetails">Add to order</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Add item dialog -->
        <v-dialog v-model="addDialog" max-width="460">
            <v-card v-if="adding">
                <v-card-title class="d-flex align-center">
                    {{ adding.name }}
                    <v-spacer /><v-btn icon="mdi-close" variant="text" size="small" @click="addDialog = false" />
                </v-card-title>
                <v-card-text>
                    <div v-if="adding.variants.length" class="mb-2">
                        <div class="text-subtitle-2">Option</div>
                        <v-radio-group v-model="selVariantId" density="compact" hide-details>
                            <v-radio v-for="v in adding.variants" :key="v.id" :value="v.id"
                                :label="`${variantLabel(v)} ${priceSuffix(v.priceCents)}`" />
                        </v-radio-group>
                    </div>
                    <div v-for="g in adding.modifierGroups" :key="g.id" class="mb-2">
                        <div class="text-subtitle-2">{{ g.name }} <span class="text-caption text-medium-emphasis">{{ groupHint(g) }}</span></div>
                        <v-checkbox v-for="o in g.options" :key="o.id" density="compact" hide-details
                            :label="`${o.name} ${priceSuffix(o.priceDeltaCents)}`"
                            :model-value="selOptions[g.id]?.includes(o.id) ?? false"
                            @update:model-value="toggleOption(g, o.id, !!$event)" />
                    </div>
                    <!-- Make it a combo -->
                    <div v-if="canCombo(adding)" class="mt-4 pa-3" style="border: 1px solid rgba(128, 128, 128, 0.25); border-radius: 8px;">
                        <div class="text-subtitle-2 mb-1">Make it a combo</div>
                        <v-radio-group v-model="selComboTierId" density="compact" hide-details>
                            <v-radio :value="null" label="No thanks" />
                            <v-radio v-for="t in comboConfig.tiers" :key="t.id" :value="t.id"
                                :label="`${t.name} ${priceSuffix(t.priceCents)}`" />
                        </v-radio-group>
                        <template v-if="selComboTierId">
                            <div v-for="slot in comboConfig.slots" :key="slot.id" class="mt-2">
                                <div class="text-caption font-weight-medium">{{ slot.name }}</div>
                                <v-radio-group v-model="selComboSlots[slot.id]" density="compact" hide-details>
                                    <v-radio v-for="o in slot.options" :key="o.id" :value="o.id"
                                        :label="comboOptionLabel(slot, o)" />
                                </v-radio-group>
                            </div>
                        </template>
                    </div>

                    <v-text-field v-model="selNotes" label="Notes (e.g. onions on the side)" density="compact" class="mt-4" hide-details />
                    <div class="d-flex align-center mt-4">
                        <span class="mr-2">Qty</span>
                        <v-btn icon="mdi-minus" size="x-small" variant="tonal" @click="selQty = Math.max(1, selQty - 1)" />
                        <span class="mx-3 text-h6">{{ selQty }}</span>
                        <v-btn icon="mdi-plus" size="x-small" variant="tonal" @click="selQty++" />
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer /><v-btn variant="text" @click="addDialog = false">Cancel</v-btn>
                    <v-btn color="primary" @click="confirmAdd">Add {{ money(addPreviewTotal) }}</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Payment -->
        <v-dialog v-model="payDialog" max-width="460" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    Pay {{ money(total - (useStoreCredit ? creditToApply : 0)) }}
                    <v-spacer /><v-btn icon="mdi-close" variant="text" size="small" :disabled="paying" @click="cancelPay" />
                </v-card-title>
                <v-card-text>
                    <div id="food-payment-element"></div>
                    <v-alert v-if="payError" type="error" variant="tonal" density="compact" class="mt-3">{{ payError }}</v-alert>
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn color="primary" :loading="paying" :disabled="!stripeReady" @click="pay">Pay</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Confirmation -->
        <v-dialog v-model="doneDialog" max-width="340" persistent>
            <v-card class="text-center pa-4">
                <v-icon color="success" size="64">mdi-check-circle</v-icon>
                <div class="text-h4 mt-2">Order #{{ lastOrderNumber ?? '—' }}</div>
                <div class="text-medium-emphasis mb-4">We'll call your number when it's ready.</div>
                <v-btn block color="primary" @click="doneDialog = false">Done</v-btn>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snack.show" color="error" timeout="5000">{{ snack.text }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'
import type { Stripe, StripeElements } from '@stripe/stripe-js'
import { CreditService } from '@/services/CreditService'
import {
    ConcessionService,
    type ConcessionProduct, type ConcessionVariant, type ConcessionModifierGroup,
    type ConcessionSaleLineInput, type RiderOrder, type ConcessionComboConfig,
    type ConcessionComboSlot, type ConcessionComboSlotOption,
} from '@/services/ConcessionService'
import { branding, loadBranding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'

interface CartLine {
    input: ConcessionSaleLineInput
    name: string; variantLabel: string | null; modifierLabels: string[]
    notes: string | null; quantity: number; unitPrice: number; lineTotal: number
}

const svc = new ConcessionService()
const products = ref<ConcessionProduct[]>([])
const myOrders = ref<RiderOrder[]>([])
const loading = ref(true)
const cart = ref<CartLine[]>([])
const orderingOpenNow = ref(true)
const orderingReason = ref<string | null>(null)
const quoteMinutes = ref<number | null>(null)
const tipsEnabled = ref(false)
const tipMode = ref<'none' | 'pct' | 'custom'>('none')
const tipPct = ref(18)
const tipCustomDollars = ref<number | null>(null)
const snack = ref({ show: false, text: '' })
let timer: number | undefined

function flash(text: string) { snack.value = { show: true, text } }
function money(c: number) { return `$${(c / 100).toFixed(2)}` }
function variantLabel(v: ConcessionVariant) { return [v.size, v.color].filter(Boolean).join(' / ') || 'Standard' }
function priceSuffix(d: number | null) { return !d ? '' : d > 0 ? `(+${money(d)})` : `(${money(d)})` }
function groupHint(g: ConcessionModifierGroup) {
    if (g.isRequired && g.maxSelect === 1) return '· choose 1'
    if (g.maxSelect) return `· up to ${g.maxSelect}${g.isRequired ? ', required' : ''}`
    return g.isRequired ? '· required' : ''
}
function priceLabel(p: ConcessionProduct) {
    if (p.variants.length === 0) return money(p.priceCents)
    const prices = p.variants.map(v => v.priceCents ?? p.priceCents)
    const min = Math.min(...prices)
    return min === Math.max(...prices) ? money(min) : `from ${money(min)}`
}
function statusLabel(o: RiderOrder) {
    if (o.status !== 'paid') return o.status
    return o.fulfillmentStatus === 'ready' ? 'Ready!' : o.fulfillmentStatus === 'completed' ? 'Picked up' : 'Preparing'
}
function statusColor(o: RiderOrder) {
    if (o.status !== 'paid') return 'grey'
    return o.fulfillmentStatus === 'ready' ? 'success' : 'primary'
}

// Group products by category, ordered by category sort order; uncategorized fall last under "Other".
const categoryGroups = computed(() => {
    const map = new Map<string, { key: string; name: string; sort: number; items: ConcessionProduct[] }>()
    for (const p of products.value) {
        const key = p.categoryId ?? 'uncategorized'
        if (!map.has(key)) map.set(key, { key, name: p.categoryName ?? 'Other', sort: p.categoryId ? p.categorySortOrder : Number.MAX_SAFE_INTEGER, items: [] })
        map.get(key)!.items.push(p)
    }
    return [...map.values()].sort((a, b) => a.sort - b.sort || a.name.localeCompare(b.name))
})
const subtotal = computed(() => cart.value.reduce((s, l) => s + l.lineTotal, 0))
const cartCount = computed(() => cart.value.reduce((s, l) => s + l.quantity, 0))
const tipCents = computed(() => {
    if (!tipsEnabled.value || tipMode.value === 'none') return 0
    if (tipMode.value === 'custom') return Math.max(0, Math.round((tipCustomDollars.value || 0) * 100))
    return Math.round(subtotal.value * tipPct.value / 100)
})

// Tax preview (server is authoritative). Mirrors the cashier POS: per-item rate from its tax category
// or the tenant default, with exclusive/inclusive math.
const pricesIncludeTax = ref(false)
const taxRateByCategory = ref<Record<string, number>>({})
const defaultTaxBps = ref(0)
function lineTaxBps(productId: string): number {
    const p = products.value.find(x => x.id === productId)
    const id = p?.taxCategoryId
    return (id && taxRateByCategory.value[id] != null) ? taxRateByCategory.value[id] : defaultTaxBps.value
}
function computeTax(baseCents: number, rateBps: number): number {
    if (rateBps <= 0 || baseCents <= 0) return 0
    if (pricesIncludeTax.value) return baseCents - Math.round(baseCents * 10000 / (10000 + rateBps))
    return Math.round(baseCents * rateBps / 10000)
}
const taxCents = computed(() =>
    cart.value.reduce((s, l) => s + computeTax(l.lineTotal, lineTaxBps(l.input.productId)), 0))
const total = computed(() => subtotal.value + (pricesIncludeTax.value ? 0 : taxCents.value) + tipCents.value)

// Store credit: balance loaded on mount (this page requires sign-in); the server re-verifies
// and caps, so this is the offer + display only.
const myCreditCents = ref(0)
const useStoreCredit = ref(false)
const creditToApply = computed(() => Math.min(myCreditCents.value, total.value))

// Adjust a cart line's quantity inline; dropping to 0 removes it.
function setLineQty(i: number, qty: number) {
    if (qty <= 0) { cart.value.splice(i, 1); return }
    const l = cart.value[i]
    l.quantity = qty
    l.lineTotal = l.unitPrice * qty
}

onMounted(async () => {
    if (!branding.loaded) await loadBranding()
    await Promise.all([loadMenu(), refreshOrders(), refreshStatus()])
    // Best-effort: no balance just means the credit offer doesn't show.
    try { myCreditCents.value = (await new CreditService().mine()).data.data.balanceCents }
    catch { /* offer stays hidden */ }
    // Poll status + orders together: keeps "Ready!" live and the quote/open-state fresh as the kitchen
    // fills and drains.
    timer = window.setInterval(() => { refreshOrders(); refreshStatus() }, 8000)
})
onUnmounted(() => { if (timer) window.clearInterval(timer) })

// Live open-state + quote from the throttle. Falls back to the menu-settings open flag on failure.
async function refreshStatus() {
    try {
        const s = (await svc.orderingStatus() as any).data.data
        orderingOpenNow.value = s.openNow
        orderingReason.value = s.reason
        quoteMinutes.value = s.quoteMinutes
    } catch { /* best-effort; keep the last known state */ }
}

async function loadMenu() {
    loading.value = true
    try {
        const ms = (await svc.menuSettings() as any).data.data
        tipsEnabled.value = ms.tipsEnabled
        orderingOpenNow.value = ms.orderingOpenNow
        pricesIncludeTax.value = ms.pricesIncludeTax
    } catch { /* settings optional */ }
    try {
        const cats = (await svc.taxCategories() as any).data.data as { id: string; rateBps: number; isDefault: boolean }[]
        taxRateByCategory.value = Object.fromEntries(cats.map(c => [c.id, c.rateBps]))
        defaultTaxBps.value = cats.find(c => c.isDefault)?.rateBps ?? 0
    } catch { /* tax optional */ }
    try { products.value = (await svc.riderMenu() as any).data.data }
    catch (err: any) { flash(err.response?.data?.error || 'Could not load the menu.') }
    finally { loading.value = false }
    try { comboConfig.value = (await svc.getComboConfig() as any).data.data } catch { /* combos optional */ }
}
async function refreshOrders() {
    try { myOrders.value = (await svc.myOrders() as any).data.data } catch { /* best-effort */ }
}

// ── Add item ─────────────────────────────────────────────────────────
const addDialog = ref(false)
const adding = ref<ConcessionProduct | null>(null)

// ── Item details (description + what it comes with) ──────────────────
const detailsDialog = ref(false)
const detailsProduct = ref<ConcessionProduct | null>(null)
function openDetails(p: ConcessionProduct) { detailsProduct.value = p; detailsDialog.value = true }
function detailDefaults(p: ConcessionProduct): string[] {
    const ids = new Set(p.defaultModifierOptionIds ?? [])
    return p.modifierGroups.flatMap(g => g.options.filter(o => ids.has(o.id)).map(o => o.name))
}
function addFromDetails() {
    const p = detailsProduct.value
    detailsDialog.value = false
    if (p) beginAdd(p)
}
const selVariantId = ref<string | null>(null)
const selOptions = ref<Record<string, string[]>>({})
const selNotes = ref('')
const selQty = ref(1)

function beginAdd(p: ConcessionProduct) {
    if (p.soldOut) { flash(`"${p.name}" is sold out.`); return }
    adding.value = p
    selVariantId.value = p.variants.length ? p.variants[0].id : null
    // Pre-select the item's defaults (e.g. lettuce, tomato) so the rider sees them already checked.
    const defaults = new Set(p.defaultModifierOptionIds ?? [])
    const sel: Record<string, string[]> = {}
    for (const g of p.modifierGroups) sel[g.id] = g.options.filter(o => defaults.has(o.id)).map(o => o.id)
    selOptions.value = sel
    selNotes.value = ''; selQty.value = 1
    selComboTierId.value = null
    selComboSlots.value = defaultComboSlotSel()
    // Nothing to choose (no variant, no modifiers, not a combo): add straight to the cart.
    if (!canCombo(p) && p.variants.length === 0 && p.modifierGroups.length === 0) { confirmAdd(); return }
    addDialog.value = true
}
function toggleOption(g: ConcessionModifierGroup, optionId: string, checked: boolean) {
    const cur = selOptions.value[g.id] ?? []
    selOptions.value[g.id] = checked ? (g.maxSelect === 1 ? [optionId] : [...cur, optionId]) : cur.filter(id => id !== optionId)
}
function unitPriceFor(p: ConcessionProduct, variantId: string | null, optionIds: string[]) {
    let price = p.variants.find(x => x.id === variantId)?.priceCents ?? p.priceCents
    for (const g of p.modifierGroups) for (const o of g.options) if (optionIds.includes(o.id)) price += o.priceDeltaCents
    return price
}
const addPreviewTotal = computed(() => {
    if (!adding.value) return 0
    return (unitPriceFor(adding.value, selVariantId.value, Object.values(selOptions.value).flat()) + comboExtra.value) * selQty.value
})
function confirmAdd() {
    const p = adding.value!
    if (p.variants.length && !selVariantId.value) { flash('Choose an option.'); return }
    for (const g of p.modifierGroups) {
        const count = (selOptions.value[g.id] ?? []).length
        if (g.isRequired && count === 0) { flash(`Choose ${g.name}.`); return }
        if (count < g.minSelect) { flash(`Choose at least ${g.minSelect} for ${g.name}.`); return }
        if (g.maxSelect && count > g.maxSelect) { flash(`Choose at most ${g.maxSelect} for ${g.name}.`); return }
    }
    if (selComboTierId.value) {
        for (const slot of comboConfig.value.slots)
            if (slot.isRequired && !selComboSlots.value[slot.id]) { flash(`Choose ${slot.name}.`); return }
    }
    const optionIds = Object.values(selOptions.value).flat()
    const v = p.variants.find(x => x.id === selVariantId.value) ?? null
    const modifierLabels = p.modifierGroups.flatMap(g => g.options.filter(o => optionIds.includes(o.id)).map(o => o.name))
    let unit = unitPriceFor(p, selVariantId.value, optionIds)
    let comboSelections: { slotId: string; optionId: string }[] | undefined

    const tier = selComboTierId.value ? comboConfig.value.tiers.find(t => t.id === selComboTierId.value) : null
    if (tier) {
        unit += tier.priceCents
        comboSelections = []
        modifierLabels.unshift(`${tier.name} combo`)
        for (const slot of comboConfig.value.slots) {
            const oid = selComboSlots.value[slot.id]
            if (!oid) continue
            unit += comboSubDiff(slot, oid, tier.sizeLabel)
            comboSelections.push({ slotId: slot.id, optionId: oid })
            const o = slot.options.find(x => x.id === oid)
            if (o) modifierLabels.push(`${o.componentName}${tier.sizeLabel ? ` (${tier.sizeLabel})` : ''}`)
        }
    }

    const trimmed = selNotes.value.trim() || null
    cart.value.push({
        input: { productId: p.id, variantId: v?.id ?? null, quantity: selQty.value, modifierOptionIds: optionIds, notes: trimmed,
            comboTierId: selComboTierId.value ?? null, comboSelections },
        name: p.name, variantLabel: v ? variantLabel(v) : null, modifierLabels,
        notes: trimmed, quantity: selQty.value, unitPrice: unit, lineTotal: unit * selQty.value,
    })
    addDialog.value = false
}

// ── Make it a combo (layered onto the add modal) ─────────────────────
const comboConfig = ref<ConcessionComboConfig>({ tiers: [], slots: [] })
const selComboTierId = ref<string | null>(null)
const selComboSlots = ref<Record<string, string>>({})

function canCombo(p: ConcessionProduct | null): boolean {
    return !!p && p.comboAvailable && comboConfig.value.tiers.length > 0
}
function defaultComboSlotSel(): Record<string, string> {
    const sel: Record<string, string> = {}
    for (const slot of comboConfig.value.slots) {
        const def = slot.options.find(o => o.isDefault) ?? slot.options[0]
        if (def) sel[slot.id] = def.id
    }
    return sel
}
function componentPriceAtTier(productId: string, sizeLabel: string | null): number {
    const p = products.value.find(x => x.id === productId)
    if (!p) return 0
    if (sizeLabel) {
        const v = p.variants.find(v => (v.size ?? '').toLowerCase() === sizeLabel.toLowerCase())
        if (v) return v.priceCents ?? p.priceCents
    }
    return p.priceCents
}
function comboSubDiff(slot: ConcessionComboSlot, optionId: string, sizeLabel: string | null): number {
    const chosen = slot.options.find(o => o.id === optionId)
    if (!chosen) return 0
    const included = slot.options.find(o => o.isDefault)
    const chosenPrice = componentPriceAtTier(chosen.componentProductId, sizeLabel)
    const includedPrice = included ? componentPriceAtTier(included.componentProductId, sizeLabel) : chosenPrice
    return Math.max(0, chosenPrice - includedPrice)
}
function comboOptionLabel(slot: ConcessionComboSlot, o: ConcessionComboSlotOption): string {
    const tier = comboConfig.value.tiers.find(t => t.id === selComboTierId.value)
    const diff = tier ? comboSubDiff(slot, o.id, tier.sizeLabel) : 0
    return `${o.componentName}${diff > 0 ? ` +${money(diff)}` : ''}`
}
const comboExtra = computed(() => {
    const tier = comboConfig.value.tiers.find(t => t.id === selComboTierId.value)
    if (!tier) return 0
    let extra = tier.priceCents
    for (const slot of comboConfig.value.slots) {
        const oid = selComboSlots.value[slot.id]
        if (oid) extra += comboSubDiff(slot, oid, tier.sizeLabel)
    }
    return extra
})

// ── Checkout + payment (online, via the Payment Element) ──────────────
const placing = ref(false)
const payDialog = ref(false)
const paying = ref(false)
const stripeReady = ref(false)
const payError = ref('')
const doneDialog = ref(false)
const lastOrderNumber = ref<number | null>(null)
let stripe: Stripe | null = null
let elements: StripeElements | null = null
let currentSaleId: string | null = null

async function checkout() {
    placing.value = true
    try {
        const res = (await svc.placeOrder({
            items: cart.value.map(l => l.input), tipCents: tipCents.value, paymentMethod: 'card',
            creditCents: useStoreCredit.value ? myCreditCents.value : 0,
        }) as any).data.data
        currentSaleId = res.saleId
        if (res.status === 'paid') {
            // Store credit covered the whole order: no card to run.
            myCreditCents.value = Math.max(0, myCreditCents.value - (res.creditAppliedCents ?? 0))
            lastOrderNumber.value = res.orderNumber ?? null
            doneDialog.value = true
            cart.value = []; tipMode.value = 'none'; tipCustomDollars.value = null
            await refreshOrders()
            return
        }
        payError.value = ''
        payDialog.value = true
        await nextTick()
        const stripeAccount = branding.stripeChargeMode === 'direct' ? branding.stripeConnectAccountId : null
        stripe = await getStripe(branding.stripePublishableKey, stripeAccount)
        if (!stripe) { payError.value = 'Payments are unavailable right now.'; return }
        elements = stripe.elements({ clientSecret: res.clientSecret! })
        elements.create('payment').mount('#food-payment-element')
        stripeReady.value = true
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not start your order. Nothing was charged.')
    } finally {
        placing.value = false
    }
}

async function pay() {
    if (!stripe || !elements) return
    paying.value = true
    payError.value = ''
    try {
        const { error } = await stripe.confirmPayment({ elements, redirect: 'if_required' })
        if (error) { payError.value = error.message || 'Payment failed. Please try again.'; return }
        payDialog.value = false
        stripeReady.value = false
        lastOrderNumber.value = await pollOrderNumber(currentSaleId!)
        doneDialog.value = true
        cart.value = []; tipMode.value = 'none'; tipCustomDollars.value = null
        await refreshOrders()
    } catch (err: any) {
        payError.value = err.message || 'Payment failed. Please try again.'
    } finally {
        paying.value = false
    }
}

function cancelPay() {
    // Payment not completed; the pending order is swept by the reconciler. Let the rider keep their cart.
    payDialog.value = false
    stripeReady.value = false
}

async function pollOrderNumber(saleId: string): Promise<number | null> {
    for (let i = 0; i < 10; i++) {
        try {
            const o = ((await svc.myOrders() as any).data.data as RiderOrder[]).find(x => x.saleId === saleId)
            if (o && o.status === 'paid' && o.orderNumber != null) return o.orderNumber
        } catch { /* keep polling */ }
        await new Promise(r => setTimeout(r, 1000))
    }
    return null
}
</script>

<style scoped>
.of-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 14px; }

.of-card {
    display: flex;
    flex-direction: column;
    border: 1px solid rgba(128, 128, 128, 0.2);
    border-radius: 14px;
    overflow: hidden;
    cursor: pointer;
    background: rgb(var(--v-theme-surface));
    transition: box-shadow 0.15s ease, transform 0.15s ease;
}
.of-card:hover { box-shadow: 0 6px 18px rgba(0, 0, 0, 0.12); transform: translateY(-2px); }
.of-card--out { opacity: 0.55; cursor: default; }
.of-card--out:hover { box-shadow: none; transform: none; }
.of-card__media { position: relative; height: 120px; background: rgba(128, 128, 128, 0.1); }
.of-card__ph { height: 100%; display: flex; align-items: center; justify-content: center; }
.of-card__add { position: absolute; right: 8px; bottom: 8px; }
.of-card__info { position: absolute; top: 8px; right: 8px; background: rgba(0, 0, 0, 0.45) !important; color: #fff !important; }
.of-card__out {
    position: absolute; top: 10px; left: 0; right: 0; text-align: center;
    background: rgba(211, 47, 47, 0.92); color: #fff; font-weight: 700;
    font-size: 0.72rem; letter-spacing: 0.05em; padding: 3px 0;
}
.of-card__body { padding: 10px 12px 12px; }
.of-card__name { font-weight: 600; line-height: 1.25; }
.of-card__desc {
    font-size: 0.8rem; color: rgba(var(--v-theme-on-surface), 0.6); margin-top: 2px;
    display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden;
}
.of-card__price { font-weight: 700; margin-top: 6px; }

.of-cart { position: sticky; top: 16px; }

.of-bottombar {
    position: fixed;
    left: 0; right: 0; bottom: 0;
    z-index: 5;
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 12px 16px;
    background: rgb(var(--v-theme-surface));
    border-top: 1px solid rgba(128, 128, 128, 0.2);
    box-shadow: 0 -4px 16px rgba(0, 0, 0, 0.12);
}
/* Keep the mobile checkout bar from covering the last items. */
@media (max-width: 959px) {
    .of-container { padding-bottom: 88px; }
}
</style>
