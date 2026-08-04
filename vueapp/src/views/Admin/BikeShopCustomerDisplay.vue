<template>
    <div class="scfd d-flex flex-column">
        <div v-if="loading" class="d-flex flex-grow-1 align-center justify-center">
            <v-progress-circular indeterminate size="64" />
        </div>

        <!-- Idle / welcome (carries the pair code so the cashier can pair this tablet) -->
        <div v-else-if="!state || state.status === 'idle'" class="d-flex flex-column flex-grow-1 align-center justify-center pa-8 text-center">
            <v-img v-if="branding.logoUrl" :src="branding.logoUrl" max-height="140" max-width="360" class="mb-6" />
            <div class="text-h3 font-weight-bold mb-2">Welcome to {{ branding.displayName || 'RidePass' }}</div>
            <div class="text-h6 text-medium-emphasis">Bike shop</div>
            <div v-if="pairCode" class="pair-code text-medium-emphasis">
                Display pair code: <strong>{{ pairCode }}</strong>
            </div>
        </div>

        <!-- Charges mirror: read-only view of what the cashier is ringing up -->
        <template v-else-if="state.status === 'charges'">
            <div class="d-flex align-center px-6 py-4">
                <v-img v-if="branding.logoUrl" :src="branding.logoUrl" max-height="48" max-width="140" position="left" />
                <v-spacer />
                <span class="text-h5 font-weight-bold">Your charges</span>
            </div>
            <v-divider />
            <div class="scfd-lines flex-grow-1 px-6 py-4">
                <div v-for="(l, i) in state.lines" :key="i" class="scfd-line">
                    <div class="d-flex justify-space-between align-baseline">
                        <span class="text-h6 font-weight-medium">{{ l.qty > 1 ? `${l.qty}× ` : '' }}{{ l.name }}</span>
                        <span class="text-h6">{{ money(l.lineTotal) }}</span>
                    </div>
                    <div v-if="l.detail" class="text-body-2 text-medium-emphasis">{{ l.detail }}</div>
                </div>
                <div v-if="state.lines.length === 0" class="text-center text-h6 text-medium-emphasis py-12">
                    Your items will appear here.
                </div>
            </div>
            <v-divider />
            <div class="px-6 py-4">
                <div class="d-flex justify-space-between align-center">
                    <span class="text-h5 font-weight-bold">{{ state.totalLabel || 'Subtotal' }}</span>
                    <span class="text-h4 font-weight-bold">{{ money(state.subtotalCents) }}</span>
                </div>
                <div v-if="state.note" class="text-body-2 text-medium-emphasis mt-1">{{ state.note }}</div>
            </div>
        </template>

        <!-- Read + sign: rental agreement or waiver -->
        <template v-else-if="state.status === 'sign' && state.sign">
            <div class="px-6 py-4">
                <span class="text-h5 font-weight-bold">{{ state.sign.title }}</span>
            </div>
            <v-divider />
            <div class="scfd-lines flex-grow-1 px-6 py-4">
                <div class="doc-body text-body-1 mb-6">{{ state.sign.body }}</div>

                <template v-if="submitted">
                    <v-alert type="success" variant="tonal">
                        Thank you! Your signature has been recorded. Please hand the screen back.
                    </v-alert>
                </template>

                <!-- Agreement (rental terms / repair authorization): name + optional email + signature -->
                <template v-else-if="state.sign.docKind !== 'waiver'">
                    <v-text-field v-model="form.signerName" label="Your full name" density="comfortable" hide-details />
                    <v-text-field v-model="form.email" type="email" label="Email (optional)" density="comfortable" hide-details class="mt-4" />
                </template>

                <!-- Waiver: rider details, guardian for minors -->
                <template v-else>
                    <v-row dense>
                        <v-col cols="6"><v-text-field v-model="form.firstName" label="Rider first name" density="comfortable" hide-details /></v-col>
                        <v-col cols="6"><v-text-field v-model="form.lastName" label="Rider last name" density="comfortable" hide-details /></v-col>
                    </v-row>
                    <v-text-field v-model="form.email" type="email" label="Email (optional)" density="comfortable" hide-details class="mt-4" />
                    <v-text-field v-model="form.birthdate" type="date" label="Rider date of birth" density="comfortable" hide-details class="mt-4" />
                    <v-alert v-if="isMinor" type="info" variant="tonal" density="compact" class="mt-4">
                        Riders under 18 need a parent or guardian to sign.
                    </v-alert>
                    <template v-if="isMinor">
                        <v-text-field v-model="form.parentName" label="Parent/guardian name" density="comfortable" hide-details class="mt-4" />
                        <v-text-field v-model="form.parentPhone" label="Parent/guardian phone" density="comfortable" hide-details class="mt-4" />
                    </template>
                </template>

                <template v-if="!submitted">
                    <div class="text-subtitle-1 font-weight-bold mt-6 mb-1">
                        {{ state.sign.docKind === 'waiver' && isMinor ? 'Parent/guardian signature' : 'Signature' }}
                    </div>
                    <SignaturePad v-model="signatureDataUrl" :height="220" :disabled="sending" />
                    <div v-if="signError" class="text-error text-body-2 mt-2">{{ signError }}</div>
                    <v-btn block size="x-large" color="primary" class="mt-4" :loading="sending"
                        :disabled="!canSubmit" @click="submitSignature">Accept and sign</v-btn>
                </template>
            </div>
        </template>

        <!-- Done / thank you -->
        <div v-else class="d-flex flex-column flex-grow-1 align-center justify-center pa-8 text-center">
            <v-icon color="success" size="96">mdi-check-circle</v-icon>
            <div class="text-h3 font-weight-bold mt-4">Thank you!</div>
            <div class="text-h6 text-medium-emphasis mt-2">Enjoy your ride.</div>
        </div>

        <div v-if="stale" class="scfd-stale text-caption">Reconnecting…</div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import dayjs from 'dayjs'
