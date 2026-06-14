<template>
    <div>
        <v-progress-circular v-if="loading" indeterminate color="primary"></v-progress-circular>

        <v-card v-else-if="!event" class="pa-6 text-center" variant="outlined">
            <p class="text-body-2 mb-3">We couldn't find that event.</p>
            <v-btn color="primary" to="/Events">Back to Events</v-btn>
        </v-card>

        <template v-else>
            <v-stepper v-if="!completed" v-model="step" color="primary" hide-actions>
                <v-stepper-header>
                    <template v-for="(item, idx) in stepperItems" :key="item.value">
                        <v-divider v-if="idx > 0"></v-divider>
                        <v-stepper-item :value="item.value" :title="item.title">
                            <template #icon>
                                <span class="text-body-2 font-weight-bold">{{ idx + 1 }}</span>
                            </template>
                        </v-stepper-item>
                    </template>
                </v-stepper-header>
                <v-stepper-window>
                    <!-- Step 1: pick gate fees / spectator add-ons -->
                    <v-stepper-window-item value="passes">
                        <v-card class="mb-4 pa-4">
                            <v-card-text>
                                <p v-if="spectatorExtras.length === 0" class="text-medium-emphasis">
                                    No spectator passes are configured for this event.
                                </p>
                                <ExtrasPicker v-else
                                    :extras="spectatorExtras"
                                    v-model="extraSelections" />
                                <v-alert v-if="totalUnits === 0"
                                    type="warning" variant="tonal" density="compact" class="mt-3">
                                    Add at least one Gate Fee to continue.
                                </v-alert>
                                <div class="d-flex mt-4">
                                    <v-spacer></v-spacer>
                                    <v-btn color="primary" :disabled="totalUnits === 0" @click="step = 'buyer'">
                                        Continue
                                    </v-btn>
                                </div>
                            </v-card-text>
                        </v-card>
                    </v-stepper-window-item>

                    <!-- Step 2: buyer info -->
                    <v-stepper-window-item value="buyer">
                        <v-card class="mb-4 pa-4">
                            <v-card-title>Your info</v-card-title>
                            <v-card-text>
                                <p class="text-body-2 text-medium-emphasis mb-3">
                                    We'll email a receipt and your gate QR codes to this address.
                                </p>
                                <v-row>
                                    <v-col cols="12" sm="6">
                                        <v-text-field v-model="purchaserName" label="Full name" density="compact"></v-text-field>
                                    </v-col>
                                    <v-col cols="12" sm="6">
                                        <v-text-field v-model="purchaserEmail" type="email" label="Email" density="compact"
                                            @blur="onEmailBlur"></v-text-field>
                                    </v-col>
                                </v-row>
                                <v-alert v-if="waiverActive && purchaserHasSigned" type="success" variant="tonal" density="compact" class="mt-2">
                                    We found a signed waiver on file for this email — you won't need to re-sign for yourself.
                                    Children attending will still need a separate signature.
                                </v-alert>
                                <p v-if="waiverActive" class="text-caption text-medium-emphasis mt-3">
                                    This event requires a spectator waiver. You'll sign it on the next step
                                    for each attendee you're bringing.
                                </p>
                                <div class="d-flex mt-4">
                                    <v-btn variant="text" @click="step = 'passes'">Back</v-btn>
                                    <v-spacer></v-spacer>
                                    <v-btn color="primary" :disabled="!canAdvanceFromBuyer" @click="advanceToSpectators">
                                        Continue
                                    </v-btn>
                                </div>
                            </v-card-text>
                        </v-card>
                    </v-stepper-window-item>

                    <!-- Step 3: per-spectator info + waivers -->
                    <v-stepper-window-item value="spectators">
                        <v-card class="mb-4 pa-4">
                            <v-card-title>Spectators ({{ totalUnits }})</v-card-title>
                            <v-card-text>
                                <p class="text-body-2 text-medium-emphasis mb-3">
                                    Tell us who's attending. Each spectator pass needs an attendee on the waiver.
                                </p>
                                <v-card v-for="(spec, idx) in spectators" :key="idx"
                                    variant="outlined" class="pa-3 mb-3">
                                    <div class="text-subtitle-2 mb-2">Spectator #{{ idx + 1 }}</div>
                                    <v-row>
                                        <v-col cols="12" sm="4">
                                            <v-text-field v-model="spec.firstName" label="First name" density="compact"
                                                :error="!!spec.showFieldErrors && !spec.firstName.trim()"></v-text-field>
                                        </v-col>
                                        <v-col cols="12" sm="4">
                                            <v-text-field v-model="spec.lastName" label="Last name" density="compact"
                                                :error="!!spec.showFieldErrors && !spec.lastName.trim()"></v-text-field>
                                        </v-col>
                                        <v-col cols="12" sm="4">
                                            <v-text-field v-model="spec.birthdate" type="date" :max="todayIso"
                                                label="Birthdate" density="compact"
                                                :error="!!spec.showFieldErrors && !spec.birthdate"></v-text-field>
                                        </v-col>
                                    </v-row>

                                    <template v-if="waiverActive && needsSignature(spec)">
                                        <v-alert v-if="isMinor(spec)" type="info" variant="tonal" density="compact" class="mb-2">
                                            Spectator is under 18 — a parent or guardian must sign on their behalf.
                                        </v-alert>
                                        <v-alert v-else-if="isSelfMatch(spec) && purchaserHasSigned" type="success"
                                            variant="tonal" density="compact" class="mb-2">
                                            Waiver on file for this person — no signature needed.
                                        </v-alert>
                                        <v-row v-if="isMinor(spec)" class="mt-1">
                                            <v-col cols="12" sm="6">
                                                <v-text-field v-model="spec.parentName"
                                                    label="Parent / guardian name" density="compact"></v-text-field>
                                            </v-col>
                                            <v-col cols="12" sm="6">
                                                <PhoneField v-model="spec.parentPhone"
                                                    label="Parent / guardian phone" density="compact" />
                                            </v-col>
                                        </v-row>
                                        <div v-if="!(isSelfMatch(spec) && purchaserHasSigned && !isMinor(spec))"
                                            class="agreement-block mt-2">
                                            <div class="d-flex align-start ga-2">
                                                <v-checkbox v-model="spec.waiverAgreed"
                                                    density="compact" hide-details color="primary"
                                                    style="flex: 0 0 auto; margin-top: -6px"></v-checkbox>
                                                <div class="text-body-2 agreement-text">
                                                    I have read and agree to the
                                                    <a href="javascript:void(0)" class="waiver-link"
                                                        @click.stop.prevent="waiverDialog = true">waiver</a>.
                                                </div>
                                            </div>
                                            <div class="text-caption text-medium-emphasis mb-1 mt-2">
                                                {{ isMinor(spec) ? 'Parent signs below' : 'Sign below' }}
                                            </div>
                                            <SignaturePad v-model="spec.signatureDataUrl"
                                                :disabled="!isSpectatorReady(spec) || !spec.waiverAgreed"
                                                disabled-placeholder="Check the box above to sign" />
                                            <!-- Click-shield: covers the checkbox + signature pad until name and
                                                 birthdate are filled. The waiver link sits above it via z-index so
                                                 the rider can still read the waiver. Clicking anything underneath
                                                 the shield turns the empty identity fields red. -->
                                            <div v-if="!isSpectatorReady(spec)" class="agreement-shield"
                                                @click="flagMissingFields(spec)"
                                                @pointerdown="flagMissingFields(spec)"></div>
                                        </div>
                                    </template>
                                </v-card>

                                <div v-if="errorMessage" class="text-error text-caption mt-2">{{ errorMessage }}</div>

                                <div class="d-flex mt-4">
                                    <v-btn variant="text" @click="step = 'buyer'">Back</v-btn>
                                    <v-spacer></v-spacer>
                                    <v-btn color="primary" :disabled="!canAdvanceFromSpectators" :loading="creating" @click="createIntent">
                                        Continue to Payment
                                    </v-btn>
                                </div>
                            </v-card-text>
                        </v-card>
                    </v-stepper-window-item>

                    <!-- Step 4: payment -->
                    <v-stepper-window-item value="payment">
                        <v-card class="mb-4 pa-4">
                            <v-card-title>Payment</v-card-title>
                            <v-card-text>
                                <div class="mb-3">
                                    Total: <strong>${{ (totalCents / 100).toFixed(2) }}</strong>
                                </div>
                                <div :id="paymentElementId" class="mb-4"></div>
                                <v-btn color="primary" :loading="paying" :disabled="!stripeReady" @click="pay">
                                    Pay ${{ (totalCents / 100).toFixed(2) }}
                                </v-btn>
                                <div v-if="paymentError" class="text-error mt-3">{{ paymentError }}</div>
                            </v-card-text>
                        </v-card>
                    </v-stepper-window-item>
                </v-stepper-window>
            </v-stepper>

            <v-card v-if="!completed" class="mt-4 pa-3" variant="outlined">
                <div class="text-overline text-medium-emphasis mb-2">Order Summary</div>
                <v-table density="compact" class="bg-transparent">
                    <tbody>
                        <tr v-for="line in cartLines" :key="line.productId + (line.variantId ?? '')">
                            <td>{{ line.name }} × {{ line.quantity }}</td>
                            <td class="text-right">${{ ((line.priceCents * line.quantity) / 100).toFixed(2) }}</td>
                        </tr>
                        <tr>
                            <td><strong>Total</strong></td>
                            <td class="text-right"><strong>${{ (totalCents / 100).toFixed(2) }}</strong></td>
                        </tr>
                    </tbody>
                </v-table>
            </v-card>

            <v-card v-if="completed" class="mb-4 pa-4">
                <v-alert type="success" class="mb-4">
                    Spectator passes purchased! We've emailed your QR codes to {{ purchaserEmail }}.
                </v-alert>
            </v-card>

            <v-dialog v-model="waiverDialog" max-width="720" scrollable>
                <v-card v-if="waiver">
                    <v-card-title class="d-flex align-center">
                        <span>{{ waiver.title }}</span>
                        <v-spacer></v-spacer>
                        <v-btn icon="mdi-close" variant="text" size="small" @click="waiverDialog = false"></v-btn>
                    </v-card-title>
                    <v-card-subtitle class="mb-2">Version {{ waiver.version }}</v-card-subtitle>
                    <v-card-text>
                        <RichTextView :html="waiver.body" />
                    </v-card-text>
                    <v-card-actions>
                        <v-spacer></v-spacer>
                        <v-btn color="primary" @click="waiverDialog = false">Close</v-btn>
                    </v-card-actions>
                </v-card>
            </v-dialog>

            <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000" location="top">{{ snackbarText }}</v-snackbar>
        </template>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch, nextTick } from 'vue'
