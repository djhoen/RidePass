<template>
    <div class="evt-checkout">
        <!-- ── 1. Select entries ─────────────────────────────────────────── -->
        <template v-if="step === 'select'">
            <h2 class="text-h5 font-weight-bold font-display mb-1">Buy Entry</h2>

            <div v-if="raceTiers.length" class="mb-4">
                <div class="evt-group-label mb-2">Race Classes</div>
                <div v-for="t in raceTiers" :key="t.id" class="evt-line pl-4">
                    <div>
                        <div class="font-weight-medium">{{ t.name }}</div>
                        <div class="text-caption text-medium-emphasis">{{ priceLabel(t.priceCents) }}<span v-if="soldOut(t)"> · Sold out</span><span v-else-if="stepHint(t)"> · {{ stepHint(t) }}</span></div>
                    </div>
                    <div class="d-flex align-center ga-1">
                        <v-btn icon="mdi-minus" size="x-small" variant="tonal" :disabled="(qty[t.id] || 0) <= 0" @click="bump(t, -1)"></v-btn>
                        <span class="evt-qty">{{ qty[t.id] || 0 }}</span>
                        <v-btn icon="mdi-plus" size="x-small" variant="tonal" :disabled="!canAdd(t)" @click="bump(t, 1)"></v-btn>
                    </div>
                </div>
            </div>

            <!-- Number of riders: only for a race when more than one class entry is in
                 the cart (a rider may take several classes). One rider gate fee per rider. -->
            <div v-if="showRiderCount" class="evt-riders mb-4 pa-3">
                <div class="d-flex align-center justify-space-between">
                    <div>
                        <div class="font-weight-medium">How many riders?</div>
                        <div class="text-caption text-medium-emphasis">One gate fee per rider. A rider can enter several classes.</div>
                    </div>
                    <div class="d-flex align-center ga-1">
                        <v-btn icon="mdi-minus" size="x-small" variant="tonal" :disabled="riderCount <= maxClassQty" @click="riderCount = Math.max(maxClassQty, riderCount - 1)"></v-btn>
                        <span class="evt-qty">{{ riderCount }}</span>
                        <v-btn icon="mdi-plus" size="x-small" variant="tonal" :disabled="riderCount >= totalRaceQty" @click="riderCount = Math.min(totalRaceQty, riderCount + 1)"></v-btn>
                    </div>
                </div>
            </div>

            <div v-if="riderGateTiers.length" class="mb-4">
                <div class="evt-group-label mb-2">
                    {{ riderGateLabel }} <span v-if="requiredRiderGate" class="text-error">*</span>
                </div>
                <div v-for="t in riderGateTiers" :key="t.id" class="evt-line pl-4">
                    <div>
                        <div class="font-weight-medium">{{ t.name }}</div>
                        <div class="text-caption text-medium-emphasis">{{ priceLabel(t.priceCents) }}<span v-if="soldOut(t)"> · Sold out</span><span v-else-if="stepHint(t)"> · {{ stepHint(t) }}</span></div>
                    </div>
                    <div class="d-flex align-center ga-1">
                        <v-btn icon="mdi-minus" size="x-small" variant="tonal" :disabled="(qty[t.id] || 0) <= 0" @click="bump(t, -1)"></v-btn>
                        <span class="evt-qty">{{ qty[t.id] || 0 }}</span>
                        <v-btn icon="mdi-plus" size="x-small" variant="tonal" :disabled="!canAdd(t)" @click="bump(t, 1)"></v-btn>
                    </div>
                </div>
                <p v-if="riderGateHint" class="text-caption text-error mt-1">{{ riderGateHint }}</p>
            </div>

            <div v-if="spectatorGateTiers.length" class="mb-4">
                <div class="evt-group-label mb-2">{{ spectatorGateLabel }}</div>
                <div v-for="t in spectatorGateTiers" :key="t.id" class="evt-line pl-4">
                    <div>
                        <div class="font-weight-medium">{{ t.name }}</div>
                        <div class="text-caption text-medium-emphasis">{{ priceLabel(t.priceCents) }}<span v-if="soldOut(t)"> · Sold out</span><span v-else-if="stepHint(t)"> · {{ stepHint(t) }}</span></div>
                    </div>
                    <div class="d-flex align-center ga-1">
                        <v-btn icon="mdi-minus" size="x-small" variant="tonal" :disabled="(qty[t.id] || 0) <= 0" @click="bump(t, -1)"></v-btn>
                        <span class="evt-qty">{{ qty[t.id] || 0 }}</span>
                        <v-btn icon="mdi-plus" size="x-small" variant="tonal" :disabled="!canAdd(t)" @click="bump(t, 1)"></v-btn>
                    </div>
                </div>
            </div>

            <template v-if="hasSelection">
                <!-- Break out the service fee so the buyer sees what the extra cents are
                     (collapses to a single Total when the track absorbs the whole fee). -->
                <template v-if="serviceFeeCents > 0">
                    <div class="d-flex align-center justify-space-between text-body-2 text-medium-emphasis mb-1">
                        <span>Subtotal</span>
                        <span>{{ priceLabel(estTotalCents) }}</span>
                    </div>
                    <div class="d-flex align-center justify-space-between text-body-2 text-medium-emphasis mb-1">
                        <span>Service fee</span>
                        <span>{{ priceLabel(serviceFeeCents) }}</span>
                    </div>
                </template>
                <div v-if="estTaxCents > 0" class="d-flex align-center justify-space-between text-body-2 text-medium-emphasis mb-1">
                    <span>Tax</span>
                    <span>{{ priceLabel(estTaxCents) }}</span>
                </div>
                <div class="d-flex align-center justify-space-between mb-3">
                    <span class="text-h6">Total</span>
                    <span class="text-h6 font-weight-bold">{{ priceLabel(grandTotalCents) }}</span>
                </div>
                <p class="text-caption text-medium-emphasis mb-3">Final total is shown before you pay.</p>
                <v-btn block color="primary" size="large" :disabled="!canContinue" @click="step = 'details'">Continue</v-btn>
            </template>
            <p v-else class="text-body-2 text-medium-emphasis text-center py-2">Add an option above to get started.</p>
        </template>

        <!-- ── 2. Add-ons + your info + codes (post-Continue) ────────────── -->
        <template v-else-if="step === 'details'">
            <div class="d-flex align-center mb-3">
                <h2 class="text-h6 font-weight-bold">Almost done</h2>
                <v-spacer></v-spacer>
                <v-btn variant="text" size="small" prepend-icon="mdi-arrow-left" @click="step = 'select'">Back</v-btn>
            </div>

            <!-- Section 1: Add-ons -->
            <template v-if="addonExtras.length">
                <div class="evt-group-label mb-2">Add-ons</div>
                <ExtrasPicker :extras="addonExtras" v-model="extrasSelection" class="mb-5" />
            </template>

            <!-- Section 2: Your info (+ inline login) -->
            <div class="evt-group-label mb-2">Your info</div>
            <v-text-field v-model="name" label="Full name" density="compact" class="mb-3 mt-4"
                :readonly="isAuthed && name.trim().length > 1"
                :hint="isAuthed && name.trim().length <= 1 ? 'Add your full name to continue.' : ''"
                :persistent-hint="isAuthed && name.trim().length <= 1"></v-text-field>
            <v-text-field v-model="email" type="email" label="Email" density="compact" class="mt-4"
                :readonly="isAuthed" @blur="onEmailBlur"></v-text-field>
            <div v-if="isAuthed" class="text-caption text-medium-emphasis mt-1">
                Not you? <a href="#" class="text-primary" style="text-decoration: underline"
                    @click.prevent="switchUser">Log out</a>
            </div>

            <v-expand-transition>
                <div v-if="showLogin && !isAuthed" class="evt-login mt-3 pa-3">
                    <p class="text-body-2 mb-2">You already have an account. Log in to use your saved info and any season pass.</p>
                    <v-text-field v-model="loginPassword" type="password" label="Password" density="compact"
                        :error-messages="loginError ? [loginError] : []" @keyup.enter="doLogin"></v-text-field>
                    <div class="d-flex ga-2 mt-4">
                        <v-btn :loading="loggingIn" color="primary" size="small" @click="doLogin">Log in</v-btn>
                        <v-btn variant="text" size="small" @click="showLogin = false">Continue as guest</v-btn>
                    </div>
                </div>
            </v-expand-transition>

            <!-- Section 3: Promo or gift code -->
            <div class="evt-group-label mt-5 mb-2">Promo or gift code</div>
            <v-text-field v-model="couponCode" label="Promo code" density="compact" hide-details class="mb-3 mt-4"></v-text-field>
            <v-text-field v-model="giftCardCode" label="Gift card code" density="compact" hide-details class="mt-4"></v-text-field>

            <!-- Order summary -->
            <template v-if="orderLines.length">
                <div class="evt-group-label mt-5 mb-2">Order summary</div>
                <div v-for="(l, i) in orderLines" :key="i" class="d-flex justify-space-between text-body-2 py-1 pl-4">
                    <span>{{ l.label }}<span v-if="l.qty > 1" class="text-medium-emphasis"> × {{ l.qty }}</span></span>
                    <span>{{ priceLabel(l.lineCents) }}</span>
                </div>
                <div v-if="serviceFeeCents > 0" class="d-flex justify-space-between text-body-2 py-1 pl-4">
                    <span class="text-medium-emphasis">Service fee</span>
                    <span class="text-medium-emphasis">{{ priceLabel(serviceFeeCents) }}</span>
                </div>
                <div v-if="estTaxCents > 0" class="d-flex justify-space-between text-body-2 py-1 pl-4">
                    <span class="text-medium-emphasis">Tax</span>
                    <span class="text-medium-emphasis">{{ priceLabel(estTaxCents) }}</span>
                </div>
                <v-divider class="my-2"></v-divider>
                <div class="d-flex justify-space-between">
                    <span class="text-subtitle-1 font-weight-bold">Total</span>
                    <span class="text-subtitle-1 font-weight-bold">{{ priceLabel(grandTotalCents) }}</span>
                </div>
                <p class="text-caption text-medium-emphasis mt-1">Final total is shown before you pay.</p>
            </template>

            <div v-if="errorMessage" class="text-error text-body-2 mt-3">{{ errorMessage }}</div>
            <div class="d-flex align-center ga-2 mt-4">
                <v-btn variant="text" @click="step = 'select'">Back</v-btn>
                <v-spacer></v-spacer>
                <v-btn color="primary" :loading="creating" :disabled="!detailsValid" @click="createIntent">
                    {{ estTotalCents > 0 ? 'Pay Now' : 'Continue' }}
                </v-btn>
            </div>
        </template>

        <!-- ── 3. Payment (skipped for $0) ───────────────────────────────── -->
        <template v-else-if="step === 'payment'">
            <div class="d-flex align-center mb-3">
                <h2 class="text-h6 font-weight-bold">Payment</h2>
                <v-spacer></v-spacer>
                <v-btn variant="text" size="small" prepend-icon="mdi-arrow-left" :disabled="paying" @click="step = 'details'">Back</v-btn>
            </div>
            <div v-if="!branding.stripePublishableKey" class="text-error">Payments aren't configured for this track yet.</div>
            <template v-else>
                <div :id="paymentElementId" class="mb-4"></div>
                <div v-if="paymentError" class="text-error text-body-2 mb-2">{{ paymentError }}</div>
                <v-btn block color="primary" size="large" :loading="paying" :disabled="!stripeReady" @click="pay">
                    Pay {{ priceLabel(chargeCents) }}
                </v-btn>
            </template>
        </template>

        <!-- ── 4. Register (riders + class assignment, post-payment) ─────── -->
        <template v-else-if="step === 'register'">
            <h2 class="text-h6 font-weight-bold mb-1">Almost done</h2>
            <p class="text-body-2 text-medium-emphasis mb-4">
                Payment received. Add rider details{{ anyWaiver ? ' and sign the waiver' : '' }} to finish registration.
            </p>

            <!-- Riders -->
            <div v-for="(rider, ri) in riders" :key="'rider-' + ri" class="evt-reg mb-4 pa-3">
                <div class="font-weight-medium mb-2">Rider {{ riders.length > 1 ? ri + 1 : '' }}</div>
                <v-row dense class="pt-2">
                    <v-col cols="6"><v-text-field v-model="rider.firstName" label="First name" density="compact" hide-details></v-text-field></v-col>
                    <v-col cols="6"><v-text-field v-model="rider.lastName" label="Last name" density="compact" hide-details></v-text-field></v-col>
                </v-row>
                <v-row dense class="mt-4">
                    <v-col cols="6"><v-text-field v-model="rider.birthdate" type="date" label="Date of birth" density="compact" :max="todayIso" hide-details></v-text-field></v-col>
                    <v-col cols="6"><v-text-field v-model="rider.bike" label="Bike (optional)" density="compact" hide-details></v-text-field></v-col>
                </v-row>

                <!-- Emergency contact (when the track requires one) -->
                <v-row v-if="branding.requireEmergencyContact" dense class="mt-4">
                    <v-col cols="6"><v-text-field v-model="rider.emergencyName" label="Emergency contact name" density="compact" hide-details></v-text-field></v-col>
                    <v-col cols="6"><v-text-field v-model="rider.emergencyPhone" type="tel" label="Emergency contact phone" density="compact" hide-details></v-text-field></v-col>
                </v-row>

                <!-- Classes assigned to this rider -->
                <template v-if="classAssigns.length">
                    <div class="text-caption text-medium-emphasis mt-3 mb-1">Race classes for this rider</div>
                    <div v-for="ca in classesForRider(ri)" :key="ca.ticketId" class="d-flex align-center ga-2 mb-1">
                        <div class="flex-grow-1">{{ ca.tierName }}</div>
                        <v-text-field v-model="ca.raceNumber" label="Race #" density="compact" hide-details style="max-width: 110px"></v-text-field>
                        <v-select v-if="riders.length > 1" :model-value="ca.riderIndex"
                            @update:model-value="ca.riderIndex = $event"
                            :items="riderIndexOptions" item-title="label" item-value="value"
                            label="Rider" density="compact" hide-details style="max-width: 120px"></v-select>
                    </div>
                </template>

                <template v-if="rider.needsWaiver">
                    <div class="text-caption text-medium-emphasis mt-3 mb-1">
                        {{ isMinor(rider.birthdate) ? 'Rider is under 18 — a parent/guardian must sign' : 'Signature' }}
                    </div>
                    <v-text-field v-if="isMinor(rider.birthdate)" v-model="rider.parentName" label="Parent/guardian name" density="compact" class="mb-2 mt-4" hide-details></v-text-field>
                    <SignaturePad v-model="rider.signatureDataUrl" />
                </template>
            </div>

            <!-- Spectators that need a signed waiver -->
            <div v-for="(spec, si) in spectators" :key="'spec-' + si" class="evt-reg mb-4 pa-3">
                <div class="font-weight-medium mb-2">Spectator {{ spectators.length > 1 ? si + 1 : '' }} — {{ spec.tierName }}</div>
                <v-row dense>
                    <v-col cols="6"><v-text-field v-model="spec.firstName" label="First name" density="compact" hide-details></v-text-field></v-col>
                    <v-col cols="6"><v-text-field v-model="spec.lastName" label="Last name" density="compact" hide-details></v-text-field></v-col>
                </v-row>
                <v-text-field v-model="spec.birthdate" type="date" label="Date of birth" density="compact" class="mt-3" :max="todayIso" hide-details></v-text-field>
                <div class="text-caption text-medium-emphasis mt-3 mb-1">
                    {{ isMinor(spec.birthdate) ? 'Under 18 — a parent/guardian must sign' : 'Signature' }}
                </div>
                <v-text-field v-if="isMinor(spec.birthdate)" v-model="spec.parentName" label="Parent/guardian name" density="compact" class="mb-2" hide-details></v-text-field>
                <SignaturePad v-model="spec.signatureDataUrl" />
            </div>

            <div v-if="errorMessage" class="text-error text-body-2 mb-2">{{ errorMessage }}</div>
            <v-btn block color="primary" size="large" :loading="finishing" @click="finish">Finish registration</v-btn>
        </template>

        <!-- ── 5. Done ───────────────────────────────────────────────────── -->
        <template v-else>
            <div class="text-center py-4">
                <v-icon color="success" size="56">mdi-check-circle</v-icon>
                <h2 class="text-h6 font-weight-bold mt-2 mb-1">You're all set!</h2>
                <p class="text-body-2 text-medium-emphasis mb-4">A confirmation and your entry QR codes are on the way to {{ email }}.</p>
                <v-btn v-if="isEmbed && embedCameFromWidget" color="primary" @click="router.back()">Done</v-btn>
                <v-btn v-else-if="!isEmbed" color="primary" :to="isAuthed ? '/User/Upcoming' : '/Events'">{{ isAuthed ? 'View my entries' : 'Done' }}</v-btn>
            </div>

            <!-- Guest conversion: invite account creation while their details are still fresh
                 (prefilled from the order + the first rider's registration). -->
            <div v-if="!isAuthed" class="evt-signup mt-4 pa-4">
                <h3 class="text-subtitle-1 font-weight-bold mb-1">Create your free account</h3>
                <p class="text-body-2 text-medium-emphasis mb-3">Save time on your next visit and keep your entries in one place.</p>
                <AccountSignupForm :prefill="signupPrefill" />
            </div>
        </template>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, watch, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { TicketService, type TicketTier, type TicketRedemption } from '@/services/TicketService'
import { UserService } from '@/services/UserService'
import { UpcomingService } from '@/services/UpcomingService'
import { useConfirm } from '@/composables/useConfirm'
import { branding } from '@/stores/branding'
import authHelper from '@/helpers/AuthHelper'
import { getStripe } from '@/helpers/StripeHelper'
import SignaturePad from '@/components/SignaturePad.vue'
import ExtrasPicker, { type ExtraSelection } from '@/components/ExtrasPicker.vue'
import AccountSignupForm from '@/components/AccountSignupForm.vue'
import { TaxService } from '@/services/TaxService'
import type { EventDto, EligibleExtra } from '@/services/EventService'

const props = defineProps<{ event: EventDto; tiers: TicketTier[] }>()
// Asks the parent to re-fetch tiers (e.g. after a 409 price_changed) so the buyer
// sees the new active-step price without a manual page refresh.
const emit = defineEmits<{ (e: 'price-changed'): void }>()

const route = useRoute()
const router = useRouter()

// Embed mode: the checkout is framed on the track's own site, so the "done" step
// must not lead anywhere outside the flow. If the visitor came from an embedded
// events/calendar widget in this iframe, "Done" returns to it (history back keeps
// the widget's query config); on a direct single-event embed there's nowhere to
// go, so no button renders at all.
const isEmbed = computed(() => !!route.meta.embed)
const embedCameFromWidget: boolean = (() => {
    const back = String(window.history.state?.back ?? '')
    return back.startsWith('/embed/events') || back.startsWith('/embed/calendar')
})()

const ticketService = new TicketService()
const userService = new UserService()
const upcomingService = new UpcomingService()
const taxService = new TaxService()
const confirm = useConfirm()

// Admission tax config for the estimate shown before payment. The server is authoritative
// (it returns the exact taxCents on the purchase), so this only drives the pre-pay estimate.
const admissionTaxCfg = ref<{ rateBps: number; pricesIncludeTax: boolean; serviceChargeTaxable: boolean }>(
    { rateBps: 0, pricesIncludeTax: false, serviceChargeTaxable: true })

type Step = 'select' | 'details' | 'payment' | 'register' | 'done'
const step = ref<Step>('select')

const qty = reactive<Record<string, number>>({})
const riderCount = ref(1)
const couponCode = ref('')
const giftCardCode = ref('')

const name = ref('')
const email = ref('')
const isAuthed = ref(authHelper.isAuthenticated())
// The logged-in buyer's saved emergency contact, used to pre-fill the first rider's signup.
const profileEmergencyName = ref('')
const profileEmergencyPhone = ref('')
const showLogin = ref(false)
const loginPassword = ref('')
const loggingIn = ref(false)
const loginError = ref('')

const creating = ref(false)
const finishing = ref(false)
const errorMessage = ref('')

const clientSecret = ref<string | null>(null)
const chargeCents = ref(0)
// A redemption token from the created order. Used to point a redirect-flow payment
// (3DS / wallet / bank methods) at the resume page instead of back at this component,
// which would otherwise remount blank and silently drop the post-payment register step.
const resumeToken = ref<string | null>(null)
const paymentElementId = `evt-pay-${Math.random().toString(36).slice(2, 8)}`
const stripeReady = ref(false)
const paying = ref(false)
const paymentError = ref<string | null>(null)
let stripe: any = null
let elements: any = null

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const todayIso = dayjs().format('YYYY-MM-DD')

// ── Registration model ───────────────────────────────────────────────────────
interface RiderCard {
    firstName: string
    lastName: string
    birthdate: string
    bike: string
    parentName: string
    emergencyName: string
    emergencyPhone: string
    signatureDataUrl: string | null
    gateTicketId: string | null    // this rider's gate fee ticket (one per rider)
    needsWaiver: boolean
}
interface ClassAssign {
    ticketId: string
    tierName: string
    riderIndex: number
    raceNumber: string
}
interface SpectatorCard {
    ticketId: string
    tierName: string
    firstName: string
    lastName: string
    birthdate: string
    parentName: string
    signatureDataUrl: string | null
}
const riders = ref<RiderCard[]>([])
const classAssigns = ref<ClassAssign[]>([])
const spectators = ref<SpectatorCard[]>([])

const isRaceEvent = computed(() => raceTiers.value.length > 0)
// A genuine race (not practice/open-ride/lesson/etc.) is the only event type where one
// rider can hold several class entries, so it's the only type that needs the rider-count
// stepper. Other types model each entry as its own rider.
const isTrueRace = computed(() => props.event.eventTypeCode === 'race')
const activeTiers = computed(() => props.tiers.filter(t => t.isActive))
const raceTiers = computed(() => activeTiers.value.filter(t => t.kind === 'race_entry'))
const riderGateTiers = computed(() => activeTiers.value.filter(t => t.kind === 'gate_fee' && t.audience === 'rider'))
const spectatorGateTiers = computed(() => activeTiers.value.filter(t => t.kind === 'gate_fee' && t.audience === 'spectator'))

// Add-ons: real extras only (gate fees are tiers now, so the legacy gate-fee extra is excluded).
const addonExtras = computed<EligibleExtra[]>(() =>
    (props.event.eligibleExtras ?? []).filter(e =>
        e.kind !== 'gate_fee' && e.name.trim().toLowerCase() !== 'gate fee'))
const extrasSelection = ref<ExtraSelection[]>([])
// Resolve each selection's unit price (variant override or product base) for the running total.
const extrasTotalCents = computed(() => extrasSelection.value.reduce((sum, s) => {
    const prod = addonExtras.value.find(e => e.productId === s.productId)
    if (!prod) return sum
    const unit = s.variantId
        ? (prod.variants.find(v => v.id === s.variantId)?.priceCents ?? prod.priceCents)
        : prod.priceCents
    return sum + unit * s.quantity
}, 0))

// Order summary lines for the details step — selected tiers + add-ons with line totals.
interface OrderLine { label: string; qty: number; lineCents: number }
const orderLines = computed<OrderLine[]>(() => {
    const lines: OrderLine[] = []
    for (const t of props.tiers) {
        const q = qty[t.id] || 0
        if (q > 0) lines.push({ label: t.name, qty: q, lineCents: t.priceCents * q })
    }
    for (const s of extrasSelection.value) {
        if (s.quantity <= 0) continue
        const p = addonExtras.value.find(e => e.productId === s.productId)
        if (!p) continue
        let unit = p.priceCents
        let suffix = ''
        if (s.variantId) {
            const v = p.variants.find(x => x.id === s.variantId)
            if (v) {
                unit = v.priceCents
                const vl = [v.size, v.color, v.gender].filter(Boolean).join(' ')
                if (vl) suffix = ` (${vl})`
            }
        }
        lines.push({ label: p.name + suffix, qty: s.quantity, lineCents: unit * s.quantity })
    }
    return lines
})

const totalRaceQty = computed(() => raceTiers.value.reduce((s, t) => s + (qty[t.id] || 0), 0))
const riderGateQty = computed(() => riderGateTiers.value.reduce((s, t) => s + (qty[t.id] || 0), 0))
const requiredRiderGate = computed(() => riderGateTiers.value.some(t => t.required))
// Number of distinct race classes in the cart. A single rider can't enter the same
// class twice, so within one class quantity == riders; only across two or more classes
// is the rider count ambiguous (one rider in several classes vs. several riders).
const raceTiersInCart = computed(() => raceTiers.value.filter(t => (qty[t.id] || 0) > 0).length)
// Largest single-class quantity: those entries must be distinct riders, so this is the
// floor on the rider count (and the total quantity is the ceiling).
const maxClassQty = computed(() => Math.max(0, ...raceTiers.value.map(t => qty[t.id] || 0)))
// Show the "number of riders" stepper only for a real race with two or more classes selected.
const showRiderCount = computed(() => isTrueRace.value && raceTiersInCart.value > 1)
// Riders implied by the cart: the stepper when it's shown, else one rider per entry.
const raceRiderCount = computed(() => {
    if (!isRaceEvent.value) return 0
    if (showRiderCount.value) return riderCount.value
    return totalRaceQty.value
})

const hasSelection = computed(() =>
    Object.values(qty).some(q => q > 0) || extrasSelection.value.some(s => s.quantity > 0))
const estTotalCents = computed(() =>
    props.tiers.reduce((sum, t) => sum + t.priceCents * (qty[t.id] || 0), 0) + extrasTotalCents.value)

// Rider-paid service fee, mirroring the server's per-unit integer math
// (ComputeWithServiceCharge): floor(price * tenantBps) then floor(× riderBps).
// 0 across the board = the track is absorbing the whole fee, so we hide the line.
function riderFeePerUnit(priceCents: number, riderBps: number): number {
    const tenantBps = branding.serviceChargeBps ?? 0
    const serviceCharge = Math.floor((priceCents * tenantBps) / 10000)
    return Math.floor((serviceCharge * riderBps) / 10000)
}
const serviceFeeCents = computed(() => {
    let fee = 0
    for (const t of props.tiers) {
        const q = qty[t.id] || 0
        if (q > 0) fee += riderFeePerUnit(t.priceCents, t.riderPaidServiceChargeBps ?? 10000) * q
    }
    for (const s of extrasSelection.value) {
        if (s.quantity <= 0) continue
        const p = addonExtras.value.find(e => e.productId === s.productId)
        if (!p) continue
        const unit = s.variantId
            ? (p.variants.find(v => v.id === s.variantId)?.priceCents ?? p.priceCents)
            : p.priceCents
        fee += riderFeePerUnit(unit, p.riderPaidServiceChargeBps ?? 10000) * s.quantity
    }
    return fee
})
// Estimated admission tax for the pre-pay summary. Tickets only (admission tax doesn't apply to
// extras). Mirrors the server's per-unit rounding. Inclusive pricing adds nothing on top (the tax
// is already in the listed price), so the estimate is 0 there. The server returns the exact tax.
const estTaxCents = computed(() => {
    const cfg = admissionTaxCfg.value
    if (!cfg.rateBps || cfg.pricesIncludeTax) return 0
    let tax = 0
    for (const t of props.tiers) {
        const q = qty[t.id] || 0
        if (q <= 0) continue
        const fee = riderFeePerUnit(t.priceCents, t.riderPaidServiceChargeBps ?? 10000)
        const base = cfg.serviceChargeTaxable ? t.priceCents + fee : t.priceCents
        tax += Math.round((base * cfg.rateBps) / 10000) * q
    }
    return tax
})
const grandTotalCents = computed(() => estTotalCents.value + serviceFeeCents.value + estTaxCents.value)
const detailsValid = computed(() => name.value.trim().length > 1 && /\S+@\S+\.\S+/.test(email.value.trim()))
const anyWaiver = computed(() => riders.value.some(r => r.needsWaiver) || spectators.value.length > 0)

// Prefill the post-checkout signup from what we already collected: the purchaser's name +
// email, and the first rider's birthdate / emergency contact from registration.
const signupPrefill = computed(() => {
    const parts = name.value.trim().split(/\s+/)
    const r0 = riders.value[0]
    return {
        email: email.value,
        firstName: parts[0] ?? '',
        lastName: parts.slice(1).join(' '),
        birthdate: r0?.birthdate || '',
        emergencyName: r0?.emergencyName || '',
        emergencyPhone: r0?.emergencyPhone || '',
    }
})

// Customizable section headings (e.g. a track that sells rider admission as
// "Passes" instead of "Rider Gate"). Resolution: this event's override (set in
// the event editor) → tenant setting → platform default.
const riderGateLabel = computed(() =>
    props.event.riderGateLabel || branding.riderGateLabel || 'Rider Gate')
const spectatorGateLabel = computed(() =>
    props.event.spectatorGateLabel || branding.spectatorGateLabel || 'Spectator Gate')

// Required-gate rule on the select step: when a race has a required rider gate fee,
// the buyer must pick exactly one gate fee per rider.
const riderGateHint = computed(() => {
    if (!isRaceEvent.value || !requiredRiderGate.value || totalRaceQty.value === 0) return ''
    const need = raceRiderCount.value
    if (riderGateQty.value === need) return ''
    return `This race requires one ${riderGateLabel.value} selection per rider — choose ${need} (you have ${riderGateQty.value}).`
})
const canContinue = computed(() => hasSelection.value && !riderGateHint.value)

const riderIndexOptions = computed(() =>
    Array.from({ length: riders.value.length }, (_, i) => ({ value: i, label: `Rider ${i + 1}` })))

function priceLabel(cents: number): string {
    return cents === 0 ? 'Free' : `$${(cents / 100).toFixed(2)}`
}
function soldOut(t: TicketTier): boolean {
    const rem = remaining(t)
    return rem != null && rem <= 0
}
function remaining(t: TicketTier): number | null {
    // Price-ladder steps cap at the event capacity (remainingToCapacity); standalone tiers
    // cap at their own inventory; null means unlimited.
    if (t.remainingToCapacity != null) return t.remainingToCapacity
    return t.inventory == null ? null : Math.max(0, t.inventory - (t.sold ?? 0))
}
// Buy-page hint for a price-ladder step: where the price goes next.
function stepHint(t: TicketTier): string {
    if (t.nextPriceCents == null) return ''
    const next = priceLabel(t.nextPriceCents)
    if (t.nextChangeKind === 'date' && t.nextChangeAtUtc) {
        return `rises to ${next} on ${new Date(t.nextChangeAtUtc).toLocaleDateString()}`
    }
    return `then ${next}`
}
function canAdd(t: TicketTier): boolean {
    const rem = remaining(t)
    return rem == null || (qty[t.id] || 0) < rem
}
function bump(t: TicketTier, delta: number) {
    qty[t.id] = Math.max(0, (qty[t.id] || 0) + delta)
}
function isMinor(birthdate: string): boolean {
    if (!birthdate) return false
    return dayjs().diff(dayjs(birthdate), 'year') < 18
}
function classesForRider(ri: number): ClassAssign[] {
    return classAssigns.value.filter(c => c.riderIndex === ri)
}

// Keep riderCount within its implied bounds as the cart changes: at least the largest
// single-class quantity (those entries must be distinct riders) and at most the total
// number of entries (everyone separate).
watch([maxClassQty, totalRaceQty], ([lo, hi]) => {
    const min = Math.max(1, lo)
    riderCount.value = Math.min(Math.max(riderCount.value, min), Math.max(min, hi))
})

onMounted(async () => {
    // Load the tenant's admission tax so the pre-pay estimate can show a tax line.
    try {
        const tr = await taxService.getAdmissionTax()
        const cfg = tr.data?.data
        if (cfg) {
            admissionTaxCfg.value = {
                rateBps: cfg.rateBps ?? 0,
                pricesIncludeTax: !!cfg.pricesIncludeTax,
                serviceChargeTaxable: cfg.serviceChargeTaxable ?? true,
            }
        }
    } catch { /* estimate falls back to no tax; the server still charges the correct amount */ }

    if (isAuthed.value) {
        try {
            const r = await userService.getProfile()
            const p: any = (r.data as any).data ?? r.data
            name.value = `${p.firstName ?? ''} ${p.lastName ?? ''}`.trim()
            email.value = p.email ?? ''
            // Remember the account's emergency contact so we can pre-fill the rider signup
            // (the buyer is usually the first rider). Empty when not yet on file.
            profileEmergencyName.value = p.emergencyContactName ?? ''
            profileEmergencyPhone.value = p.emergencyContactPhone ?? ''
        } catch { /* leave blank for them to fill */ }
    }
})

// "Not you?" on a shared device: silently sign out (no redirect, keeps the cart) and clear
// the buyer fields so the next person enters their own. Typing a new email then re-runs the
// account check below (offering inline login when that email already has an account).
function switchUser() {
    authHelper.logout()
    isAuthed.value = false
    name.value = ''
    email.value = ''
    showLogin.value = false
}

async function onEmailBlur() {
    if (isAuthed.value || !/\S+@\S+\.\S+/.test(email.value.trim())) { showLogin.value = false; return }
    try {
        const r = await userService.emailExists(email.value.trim())
        showLogin.value = !!(r.data as any).data?.exists
    } catch {
        showLogin.value = false
    }
}

async function doLogin() {
    loginError.value = ''
    loggingIn.value = true
    try {
        const r = await userService.login({ email: email.value.trim(), password: loginPassword.value })
        const d: any = (r.data as any).data ?? r.data
        authHelper.setToken(d.token)
        authHelper.setUserId(d.userId)
        authHelper.setRole(d.role)
        isAuthed.value = true
        showLogin.value = false
        name.value = `${d.firstName ?? ''} ${d.lastName ?? ''}`.trim() || name.value
        email.value = d.email ?? email.value
        snackbarColor.value = 'success'
        snackbarText.value = 'Logged in successfully.'
        snackbar.value = true
    } catch (err: any) {
        loginError.value = err.response?.data?.error || 'Could not log in. Check your password.'
    } finally {
        loggingIn.value = false
    }
}

// For a signed-in buyer, warn if they already hold active entries for this event so a
// repeat purchase is a deliberate choice. (Race-class duplicates are still hard-blocked
// server-side; this is the softer "you may not have meant to buy again" guard.) Guests'
// prior orders aren't known here, so the check is skipped for them.
async function alreadyHasEntriesConfirmed(): Promise<boolean> {
    try {
        const r = await upcomingService.eventOrder(props.event.id)
        const items = (r.data as any).data?.items ?? []
        const active = items.filter((i: any) => i.status === 'paid' || i.status === 'redeemed' || i.status === 'pending').length
        if (active === 0) return true
        return await confirm({
            title: 'You already have entries for this event',
            message: `You already have ${active} ${active === 1 ? 'entry' : 'entries'} for this event. Do you want to buy more?`,
            confirmText: 'Yes, continue',
        })
    } catch {
        // Never block checkout because the lookup failed.
        return true
    }
}

async function createIntent() {
    errorMessage.value = ''
    if (isAuthed.value && !(await alreadyHasEntriesConfirmed())) return
    creating.value = true
    try {
        const items = Object.entries(qty).filter(([, q]) => q > 0).map(([tierId, quantity]) => ({ tierId, quantity }))
        const extras = extrasSelection.value
            .filter(s => s.quantity > 0)
            .map(s => ({ productId: s.productId, quantity: s.quantity, variantId: s.variantId }))
        const r = await ticketService.createTicketPurchase({
            items,
            extras: extras.length ? extras : null,
            email: isAuthed.value ? null : email.value.trim(),
            name: isAuthed.value ? null : name.value.trim(),
            couponCode: couponCode.value.trim() || null,
            giftCardCode: giftCardCode.value.trim() || null,
            deferRegistration: true,
        })
        const data = (r.data as any).data
        chargeCents.value = data.amountCents
        resumeToken.value = data.tickets?.[0]?.redemptionToken ?? null
        buildRegistration(data.tickets)
        if (!data.clientSecret) {
            step.value = needsRegistration.value ? 'register' : 'done'
        } else {
            clientSecret.value = data.clientSecret
            step.value = 'payment'
            await nextTick()
            await mountPaymentElement()
        }
    } catch (err: any) {
        if (err.response?.status === 409 && err.response?.data?.code === 'price_changed') {
            errorMessage.value = err.response.data.message
                || 'The price for this event just changed. Please review the updated price before continuing.'
            emit('price-changed')   // parent re-fetches tiers so the new price shows immediately
        } else {
            errorMessage.value = err.response?.data?.error || 'Could not start checkout.'
        }
    } finally {
        creating.value = false
    }
}

const needsRegistration = computed(() => riders.value.length > 0 || spectators.value.length > 0)

// Build the post-payment registration model from the created tickets. Riders own a
// gate fee (when present) + the race classes assigned to them; spectators that need a
// waiver each get their own card. Plain (waiver-less) spectators have nothing to fill.
function buildRegistration(tickets: TicketRedemption[]) {
    const tierByName = new Map(props.tiers.map(t => [t.name, t]))
    const classTickets = tickets.filter(tk => tierByName.get(tk.tierName)?.kind === 'race_entry')
    const riderGateTickets = tickets.filter(tk => {
        const t = tierByName.get(tk.tierName)
        return t?.kind === 'gate_fee' && t.audience === 'rider'
    })
    const spectatorWaiverTickets = tickets.filter(tk => {
        const t = tierByName.get(tk.tierName)
        return t?.kind === 'gate_fee' && t.audience === 'spectator' && props.event.requiresSpectatorWaiver
    })

    const ridersNeeded = isRaceEvent.value
        ? Math.max(raceRiderCount.value, riderGateTickets.length, classTickets.length ? 1 : 0)
        : riderGateTickets.length

    riders.value = Array.from({ length: ridersNeeded }, (_, i) => ({
        firstName: '', lastName: '', birthdate: '', bike: '', parentName: '',
        emergencyName: '', emergencyPhone: '', signatureDataUrl: null,
        gateTicketId: riderGateTickets[i]?.purchaseId ?? null,
        needsWaiver: props.event.requiresRiderWaiver,
    }))
    classAssigns.value = classTickets.map((tk, i) => ({
        ticketId: tk.purchaseId,
        tierName: tk.tierName,
        riderIndex: ridersNeeded > 0 ? i % ridersNeeded : 0,
        raceNumber: '',
    }))
    spectators.value = spectatorWaiverTickets.map(tk => ({
        ticketId: tk.purchaseId, tierName: tk.tierName,
        firstName: '', lastName: '', birthdate: '', parentName: '', signatureDataUrl: null,
    }))

    // Prefill the first rider (or lone spectator) with the purchaser's name.
    const parts = name.value.trim().split(/\s+/)
    const first = parts[0] ?? ''
    const last = parts.slice(1).join(' ')
    if (riders.value.length) {
        riders.value[0].firstName = first
        riders.value[0].lastName = last
        // Pre-fill the first rider's emergency contact from the buyer's profile (if on file).
        if (!riders.value[0].emergencyName) riders.value[0].emergencyName = profileEmergencyName.value
        if (!riders.value[0].emergencyPhone) riders.value[0].emergencyPhone = profileEmergencyPhone.value
    } else if (spectators.value.length) {
        spectators.value[0].firstName = first
        spectators.value[0].lastName = last
    }
}

async function mountPaymentElement() {
    if (!clientSecret.value) return
    // Direct-charge tenants confirm the payment on their own connected account, so Stripe.js must
    // be initialized with that account; platform-mode tenants pass no account (charge on platform).
    const stripeAccount = branding.stripeChargeMode === 'direct' ? branding.stripeConnectAccountId : null
    stripe = await getStripe(branding.stripePublishableKey, stripeAccount)
    if (!stripe) { paymentError.value = 'Stripe is not available.'; return }
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
        // For methods that need a redirect, return to the dedicated resume page (which
        // rebuilds the order from the server by token) rather than this component, which
        // would remount blank. The inline no-redirect path below is unaffected.
        const returnUrl = resumeToken.value
            ? `${window.location.origin}/FinishRegistration/${resumeToken.value}`
            : window.location.href
        const { error, paymentIntent } = await stripe.confirmPayment({
            elements,
            confirmParams: { return_url: returnUrl },
            redirect: 'if_required',
        })
        if (error) {
            paymentError.value = error.message || 'Payment failed.'
        } else if (paymentIntent?.status === 'succeeded') {
            // Stripe confirmed the charge. Finalize server-side now (it re-verifies with
            // Stripe and is idempotent) so the entry shows on the rider's schedule right
            // away; the webhook / reconciler are the backup.
            try { await ticketService.confirmIntent(paymentIntent.id) } catch { /* webhook/reconciler will finalize */ }
            step.value = needsRegistration.value ? 'register' : 'done'
        } else {
            // No hard error, but the payment hasn't settled yet (e.g. 'processing' for
            // delayed methods). Don't claim success — the webhook finalizes it and sends the
            // confirmation once it clears.
            paymentError.value = paymentIntent?.status === 'processing'
                ? "Your payment is processing. We'll email your confirmation as soon as it clears."
                : "We couldn't confirm your payment. If you were charged, your confirmation will arrive by email."
        }
    } catch (err: any) {
        paymentError.value = err?.message || 'Payment failed.'
    } finally {
        paying.value = false
    }
}