import SignaturePad from '@/components/SignaturePad.vue'
import { BikeShopService, type ShopDisplayState } from '@/services/BikeShopService'
import { branding } from '@/stores/branding'
import { setHomeScreenIcon } from '@/helpers/HomeScreenIcon'

// This tablet's OWN session key; deliberately different from the staff key 'shopPosDisplayId'.
const STORAGE_KEY = 'shopDisplayId'
const svc = new BikeShopService()
const loading = ref(true)
const displayId = ref<string | null>(localStorage.getItem(STORAGE_KEY))
const pairCode = ref<string | null>(null)
const state = ref<ShopDisplayState | null>(null)
const stale = ref(false)
let failures = 0
let timer: number | undefined

// Signing form state, reset whenever a new sign request arrives.
const form = ref({ signerName: '', email: '', firstName: '', lastName: '', birthdate: '', parentName: '', parentPhone: '' })
const signatureDataUrl = ref<string | null>(null)
const sending = ref(false)
const submitted = ref(false)
const signError = ref('')
let activeRequestId: string | null = null

const isMinor = computed(() =>
    !!form.value.birthdate && dayjs().diff(dayjs(form.value.birthdate), 'year') < 18)

const canSubmit = computed(() => {
    if (!signatureDataUrl.value || !state.value?.sign) return false
    if (state.value.sign.docKind !== 'waiver') return !!form.value.signerName.trim()
    return !!form.value.firstName.trim() && !!form.value.lastName.trim()
        && (!isMinor.value || !!form.value.parentName.trim())
})

function money(cents: number) { return `${cents < 0 ? '-' : ''}$${(Math.abs(cents) / 100).toFixed(2)}` }