import { EventService, type EventDto, type EligibleExtra } from '@/services/EventService'
import { WaiverService, type WaiverDto } from '@/services/WaiverService'
import { SpectatorService, type SpectatorEntry } from '@/services/SpectatorService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'
import ExtrasPicker, { type ExtraSelection } from '@/components/ExtrasPicker.vue'
import PhoneField from '@/components/PhoneField.vue'
import SignaturePad from '@/components/SignaturePad.vue'
import RichTextView from '@/components/RichTextView.vue'

// Reusable spectator checkout. Mounted both by the /BuySpectator route page and
// inline on the public Event landing page so the rider never leaves the event.
const props = defineProps<{
    eventId: string
    // When the parent already has the event (e.g. the Event landing page), pass it
    // in to skip the refetch. We still resolve the spectator waiver ourselves.
    event?: EventDto | null
}>()
const emit = defineEmits<{ (e: 'completed'): void }>()

const eventService = new EventService()
const waiverService = new WaiverService()
const spectatorService = new SpectatorService()

const eventId = props.eventId
const event = ref<EventDto | null>(null)
const loading = ref(true)
const todayIso = new Date().toISOString().slice(0, 10)

// Gate Fee identification: prefer the seeded `kind === 'gate_fee'` slug, but
// also recognise products literally named "Gate Fee" (case-insensitive) so a
// hand-edited / renamed catalog still works.
function isGateFee(p: { kind: string; name: string }): boolean {
    if (p.kind === 'gate_fee') return true
    return p.name.trim().toLowerCase() === 'gate fee'
}

