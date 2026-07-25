<!--
    Counter booking for a shop rental: pick a window, add gear, take cash or card. The card path
    runs two Stripe intents in sequence on the same dialog: the rental fee (auto-capture), then the
    refundable deposit (manual-capture hold, never charged unless damage is kept at return).

    Extracted from the Rentals page so the Rental Board can book the exact resource a user clicked.
    That is what `preset` is for: a window plus, optionally, the specific variant and the specific
    serialized unit. Without a preset it behaves exactly like the old "New rental" button.

    The variant list arrives as a PROP rather than being fetched here on purpose: the catalog
    endpoints sit behind CatalogManage while this dialog is used on ShopCounter screens, so each
    host supplies the list from whatever it is already allowed to read.
-->
<template>
    <div>
        <!-- ── Book ────────────────────────────────────────────────────── -->
        <v-dialog :model-value="modelValue" max-width="640" persistent
            @update:model-value="v => !booking && emit('update:modelValue', v)">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>New rental</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="booking" @click="close"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-row dense>
                        <v-col cols="6"><v-text-field v-model="form.startsAt" type="datetime-local" label="From" density="compact" hide-details></v-text-field></v-col>
                        <v-col cols="6"><v-text-field v-model="form.endsAt" type="datetime-local" label="Until" density="compact" hide-details></v-text-field></v-col>
                    </v-row>

                    <div class="sp-group-label mt-5 mb-1">Gear</div>
                    <div class="d-flex ga-2 align-center">
                        <v-select v-model="pickVariantId" :items="rentableVariants" item-title="title" item-value="id"
                            label="Add rentable item" density="compact" hide-details class="flex-grow-1"></v-select>
                        <v-btn color="primary" variant="tonal" :disabled="!pickVariantId || !windowValid"
                            :loading="checkingAvailability" @click="addLine">Add</v-btn>
                    </div>
                    <p v-if="!windowValid" class="text-caption text-medium-emphasis mt-1">Pick the window first.</p>

                    <!-- Gear line: item text hard-left, quantity stepper and remove hard-right.
                         The explicit spacer keeps the controls in the same column on every row,
                         including serialized lines that have no stepper. -->
                    <div v-for="(l, i) in form.lines" :key="i" class="d-flex align-center ga-3 py-1 mt-2">
                        <div class="text-left">
                            <div class="text-body-2">{{ l.name }}<span v-if="l.unitLabel" class="text-medium-emphasis"> · {{ l.unitLabel }}</span></div>
                            <div class="text-caption text-medium-emphasis">{{ money(l.dailyRateCents) }}/day · deposit {{ money(l.depositCents) }}</div>
                        </div>
                        <v-spacer></v-spacer>
                        <template v-if="!l.itemId">
                            <div class="d-flex align-center ga-1">
                                <v-btn icon="mdi-minus" size="x-small" variant="tonal" :disabled="l.quantity <= 1" @click="l.quantity--"></v-btn>
                                <span class="pos-qty">{{ l.quantity }}</span>
                                <v-btn icon="mdi-plus" size="x-small" variant="tonal" :disabled="l.quantity >= l.maxQuantity" @click="l.quantity++"></v-btn>
                            </div>
                        </template>
                        <v-btn icon="mdi-close" size="x-small" variant="text" @click="form.lines.splice(i, 1)"></v-btn>
                    </div>

                    <v-divider class="my-3"></v-divider>
                    <div class="d-flex justify-space-between text-body-2">
                        <span>{{ days }} day{{ days === 1 ? '' : 's' }} × gear</span>
                        <span>{{ money(estimateCents) }}</span>
                    </div>
                    <!-- Damage waiver, offered only when the track has it configured. Placed with
                         the money, above the total, because taking it changes both the total and
                         the deposit and the counter has to read the consequence in one glance. -->
                    <div v-if="insuranceOffered" class="d-flex justify-space-between align-center text-body-2">
                        <v-checkbox v-model="form.insurance" density="compact" hide-details
                            class="ma-0 pa-0 flex-grow-0">
                            <template #label>
                                <span class="text-body-2">{{ insuranceLabel }}</span>
                            </template>
                        </v-checkbox>
                        <span>{{ form.insurance ? money(estimateInsuranceCents) : '—' }}</span>
                    </div>

                    <div v-if="estimateFeeCents > 0" class="d-flex justify-space-between text-body-2">
                        <span>Service fee</span>
                        <span>{{ money(estimateFeeCents) }}</span>
                    </div>
                    <div v-if="estimateTaxCents > 0" class="d-flex justify-space-between text-body-2">
                        <span>Sales tax</span>
                        <span>{{ money(estimateTaxCents) }}</span>
                    </div>
                    <div class="d-flex justify-space-between text-body-1 mt-1">
                        <strong>Total</strong>
                        <strong>{{ money(estimateTotalCents) }}</strong>
                    </div>
                    <p v-if="taxRateUnset" class="text-caption text-warning mt-1">
                        No rental tax rate is set, so nothing is being collected.
                        <router-link to="/Admin/BikeShop/Rentals?tab=settings" @click="close">Set it in Settings</router-link>.
                    </p>
                    <div class="d-flex justify-space-between text-body-2 text-medium-emphasis mt-1">
                        <span>Refundable deposit (card hold)</span>
                        <span>
                            <!-- Struck through rather than hidden: the counter should see what the
                                 waiver just saved the renter, and be able to say so. -->
                            <span v-if="waiverTaken && estimateDepositBeforeWaiverCents > 0"
                                class="text-decoration-line-through mr-1">
                                {{ money(estimateDepositBeforeWaiverCents) }}
                            </span>
                            {{ money(estimateDepositCents) }}
                        </span>
                    </div>
                    <p v-if="waiverTaken" class="text-caption text-medium-emphasis mt-1">
                        {{ insuranceLabel }} taken, so the refundable deposit is waived. The fee is
                        not refundable.
                    </p>
                    <p v-if="feeAbsorbed" class="text-caption text-medium-emphasis mt-1">
                        The track is absorbing the service fee on rentals.
                    </p>

                    <!-- Riders, not units: a bike plus a helmet is one rider, two bikes is two.
                         Each rider signs the waiver before the gear leaves. -->
                    <v-text-field v-model.number="form.ridersRequired" type="number" min="1" max="50"
                        label="Riders (each must sign the waiver)" density="compact" class="mt-4"
                        :hint="ridersHint" persistent-hint></v-text-field>

                    <v-text-field v-model="form.renterName" label="Renter name" density="compact" class="mt-4" hide-details></v-text-field>
                    <v-row dense class="mt-2">
                        <v-col cols="6"><v-text-field v-model="form.renterPhone" label="Phone" density="compact" hide-details></v-text-field></v-col>
                        <v-col cols="6"><v-text-field v-model="form.renterEmail" type="email" label="Email" density="compact" hide-details></v-text-field></v-col>
                    </v-row>

                    <div v-if="bookError" class="text-error text-body-2 mt-3">{{ bookError }}</div>
                    <div class="d-flex ga-2 mt-4">
                        <v-btn color="secondary" size="large" class="flex-grow-1" :disabled="form.lines.length === 0 || !windowValid || booking"
                            :loading="booking && payMethod === 'cash'" @click="book('cash')">Cash</v-btn>
                        <v-btn color="primary" size="large" class="flex-grow-1" :disabled="form.lines.length === 0 || !windowValid || booking"
                            :loading="booking && payMethod === 'card'" @click="book('card')">Card</v-btn>
                    </div>
                    <p v-if="form.lines.length > 0 && !windowValid" class="text-caption text-error mt-1">
                        The rental window is invalid (the end must be after the start).
                    </p>
                </v-card-text>
            </v-card>
        </v-dialog>

        <!-- ── Card payment (fee, then deposit hold) ───────────────────── -->
        <v-dialog v-model="payOpen" persistent max-width="480">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ payStage === 'fee' ? 'Rental payment' : 'Deposit hold' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="paying" @click="closePay"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p v-if="payStage === 'deposit'" class="text-body-2 text-medium-emphasis mb-2">
                        Payment received. Now authorize the refundable deposit — the card is NOT charged
                        unless damage is kept at return.
                    </p>
                    <div class="text-h6 mb-3">{{ money(payStage === 'fee' ? pendingTotal : pendingDeposit) }}</div>
                    <div ref="paymentHost" class="mb-4"></div>
                    <div v-if="payError" class="text-error text-body-2 mb-2">{{ payError }}</div>
                    <v-btn block color="primary" size="large" :loading="paying" :disabled="!stripeReady" @click="payCurrent">
                        {{ payStage === 'fee' ? `Charge ${money(pendingTotal)}` : `Hold ${money(pendingDeposit)}` }}
                    </v-btn>
                    <v-btn v-if="payStage === 'deposit'" block variant="text" class="mt-2" :disabled="paying" @click="skipDeposit">
                        Skip the hold (collect deposit another way)
                    </v-btn>
                </v-card-text>
            </v-card>
        </v-dialog>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import dayjs from 'dayjs'