async function submitSignature() {
    if (!displayId.value || !state.value?.sign || !canSubmit.value) return
    sending.value = true
    signError.value = ''
    const sign = state.value.sign
    try {
        await svc.respondShopDisplay(displayId.value, sign.docKind !== 'waiver'
            ? {
                requestId: sign.requestId, signatureDataUrl: signatureDataUrl.value!,
                signerName: form.value.signerName.trim(), signerEmail: form.value.email.trim() || null,
            }
            : {
                requestId: sign.requestId, signatureDataUrl: signatureDataUrl.value!,
                firstName: form.value.firstName.trim(), lastName: form.value.lastName.trim(),
                email: form.value.email.trim() || null, birthdate: form.value.birthdate || null,
                signedByParent: isMinor.value,
                parentName: form.value.parentName.trim() || null,
                parentPhone: form.value.parentPhone.trim() || null,
            })
        submitted.value = true
    } catch (err: any) {
        signError.value = err.response?.data?.error || 'Could not send the signature. Please tell the staff member.'
    } finally { sending.value = false }
}

// Only a real 404 mints a new session; a network blip must not silently break the pairing.
async function ensureSession() {
    if (displayId.value) {
        try {
            const d = (await svc.shopDisplay(displayId.value) as any).data.data
            pairCode.value = d.pairCode
            applyServerState(d)
            return
        } catch (err: any) {
            if (err.response?.status !== 404) throw err
        }
    }
    const d = (await svc.createShopDisplay() as any).data.data
    displayId.value = d.id
    pairCode.value = d.pairCode
    localStorage.setItem(STORAGE_KEY, d.id)
}

let lastStateJson: string | null = null
function applyServerState(d: { stateJson: string | null }) {
    if (d.stateJson === lastStateJson) return   // no churn while the customer is mid-signature
    lastStateJson = d.stateJson
    let next: ShopDisplayState | null = null
    if (d.stateJson) {
        try { next = JSON.parse(d.stateJson) } catch { next = null }
    }
    state.value = next
    // A NEW sign request (different requestId) resets the form with the staff-provided prefills.
    const reqId = next?.status === 'sign' ? next.sign?.requestId ?? null : null
    if (reqId !== activeRequestId) {
        activeRequestId = reqId
        submitted.value = false
        sending.value = false
        signError.value = ''
        signatureDataUrl.value = null
        const name = next?.sign?.signerName ?? ''
        const parts = name.trim().split(/\s+/)
        form.value = {
            signerName: name,
            email: next?.sign?.signerEmail ?? '',
            firstName: parts[0] ?? '', lastName: parts.slice(1).join(' '),
            birthdate: '', parentName: '', parentPhone: '',
        }
    }
}

async function poll() {
    if (!displayId.value) return
    try {
        const d = (await svc.shopDisplay(displayId.value) as any).data.data
        applyServerState(d)
        failures = 0
        stale.value = false
    } catch (err: any) {
        // Session row vanished: forget it so the next tick mints a fresh one (new pair code).
        if (err.response?.status === 404) {
            displayId.value = null
            pairCode.value = null
            localStorage.removeItem(STORAGE_KEY)
            return
        }
        if (++failures >= 4) stale.value = true
    }
}

onMounted(async () => {
    setHomeScreenIcon({ title: `${branding.displayName || 'RidePass'} Shop Display`, iconUrl: '/icon-menu.png', startPath: '/Admin/BikeShop/CustomerDisplay' })
    try { await ensureSession() } catch { stale.value = true }
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
.scfd { min-height: 100vh; }
.scfd-lines { overflow-y: auto; min-height: 0; }
.scfd-line { padding: 10px 0; border-bottom: 1px solid rgba(128, 128, 128, 0.12); }
.doc-body { white-space: pre-wrap; }
.pair-code { position: fixed; bottom: 16px; left: 0; right: 0; text-align: center; font-size: 1rem; }
.scfd-stale { position: fixed; top: 8px; right: 12px; opacity: 0.6; }
</style>