// All add-ons offered at this event are treated as "spectator passes" — Gate Fees
// for sure, but tracks may also offer e.g. parking that goes through this flow.
// Gate Fees always lead the list since they're the primary purchase here.
const spectatorExtras = computed<EligibleExtra[]>(() => {
    const extras = [...(event.value?.eligibleExtras ?? [])]
    extras.sort((a, b) => {
        const aGate = isGateFee(a) ? 0 : 1
        const bGate = isGateFee(b) ? 0 : 1
        if (aGate !== bGate) return aGate - bGate
        return a.name.localeCompare(b.name)
    })
    return extras
})
const extraSelections = ref<ExtraSelection[]>([])

// Only Gate Fee units count as "spectators on the waiver". Other add-ons
// (camping, parking, merch) ride along on the same purchase but don't require
// a per-attendee signature.
const gateFeeProductIds = computed(() => new Set(
    spectatorExtras.value.filter(isGateFee).map(p => p.productId)))
const totalUnits = computed(() => extraSelections.value
    .filter(s => gateFeeProductIds.value.has(s.productId))
    .reduce((sum, s) => sum + s.quantity, 0))
const cartLines = computed(() => extraSelections.value
    .filter(s => s.quantity > 0)
    .map(s => {
        const product = spectatorExtras.value.find(p => p.productId === s.productId)
        const variant = s.variantId ? product?.variants.find(v => v.id === s.variantId) ?? null : null
        const priceCents = variant?.priceCents ?? product?.priceCents ?? 0
        return {
            productId: s.productId,
            variantId: s.variantId,
            name: product?.name ?? '',
            priceCents,
            quantity: s.quantity,
        }
    }))