async function finish() {
    errorMessage.value = ''
    const registrants: Array<{
        firstName: string; lastName: string; birthdate?: string | null; bike?: string | null
        parentGuardianName?: string | null
        emergencyContactName?: string | null; emergencyContactPhone?: string | null
        waiverSignatureDataUrl?: string | null
        tickets: Array<{ ticketId: string; raceNumber?: string | null }>
    }> = []

    for (let i = 0; i < riders.value.length; i++) {
        const r = riders.value[i]
        const tickets: Array<{ ticketId: string; raceNumber?: string | null }> = []
        if (r.gateTicketId) tickets.push({ ticketId: r.gateTicketId })
        for (const ca of classAssigns.value.filter(c => c.riderIndex === i)) {
            tickets.push({ ticketId: ca.ticketId, raceNumber: ca.raceNumber.trim() || null })
        }
        if (tickets.length === 0) continue   // an unused rider slot — skip
        if (!r.firstName.trim() || !r.lastName.trim()) {
            errorMessage.value = `Rider ${i + 1} needs a first and last name.`; return
        }
        if (r.needsWaiver && !r.signatureDataUrl) {
            errorMessage.value = `${r.firstName || `Rider ${i + 1}`} needs to sign the waiver.`; return
        }
        // Birthdate is required to sign a waiver (matches the server), so a minor can't be signed in as
        // an adult by leaving it blank, which would also skip the parent/guardian requirement below.
        if (r.needsWaiver && !r.birthdate) {
            errorMessage.value = `${r.firstName || `Rider ${i + 1}`} needs a date of birth to sign the waiver.`; return
        }
        if (r.needsWaiver && isMinor(r.birthdate) && !r.parentName.trim()) {
            errorMessage.value = `A parent/guardian name is required for ${r.firstName || `rider ${i + 1}`}.`; return
        }
        if (branding.requireEmergencyContact && !r.emergencyPhone.trim()) {
            errorMessage.value = `An emergency contact phone is required for ${r.firstName || `rider ${i + 1}`}.`; return
        }
        registrants.push({
            firstName: r.firstName.trim(), lastName: r.lastName.trim(),
            birthdate: r.birthdate || null, bike: r.bike.trim() || null,
            parentGuardianName: r.needsWaiver && isMinor(r.birthdate) ? r.parentName.trim() : null,
            emergencyContactName: branding.requireEmergencyContact ? (r.emergencyName.trim() || null) : null,
            emergencyContactPhone: branding.requireEmergencyContact ? (r.emergencyPhone.trim() || null) : null,
            waiverSignatureDataUrl: r.needsWaiver ? r.signatureDataUrl : null,
            tickets,
        })
    }

    for (const s of spectators.value) {
        if (!s.firstName.trim() || !s.lastName.trim()) {
            errorMessage.value = 'Each spectator needs a first and last name.'; return
        }
        if (!s.signatureDataUrl) {
            errorMessage.value = `${s.firstName || 'This spectator'} needs to sign the waiver.`; return
        }
        if (isMinor(s.birthdate) && !s.parentName.trim()) {
            errorMessage.value = `A parent/guardian name is required for ${s.firstName || 'the minor spectator'}.`; return
        }
        registrants.push({
            firstName: s.firstName.trim(), lastName: s.lastName.trim(),
            birthdate: s.birthdate || null, bike: null,
            parentGuardianName: isMinor(s.birthdate) ? s.parentName.trim() : null,
            waiverSignatureDataUrl: s.signatureDataUrl,
            tickets: [{ ticketId: s.ticketId }],
        })
    }

    finishing.value = true
    try {
        await ticketService.completeRegistration({ registrants })
        step.value = 'done'
    } catch (err: any) {
        errorMessage.value = err.response?.data?.error || 'Could not save registration.'
    } finally {
        finishing.value = false
    }
}
</script>

<style scoped>
.evt-line {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 10px 0;
    border-bottom: 1px solid rgba(var(--v-theme-on-surface), 0.06);
}
/* on-surface (not hardcoded black) so dark-theme tenants keep readable labels on
   the dark checkout card. */
.evt-group-label {
    font-size: 13px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: rgba(var(--v-theme-on-surface), 0.6);
}
.evt-qty {
    min-width: 26px;
    text-align: center;
    font-variant-numeric: tabular-nums;
}
.evt-login,
.evt-reg,
.evt-riders,
.evt-signup {
    background: rgba(var(--v-theme-on-surface), 0.03);
    border-radius: 8px;
}
.evt-codes :deep(.v-expansion-panel) {
    background: transparent;
}
</style>
