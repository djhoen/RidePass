<template>
    <div class="cfd d-flex flex-column">
        <div v-if="loading" class="d-flex flex-grow-1 align-center justify-center">
            <v-progress-circular indeterminate size="64" />
        </div>

        <!-- Idle / welcome (also carries the pair code so the cashier can pair this tablet) -->
        <div v-else-if="!state || state.status === 'idle'" class="d-flex flex-column flex-grow-1 align-center justify-center pa-8 text-center">
            <v-img v-if="branding.logoUrl" :src="branding.logoUrl" max-height="140" max-width="360" class="mb-6" />
            <div class="text-h3 font-weight-bold mb-2">Welcome to {{ branding.displayName || 'RidePass' }}</div>
            <div class="text-h6 text-medium-emphasis">Order at the counter</div>
            <div v-if="pairCode" class="pair-code text-medium-emphasis">
                Display pair code: <strong>{{ pairCode }}</strong>
            </div>
        </div>

        <!-- Order in progress: read-only mirror of the cashier's cart -->
        <template v-else-if="state.status === 'building' || state.status === 'tip' || state.status === 'processing'">
            <div class="d-flex align-center px-6 py-4">
                <v-img v-if="branding.logoUrl" :src="branding.logoUrl" max-height="48" max-width="140" position="left" />
                <v-spacer />
                <span class="text-h5 font-weight-bold">Your order</span>
            </div>
            <v-divider />

            <div class="cfd-lines flex-grow-1 px-6 py-4">
                <div v-for="(l, i) in state.lines" :key="i" class="cfd-line">
                    <div class="d-flex justify-space-between align-baseline">
                        <span class="text-h6 font-weight-medium">{{ l.quantity }}× {{ l.name }}</span>
                        <span class="text-h6">{{ money(l.lineTotal) }}</span>
                    </div>
                    <div v-if="l.variantLabel" class="text-body-2 text-medium-emphasis">{{ l.variantLabel }}</div>
                    <div v-for="m in l.modifierLabels" :key="m" class="text-body-2 text-medium-emphasis">+ {{ m }}</div>
                </div>
                <div v-if="state.lines.length === 0" class="text-center text-h6 text-medium-emphasis py-12">
                    Your items will appear here.
                </div>
            </div>

            <v-divider />
            <div class="px-6 py-4">
                <div class="d-flex justify-space-between text-body-1">
                    <span class="text-medium-emphasis">Subtotal</span>
                    <span>{{ money(state.pricesIncludeTax ? state.subtotal - state.taxCents : state.subtotal) }}</span>
                </div>
                <div v-if="state.taxCents" class="d-flex justify-space-between text-body-1 mt-1">
                    <span class="text-medium-emphasis">Tax{{ state.pricesIncludeTax ? ' (incl.)' : '' }}</span>
                    <span>{{ money(state.taxCents) }}</span>
                </div>
                <div v-if="state.discountCents" class="d-flex justify-space-between text-body-1 mt-1 text-success">
                    <span>Discount</span><span>-{{ money(state.discountCents) }}</span>
                </div>
                <div v-if="displayTipCents" class="d-flex justify-space-between text-body-1 mt-1">
                    <span class="text-medium-emphasis">Tip</span><span>{{ money(displayTipCents) }}</span>
                </div>
                <div class="d-flex justify-space-between align-center mt-2">
                    <span class="text-h5 font-weight-bold">Total</span>
                    <span class="text-h4 font-weight-bold">{{ money(state.totalCents - state.tipCents + displayTipCents) }}</span>
                </div>

                <!-- Tip prompt: only when the cashier starts checkout and the venue accepts tips -->
                <template v-if="state.status === 'tip' && state.tipsEnabled">
                    <v-divider class="my-4" />
                    <div class="text-h6 font-weight-bold mb-3">Add a tip?</div>
                    <div class="d-flex flex-wrap ga-3">
                        <v-btn size="x-large" :variant="chosenTip === 0 ? 'flat' : 'outlined'"
                            :color="chosenTip === 0 ? 'primary' : undefined" @click="pickTip(0)">No tip</v-btn>
                        <v-btn v-for="pct in [15, 18, 20]" :key="pct" size="x-large"
                            :variant="chosenTip === pctCents(pct) ? 'flat' : 'outlined'"
                            :color="chosenTip === pctCents(pct) ? 'primary' : undefined"
                            @click="pickTip(pctCents(pct))">{{ pct }}% · {{ money(pctCents(pct)) }}</v-btn>
                        <v-btn size="x-large" :variant="customOpen ? 'flat' : 'outlined'"
                            :color="customOpen ? 'primary' : undefined" @click="customOpen = !customOpen">Custom</v-btn>
                    </div>
                    <div v-if="customOpen" class="d-flex ga-3 mt-4 align-center">
                        <v-text-field v-model.number="customDollars" type="number" min="0" step="0.50" prefix="$"
                            label="Tip amount" density="comfortable" hide-details style="max-width: 200px" />
                        <v-btn size="large" color="primary" @click="pickTip(Math.max(0, Math.round((customDollars || 0) * 100)))">Apply</v-btn>
                    </div>
                    <div v-if="tipConfirmed" class="text-body-1 text-success mt-3">
                        <v-icon size="small">mdi-check-circle</v-icon>
                        {{ chosenTip ? `Tip added: ${money(chosenTip)}` : 'No tip — all set!' }} You can still change it.
                    </div>
                    <div v-if="tipError" class="text-body-1 text-error mt-3">{{ tipError }}</div>
                </template>

                <div v-if="state.status === 'processing'" class="d-flex align-center justify-center ga-3 mt-4">
                    <v-progress-circular indeterminate size="28" />
                    <span class="text-h6">Processing payment…</span>
                </div>
            </div>
        </template>

        <!-- Done / thank you -->
        <div v-else class="d-flex flex-column flex-grow-1 align-center justify-center pa-8 text-center">
            <v-icon color="success" size="96">mdi-check-circle</v-icon>
            <div class="text-h3 font-weight-bold mt-4">Thank you!</div>
            <div v-if="state.orderNumber != null" class="text-h4 mt-3">Order #{{ state.orderNumber }}</div>
            <div class="text-h6 text-medium-emphasis mt-2">We'll call your number when it's ready.</div>
        </div>

        <div v-if="stale" class="cfd-stale text-caption">Reconnecting…</div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { ConcessionService, type DisplayState } from '@/services/ConcessionService'