const totalCents = computed(() => cartLines.value.reduce((sum, l) => sum + l.priceCents * l.quantity, 0))

// Waiver state
const waiver = ref<WaiverDto | null>(null)
const waiverActive = computed(() => waiver.value !== null)

// Buyer
const purchaserName = ref('')
const purchaserEmail = ref('')
const purchaserHasSigned = ref(false)

// Stepper
type StepKey = 'passes' | 'buyer' | 'spectators' | 'payment'
const step = ref<StepKey>('passes')
const stepperItems = computed<{ title: string; value: StepKey }[]>(() => {
    const items: { title: string; value: StepKey }[] = [
        { title: 'Passes', value: 'passes' },
        { title: 'Your info', value: 'buyer' },
    ]
    items.push({ title: 'Spectators', value: 'spectators' })
    items.push({ title: 'Payment', value: 'payment' })
    return items
})

// Per-spectator state. Resized to match totalUnits whenever the cart changes.
// `waiverAgreed` is the local "I have read and agree" acknowledgement; it gates
// the signature pad and the Continue button, but isn't sent to the server (the
// signature itself is the persisted proof). `showFieldErrors` flips to true the
// first time the rider tries to interact with the agreement/signature with
// empty name+birthdate fields — that turns the empty fields red as a hint.
interface SpectatorRow extends SpectatorEntry {
    waiverAgreed?: boolean
    showFieldErrors?: boolean
}
const spectators = ref<SpectatorRow[]>([])

watch(totalUnits, (n) => {
    if (n > spectators.value.length) {
        for (let i = spectators.value.length; i < n; i++) {
            spectators.value.push({
                firstName: '', lastName: '', birthdate: '',
                waiverAgreed: false, showFieldErrors: false,
            })
        }
    } else if (n < spectators.value.length) {
        spectators.value.length = n
    }
})

// Single shared modal for viewing the waiver text. The link is always
// available — riders can review the waiver before filling in name/birthdate.
const waiverDialog = ref(false)

// Name + birthdate are required before the spectator can agree to a waiver
// (we don't want a signature attached to an unidentified attendee).
function isSpectatorReady(s: SpectatorRow): boolean {
    return s.firstName.trim().length > 0
        && s.lastName.trim().length > 0
        && !!s.birthdate
}

// Click-shield handler: when the rider clicks the agreement checkbox or the
// signature pad area with empty fields above, surface the validation by
// flipping `showFieldErrors`. The empty fields turn red; filled fields stay
// clean because the per-field condition includes the not-empty check.
function flagMissingFields(s: SpectatorRow) {
    s.showFieldErrors = true
}

const errorMessage = ref('')

const canAdvanceFromBuyer = computed(() =>
    purchaserName.value.trim().length > 0
    && /\S+@\S+/.test(purchaserEmail.value.trim()))

function isMinor(s: SpectatorRow): boolean {
    if (!s.birthdate) return false
    const dob = new Date(s.birthdate)
    if (isNaN(dob.getTime())) return false
    const eighteen = new Date()
    eighteen.setFullYear(eighteen.getFullYear() - 18)
    return dob > eighteen
}

function isSelfMatch(s: SpectatorRow): boolean {
    if (!purchaserName.value || !s.firstName || !s.lastName) return false
    const combined = `${s.firstName} ${s.lastName}`.trim().toLowerCase()
    return combined === purchaserName.value.trim().toLowerCase()
}