import { BikeShopService } from '@/services/BikeShopService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'

/** A bookable catalog variant, already flattened by the host page. */
export interface RentableVariantOption {
    id: string
    title: string
    name: string
    trackingKind: 'pool' | 'serialized'
    dailyRateCents: number
    depositCents: number
}

/** What the caller wants pre-filled. `itemId` books THAT physical unit (board click). */
export interface BookRentalPreset {
    startsAt?: string       // datetime-local ("YYYY-MM-DDTHH:mm")
    endsAt?: string
    variantId?: string
    itemId?: string
}

const props = defineProps<{
    modelValue: boolean
    rentableVariants: RentableVariantOption[]
    preset?: BookRentalPreset | null
}>()
const emit = defineEmits<{
    (e: 'update:modelValue', v: boolean): void
    (e: 'booked'): void
    (e: 'notify', text: string, color: 'success' | 'error'): void
}>()

const service = new BikeShopService()
function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }
function flash(t: string, c: 'success' | 'error' = 'success') { emit('notify', t, c) }
function close() { emit('update:modelValue', false) }

interface BookLine {
    variantId: string; itemId?: string; name: string; unitLabel?: string
    quantity: number; maxQuantity: number; dailyRateCents: number; depositCents: number
}

const booking = ref(false)
const bookError = ref('')
const payMethod = ref<'cash' | 'card' | null>(null)
const pickVariantId = ref<string | null>(null)
const checkingAvailability = ref(false)
const form = ref({
    startsAt: '', endsAt: '', lines: [] as BookLine[],
    ridersRequired: 1, renterName: '', renterEmail: '', renterPhone: '',
    insurance: false,
})