import { branding } from '@/stores/branding'
import { setHomeScreenIcon } from '@/helpers/HomeScreenIcon'

const STORAGE_KEY = 'concessionDisplayId'
const svc = new ConcessionService()
const loading = ref(true)
const displayId = ref<string | null>(localStorage.getItem(STORAGE_KEY))
const pairCode = ref<string | null>(null)
const state = ref<DisplayState | null>(null)
const stale = ref(false)
let failures = 0
let timer: number | undefined

// The customer's local tip pick (server echo may lag a poll behind). The visible tip prefers the
// local pick during the tip step so taps feel instant.
const chosenTip = ref<number | null>(null)
const tipConfirmed = ref(false)
const tipError = ref('')
const customOpen = ref(false)
const customDollars = ref<number | null>(null)

const displayTipCents = computed(() => {
    if (state.value?.status === 'tip' && chosenTip.value !== null) return chosenTip.value
    return state.value?.tipCents ?? 0
})

function money(cents: number) { return `$${(cents / 100).toFixed(2)}` }
function pctCents(pct: number) { return Math.round((state.value?.subtotal ?? 0) * pct / 100) }

async function pickTip(cents: number) {
    if (!displayId.value) return
    tipError.value = ''
    customOpen.value = cents > 0 && customOpen.value && cents === Math.round((customDollars.value || 0) * 100)
    chosenTip.value = cents
    try {
        await svc.setDisplayTip(displayId.value, cents)
        tipConfirmed.value = true
    } catch (err: any) {
        tipConfirmed.value = false
        tipError.value = err.response?.data?.error || 'Could not save the tip. Please tell the cashier your tip amount.'
    }
}

// Ensure this tablet has a display session, reusing the stored one when it still exists. Only a
// real 404 (stale/foreign id, e.g. tenant switch) mints a new session — a transient network error
// must NOT, or a blip would silently break the POS pairing to this tablet.
async function ensureSession() {
    if (displayId.value) {
        try {
            const d = (await svc.display(displayId.value) as any).data.data
            pairCode.value = d.pairCode
            applyServerState(d)
            return
        } catch (err: any) {
            if (err.response?.status !== 404) throw err
        }
    }
    const d = (await svc.createDisplay() as any).data.data
    displayId.value = d.id
    pairCode.value = d.pairCode
    localStorage.setItem(STORAGE_KEY, d.id)
}

function applyServerState(d: { stateJson: string | null }) {
    let next: DisplayState | null = null
    if (d.stateJson) {
        try { next = JSON.parse(d.stateJson) } catch { next = null }
    }
    const prevStatus = state.value?.status
    state.value = next
    // Entering the tip step (or a brand-new order) resets the local tip pick.
    if (next?.status !== prevStatus && (next?.status === 'tip' || next?.status === 'building' || !next)) {
        chosenTip.value = null
        tipConfirmed.value = false
        tipError.value = ''
        customOpen.value = false
        customDollars.value = null
    }
}

async function poll() {
    if (!displayId.value) return
    try {
        const d = (await svc.display(displayId.value) as any).data.data
        applyServerState(d)
        failures = 0
        stale.value = false
    } catch (err: any) {
        // Session row vanished: forget it so the next tick mints a fresh one (with a new pair code).
        if (err.response?.status === 404) {
            displayId.value = null
            pairCode.value = null
            localStorage.removeItem(STORAGE_KEY)
            return
        }
        // Transient outages keep showing the last good state; surface a hint if it persists.
        if (++failures >= 4) stale.value = true
    }
}

onMounted(async () => {
    setHomeScreenIcon({ title: `${branding.displayName || 'RidePass'} Display`, iconUrl: '/icon-menu.png', startPath: '/Admin/ConcessionDisplay' })
    try {
        await ensureSession()
    } catch {
        // Couldn't reach the server at all; polling below keeps retrying via ensureSession.
        stale.value = true
    }
    loading.value = false
    timer = window.setInterval(async () => {
        if (!displayId.value || !pairCode.value) {
            try { await ensureSession(); stale.value = false } catch { /* retry next tick */ }
            return
        }
        await poll()
    }, 1500)
})
onUnmounted(() => { if (timer) window.clearInterval(timer) })
</script>

<style scoped>
.cfd { min-height: 100vh; }
.cfd-lines { overflow-y: auto; min-height: 0; }
.cfd-line { padding: 10px 0; border-bottom: 1px solid rgba(128, 128, 128, 0.12); }
.pair-code { position: fixed; bottom: 16px; left: 0; right: 0; text-align: center; font-size: 1rem; }
.cfd-stale { position: fixed; top: 8px; right: 12px; opacity: 0.6; }
</style>