// True when this spectator row needs a fresh signature collected. Adult buyers
// who already have a self-signed waiver on file (matched by purchaser email)
// can skip — every minor and every non-self adult must sign.
function needsSignature(s: SpectatorRow): boolean {
    if (!waiverActive.value) return false
    if (isMinor(s)) return true
    if (isSelfMatch(s) && purchaserHasSigned.value) return false
    return true
}

const canAdvanceFromSpectators = computed(() => {
    if (!event.value) return false
    if (totalUnits.value === 0) return false
    if (spectators.value.length !== totalUnits.value) return false
    for (const s of spectators.value) {
        if (!s.firstName.trim() || !s.lastName.trim() || !s.birthdate) return false
        if (waiverActive.value && needsSignature(s)) {
            if (!s.waiverAgreed) return false
            if (!s.signatureDataUrl) return false
            if (isMinor(s)) {
                if (!s.parentName?.trim()) return false
                if ((s.parentPhone ?? '').replace(/\D/g, '').length < 7) return false
            }
        }
    }
    return true
})

// Stripe / payment
const creating = ref(false)
const paying = ref(false)
const paymentElementId = `spectator-payment-${Math.random().toString(36).slice(2, 8)}`
const clientSecret = ref<string | null>(null)
const stripeReady = ref(false)
const paymentError = ref<string | null>(null)
const completed = ref(false)
let stripe: any = null
let elements: any = null

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(async () => {
    try {
        if (props.event) {
            event.value = props.event
        } else {
            const r = await eventService.getPublic(props.eventId)
            event.value = (r.data as any).data as EventDto
        }
        if (event.value?.requiresSpectatorWaiver) {
            const wId = event.value.spectatorWaiverId
            const wr = wId
                ? await waiverService.getById(wId)
                : await waiverService.getActive().catch(() => ({ data: { data: null } }))
            waiver.value = (wr.data as any).data ?? null
        }
    } finally {
        loading.value = false
    }
})

async function onEmailBlur() {
    if (!waiver.value || !purchaserEmail.value.trim()) {
        purchaserHasSigned.value = false
        return
    }
    try {
        const r = await spectatorService.checkSignature(waiver.value.id, purchaserEmail.value.trim())
        purchaserHasSigned.value = !!(r.data as any).data?.hasSigned
    } catch {
        purchaserHasSigned.value = false
    }
}

function advanceToSpectators() {
    // Pre-populate the first spectator with the buyer when no entries exist yet.
    if (spectators.value.length > 0 && !spectators.value[0].firstName && !spectators.value[0].lastName) {
        const parts = purchaserName.value.trim().split(/\s+/)
        if (parts.length >= 2) {
            spectators.value[0].firstName = parts[0]
            spectators.value[0].lastName = parts.slice(1).join(' ')
        }
    }
    step.value = 'spectators'
}

async function createIntent() {
    errorMessage.value = ''
    creating.value = true
    try {
        const items = extraSelections.value
            .filter(s => s.quantity > 0)
            .map(s => ({ productId: s.productId, quantity: s.quantity, variantId: s.variantId ?? null }))
        const payload = {
            eventId,
            purchaserEmail: purchaserEmail.value.trim(),
            purchaserName: purchaserName.value.trim(),
            items,
            spectators: spectators.value.map(s => ({
                firstName: s.firstName.trim(),
                lastName: s.lastName.trim(),
                birthdate: s.birthdate,
                signatureDataUrl: needsSignature(s) ? s.signatureDataUrl ?? null : null,
                parentName: needsSignature(s) && isMinor(s) ? s.parentName?.trim() ?? null : null,
                parentPhone: needsSignature(s) && isMinor(s) ? s.parentPhone?.trim() ?? null : null,
            })),
        }
        const r = await spectatorService.buy(payload)
        const data = (r.data as any).data
        clientSecret.value = data.clientSecret
        step.value = 'payment'
        await nextTick()
        await mountPaymentElement()
    } catch (err: any) {
        errorMessage.value = err.response?.data?.error || 'Could not start payment.'
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
    pe.mount(`#${paymentElementId}`)
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
            completed.value = true
            emit('completed')
        }
    } catch (err: any) {
        paymentError.value = err?.message || 'Payment failed.'
    } finally {
        paying.value = false
    }
}
</script>

<style scoped>
.agreement-block { position: relative; }
.agreement-shield {
    position: absolute;
    inset: 0;
    z-index: 5;
    cursor: pointer;
    background: transparent;
}
/* Keep the waiver link clickable through the shield. */
.waiver-link { position: relative; z-index: 6; }
.agreement-text { line-height: 32px; }
</style>