const windowValid = computed(() => !!form.value.startsAt && !!form.value.endsAt
    && new Date(form.value.endsAt) > new Date(form.value.startsAt))
const days = computed(() => {
    if (!windowValid.value) return 1
    const hours = (new Date(form.value.endsAt).getTime() - new Date(form.value.startsAt).getTime()) / 36e5
    return Math.max(1, Math.ceil(hours / 24))
})
const estimateCents = computed(() => form.value.lines.reduce((s, l) => s + l.dailyRateCents * days.value * l.quantity, 0))

// ── Damage waiver ───────────────────────────────────────────────────────────
// Mirrors Services.Payments.RentalCharge exactly (see RentalInsuranceTests): the fee is a
// percentage of the GROSS rental, it rides inside the subtotal so the service fee and tax apply
// to it, and taking it waives the deposit outright.
const insuranceOffered = computed(() =>
    !!branding.rentalInsuranceEnabled && (branding.rentalInsuranceBps ?? 0) > 0)
const insuranceLabel = computed(() => branding.rentalInsuranceLabel || 'Damage Protection')
const waiverTaken = computed(() => insuranceOffered.value && form.value.insurance && estimateCents.value > 0)
const estimateInsuranceCents = computed(() =>
    waiverTaken.value
        ? Math.floor((estimateCents.value * (branding.rentalInsuranceBps ?? 0)) / 10000)
        : 0)

/** What the deposit would be without the waiver, for the struck-through "you saved this". */
const estimateDepositBeforeWaiverCents = computed(() =>
    form.value.lines.reduce((s, l) => s + l.depositCents * l.quantity, 0))
const estimateDepositCents = computed(() =>
    estimateInsuranceCents.value > 0 ? 0 : estimateDepositBeforeWaiverCents.value)

/** The rental subtotal the fee and tax are computed on: gear plus the waiver, never the deposit. */
const estimateSubtotalCents = computed(() => estimateCents.value + estimateInsuranceCents.value)

// Suggested rider count: the largest single line quantity. Two bikes means two riders, while a
// bike plus a helmet is still one person. Staff can override; the server takes what we send.
const suggestedRiders = computed(() => Math.max(1, ...form.value.lines.map(l => l.quantity), 1))
const ridersHint = computed(() =>
    form.value.ridersRequired === suggestedRiders.value
        ? 'Each rider signs the waiver before the gear goes out.'
        : `Suggested ${suggestedRiders.value} based on the gear on this booking.`)

// Keep the field tracking the suggestion until staff type their own number.
let ridersTouched = false
watch(suggestedRiders, n => { if (!ridersTouched) form.value.ridersRequired = n })
watch(() => form.value.ridersRequired, (n, old) => { if (old !== undefined && n !== suggestedRiders.value) ridersTouched = true })

// Renter-paid service fee, mirroring the server's integer math exactly (floor the tenant charge,
// then floor the renter's share) so the quoted total matches what the card is charged. The base is
// the rental PLUS the waiver, which is revenue for a service; the deposit is never in it, because
// it's the renter's own money held against damage.
const estimateFeeCents = computed(() => {
    const serviceCharge = Math.floor((estimateSubtotalCents.value * (branding.serviceChargeBps ?? 0)) / 10000)
    return Math.floor((serviceCharge * (branding.rentalRiderPaidServiceChargeBps ?? 10000)) / 10000)
})
// Sales tax, mirroring the server: the base is the rental subtotal plus the renter fee when the
// tenant says the fee is taxable, and never the deposit.
const estimateTaxCents = computed(() => {
    const base = estimateSubtotalCents.value + (branding.rentalTaxServiceChargeTaxable ? estimateFeeCents.value : 0)
    return Math.round((base * (branding.rentalTaxBps ?? 0)) / 10000)
})
const estimateTotalCents = computed(() =>
    estimateSubtotalCents.value + estimateFeeCents.value + estimateTaxCents.value)
const taxRateUnset = computed(() => branding.rentalTaxBps == null)
// A fee is configured but the track is eating all of it, so the renter sees no line.
const feeAbsorbed = computed(() =>
    (branding.serviceChargeBps ?? 0) > 0 && estimateFeeCents.value === 0 && estimateCents.value > 0)

// Opening resets the form. A preset seeds the window and, when given, the exact gear the caller
// clicked; otherwise it's today through tomorrow with an empty cart.
watch(() => props.modelValue, async open => {
    if (!open) return
    const p = props.preset
    form.value = {
        startsAt: p?.startsAt || dayjs().format('YYYY-MM-DDTHH:mm'),
        endsAt: p?.endsAt || dayjs().add(1, 'day').format('YYYY-MM-DDTHH:mm'),
        lines: [], ridersRequired: 1, renterName: '', renterEmail: '', renterPhone: '',
        // Never carried over: the waiver is a decision the renter in front of you makes, and
        // inheriting the last customer's answer is how someone gets charged for one silently.
        insurance: false,
    }
    bookError.value = ''
    ridersTouched = false
    pickVariantId.value = p?.variantId ?? null
    if (p?.variantId) await addLine(p.itemId)
})

/**
 * Adds the currently picked variant to the booking. `wantItemId` pins a specific serialized unit
 * (the board's "book THIS bike"); without it a serialized line takes the first free unit.
 */
async function addLine(wantItemId?: string) {
    const meta = props.rentableVariants.find(v => v.id === pickVariantId.value)
    if (!meta || !windowValid.value) return
    checkingAvailability.value = true
    try {
        const r = await service.rentalAvailability(meta.id,
            new Date(form.value.startsAt).toISOString(), new Date(form.value.endsAt).toISOString())
        const { available, units } = r.data.data
        if (available <= 0) {
            bookError.value = 'Nothing free for that window.'
            flash('Nothing free for that window.', 'error')
            return
        }
        if (meta.trackingKind === 'serialized') {
            const used = new Set(form.value.lines.map(l => l.itemId).filter(Boolean))
            // A pinned unit must actually be free: someone else may have booked it between the
            // board rendering and this click, and the server would reject the booking anyway.
            const unit = wantItemId
                ? units.find(u => u.id === wantItemId && !used.has(u.id))
                : units.find(u => !used.has(u.id))
            if (!unit) {
                const msg = wantItemId
                    ? 'That unit is no longer free for this window. Pick another or change the times.'
                    : 'Every free unit is already on this booking.'
                bookError.value = msg
                flash(msg, 'error')
                return
            }
            form.value.lines.push({
                variantId: meta.id, itemId: unit.id, name: meta.name,
                unitLabel: unit.label + (unit.serial ? ' · ' + unit.serial : ''),
                quantity: 1, maxQuantity: 1, dailyRateCents: meta.dailyRateCents, depositCents: meta.depositCents,
            })
        } else {
            const existing = form.value.lines.find(l => l.variantId === meta.id && !l.itemId)
            if (existing) {
                if (existing.quantity < available) existing.quantity++
                else flash('No more free for that window.', 'error')
            } else {
                form.value.lines.push({
                    variantId: meta.id, name: meta.name, quantity: 1, maxQuantity: available,
                    dailyRateCents: meta.dailyRateCents, depositCents: meta.depositCents,
                })
            }
        }
    } catch (e: any) {
        const msg = e.response?.data?.error || 'Could not check availability for that item. Try again.'
        bookError.value = msg
        flash(msg, 'error')
    } finally { checkingAvailability.value = false }
}

async function book(method: 'cash' | 'card') {
    bookError.value = ''
    payMethod.value = method
    booking.value = true
    try {
        const r = await service.bookRental({
            lines: form.value.lines.map(l => ({ variantId: l.variantId, quantity: l.quantity, itemId: l.itemId ?? null })),
            ridersRequired: Math.max(1, form.value.ridersRequired || 1),
            startsAt: new Date(form.value.startsAt).toISOString(),
            endsAt: new Date(form.value.endsAt).toISOString(),
            paymentMethod: method,
            takeDepositHold: true,
            insurance: waiverTaken.value,
            renterName: form.value.renterName.trim() || null,
            renterEmail: form.value.renterEmail.trim() || null,
            renterPhone: form.value.renterPhone.trim() || null,
        })
        const data = r.data.data
        if (method === 'cash') {
            close()
            // The deposit clause is dropped when there's nothing to collect, which is the normal
            // case once the damage waiver is taken. "Deposit $0.00 to collect" reads as a bug.
            const booked = `Rental booked${data.orderNumber != null ? ' — #' + data.orderNumber : ''}.`
            flash(data.depositCents > 0
                ? `${booked} Deposit ${money(data.depositCents)} to collect at the counter.`
                : (data.insuranceCents ?? 0) > 0
                    ? `${booked} ${insuranceLabel.value} taken, so there's no deposit to collect.`
                    : booked)
            emit('booked')
        } else {
            close()
            feeSecret.value = data.clientSecret ?? null
            depositSecret.value = data.depositClientSecret ?? null
            pendingTotal.value = data.totalCents
            pendingDeposit.value = data.depositCents
            payStage.value = 'fee'
            payOpen.value = true
            await nextTick()
            await mountPayment(feeSecret.value)
        }
    } catch (e: any) {
        bookError.value = e.response?.data?.error || 'Could not book the rental. Please try again.'
    } finally { booking.value = false }
}

// ── Card payment: fee first, then the deposit hold on the same dialog ──────
const payOpen = ref(false)
const payStage = ref<'fee' | 'deposit'>('fee')
const paying = ref(false)
const payError = ref('')
const stripeReady = ref(false)
const paymentHost = ref<HTMLElement | null>(null)
const feeSecret = ref<string | null>(null)
const depositSecret = ref<string | null>(null)
const pendingTotal = ref(0)
const pendingDeposit = ref(0)
let stripe: any = null
let elements: any = null

async function mountPayment(secret: string | null) {
    payError.value = ''
    stripeReady.value = false
    if (!secret) { payError.value = 'Payment could not be started.'; return }
    const account = branding.stripeChargeMode === 'direct' ? branding.stripeConnectAccountId : null
    stripe = await getStripe(branding.stripePublishableKey, account)
    if (!stripe) { payError.value = 'Payments are unavailable right now.'; return }
    const host = paymentHost.value
    if (!host) { payError.value = 'Payment could not be started.'; return }
    host.innerHTML = ''
    elements = stripe.elements({ clientSecret: secret })
    elements.create('payment').mount(host)
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
                    finishCardBooking()
                }
            } else {
                payError.value = 'The payment has not settled yet. It will complete shortly.'
            }
        } else {
            // Deposit hold: manual capture, so success = 'requires_capture' (authorized, not charged).
            if (paymentIntent?.status === 'requires_capture' || paymentIntent?.status === 'succeeded') {
                finishCardBooking()
            } else {
                payError.value = 'The deposit hold did not authorize. Try again or skip it.'
            }
        }
    } catch (e: any) {
        payError.value = e?.message || 'Payment failed.'
    } finally { paying.value = false }
}

function skipDeposit() { finishCardBooking(true) }
function finishCardBooking(skippedDeposit = false) {
    payOpen.value = false
    stripe = null; elements = null; stripeReady.value = false
    flash(skippedDeposit ? 'Rental paid. Deposit hold skipped — collect it another way.' : 'Rental paid and deposit authorized.')
    emit('booked')
}
function closePay() {
    payOpen.value = false
    stripe = null; elements = null; stripeReady.value = false
    flash('Payment not completed — the booking stays pending until paid.', 'error')
    emit('booked')
}
</script>

<style scoped>
.sp-group-label {
    font-size: 13px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: rgba(var(--v-theme-on-surface), 0.6);
}
.pos-qty { min-width: 24px; text-align: center; font-variant-numeric: tabular-nums; }
</style>
