<template>
    <v-container style="max-width: 720px">
        <h1 class="text-h4 mb-4">Reserve a spot</h1>

        <v-progress-circular v-if="loadingEvent" indeterminate color="primary"></v-progress-circular>

        <v-card v-else-if="!event" class="pa-6 text-center" variant="outlined">
            <v-icon size="48" color="grey" class="mb-2">mdi-calendar-question</v-icon>
            <p class="text-body-2 mb-3">We couldn't find that event. It may have been cancelled or removed.</p>
            <v-btn color="primary" to="/Events">Back to Events</v-btn>
        </v-card>

        <template v-else>
            <v-card class="mb-4 pa-4" variant="tonal" :color="event.eventTypeColor || 'primary'">
                <div class="text-overline" style="opacity: 0.85">{{ event.eventTypeName }}</div>
                <div class="text-h5 font-weight-bold">{{ event.title }}</div>
                <div class="text-body-2 mt-1">
                    <v-icon size="small" class="mr-1">mdi-calendar</v-icon>{{ formatLong(event.startsAtUtc) }}
                </div>
                <div v-if="event.locationLabel" class="text-body-2">
                    <v-icon size="small" class="mr-1">mdi-map-marker</v-icon>{{ event.locationLabel }}
                </div>
                <div v-if="event.capacity" class="text-body-2 mt-1">
                    {{ Math.max(0, event.capacity - (event.spotsReserved ?? 0)) }} of {{ event.capacity }} spots left
                </div>
            </v-card>

            <v-alert v-if="needsEmergencyContact" type="warning" variant="tonal" class="mb-4">
                Participants are required to have an emergency contact on file before you can purchase.
                <router-link to="/User/Profile" class="ml-1">Add yours on your profile</router-link>.
            </v-alert>

            <v-alert v-if="eligibleProducts.length === 0" type="info" variant="tonal" class="mb-4">
                No passes are accepted at this event. Check the event for ticket options.
            </v-alert>

            <v-stepper v-if="eligibleProducts.length > 0" v-model="step" color="primary" hide-actions>
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
                    <v-stepper-window-item value="select">
                        <v-card class="mb-4 pa-4">
                            <v-card-title>Choose a pass</v-card-title>
                            <v-card-text>
                                <v-alert v-if="sigStatus?.hasSignedCurrent" type="success" variant="tonal" density="compact" class="mb-3">
                                    Your waiver is on file (signed {{ formatDate(sigStatus.signedAt) }}).
                                </v-alert>
                                <v-radio-group v-model="selectedProductId">
                                    <v-radio v-for="p in eligibleProducts" :key="p.id" :value="p.id">
                                        <template #label>
                                            <div>
                                                <strong>{{ p.name }}</strong> ${{ (p.priceCents / 100).toFixed(2) }}
                                                <div v-if="p.description" class="text-caption text-medium-emphasis">{{ p.description }}</div>
                                                <div class="text-caption text-medium-emphasis">
                                                    <span v-if="p.requiresWaiver">Waiver required</span>
                                                    <span v-else>No waiver required</span>
                                                </div>
                                            </div>
                                        </template>
                                    </v-radio>
                                </v-radio-group>
                                <v-text-field v-model.number="quantity" type="number" min="1" :max="maxQuantity"
                                    label="Quantity" density="compact"
                                    :hint="`Up to ${maxQuantity} spots available`" persistent-hint></v-text-field>
                                <v-select v-if="availableVouchers.length > 0 && quantity === 1"
                                    v-model="selectedVoucherId"
                                    :items="voucherOptions"
                                    item-title="title" item-value="value"
                                    label="Apply a reward voucher (optional)" density="compact"
                                    clearable class="mt-3"></v-select>
                                <p v-else-if="availableVouchers.length > 0 && quantity > 1" class="text-caption text-medium-emphasis mt-2">
                                    Vouchers can only be applied to single-pass purchases. Set quantity to 1 to use one.
                                </p>
                                <div class="text-body-1 mt-3" v-if="selectedProduct">
                                    Total: <strong>${{ ((selectedProduct.priceCents * quantity) / 100).toFixed(2) }}</strong>
                                    <span v-if="selectedVoucherId" class="text-caption text-success ml-2">
                                        (voucher applied — final total shown next)
                                    </span>
                                </div>
                                <v-btn color="primary" class="mt-4" :loading="creating" :disabled="!canAdvanceFromSelect"
                                    @click="advanceFromSelect">
                                    {{ extrasNeeded ? 'Continue to Add-ons' : 'Continue' }}
                                </v-btn>
                            </v-card-text>
                        </v-card>
                    </v-stepper-window-item>

                    <v-stepper-window-item v-if="extrasNeeded" value="extras">
                        <v-card class="mb-4 pa-4">
                            <v-card-title>Add-ons (optional)</v-card-title>
                            <v-card-text>
                                <ExtrasPicker :extras="eligibleExtras" v-model="extraSelections" />
                                <div class="d-flex align-center mt-4 ga-2 flex-wrap">
                                    <v-btn variant="text" @click="step = 'select'">Back to Select Pass</v-btn>
                                    <v-spacer></v-spacer>
                                    <v-btn color="primary" @click="advanceFromExtras">Continue</v-btn>
                                </div>
                            </v-card-text>
                        </v-card>
                    </v-stepper-window-item>

                    <v-stepper-window-item value="discounts">
                        <v-card class="mb-4 pa-4">
                            <v-card-title>Promo &amp; gift codes</v-card-title>
                            <v-card-text>
                                <p class="text-body-2 text-medium-emphasis mb-3">
                                    Have a code? Enter it below — otherwise just continue.
                                </p>
                                <v-text-field v-model="couponCode" label="Promo code (optional)"
                                              placeholder="SUMMER25" density="compact"
                                              :hide-details="false" :error-messages="couponError ? [couponError] : []"></v-text-field>
                                <v-text-field v-model="giftCardCode" label="Gift card code (optional)"
                                              placeholder="GIFT-XXXXXXXX" density="compact" class="mt-3"
                                              :hide-details="false" :error-messages="giftCardError ? [giftCardError] : []"></v-text-field>
                                <div class="text-caption text-medium-emphasis mt-3">
                                    Service charge and any voucher / coupon / gift card discounts apply at the payment step.
                                </div>
                                <div class="d-flex align-center mt-4 ga-2 flex-wrap">
                                    <v-btn variant="text" @click="step = extrasNeeded ? 'extras' : 'select'">Back</v-btn>
                                    <v-spacer></v-spacer>
                                    <v-btn color="primary" :loading="creating" @click="advanceFromDiscounts">
                                        {{ waiverNeeded ? 'Continue to Waiver' : 'Continue to Payment' }}
                                    </v-btn>
                                </div>
                            </v-card-text>
                        </v-card>
                    </v-stepper-window-item>

                    <v-stepper-window-item v-if="waiverNeeded" value="waiver">
                        <v-card class="mb-4 pa-4" v-if="waiver">
                            <v-card-title>{{ waiver.title }}</v-card-title>
                            <v-card-subtitle class="mb-2">Version {{ waiver.version }}</v-card-subtitle>
                            <v-card-text>
                                <p class="text-body-2 text-medium-emphasis mb-2">
                                    Your selection requires you to sign the waiver before purchase.
                                </p>
                                <v-alert v-if="riderIsMinor" type="info" variant="tonal" density="compact" class="mb-3">
                                    You're under 18 — a parent or guardian must sign on your behalf.
                                    Please hand the device to them and have them fill in their info below.
                                </v-alert>
                                <div v-if="hasBody(waiver.body)" class="waiver-body">
                                    <RichTextView :html="waiver.body" />
                                </div>
                                <div v-else class="text-medium-emphasis">
                                    (Tenant has not filled in waiver text yet. Ask them to do so.)
                                </div>
                                <v-row v-if="riderIsMinor" class="mt-2">
                                    <v-col cols="12" md="6">
                                        <v-text-field v-model="parentName" label="Parent / guardian name" density="compact" required></v-text-field>
                                    </v-col>
                                    <v-col cols="12" md="6">
                                        <PhoneField v-model="parentPhone" label="Parent / guardian phone" density="compact" required />
                                    </v-col>
                                </v-row>
                                <div class="mt-4">
                                    <div class="text-subtitle-2 mb-1">{{ riderIsMinor ? 'Parent signs below' : 'Sign below' }}</div>
                                    <SignaturePad v-model="signatureDataUrl" />
                                </div>
                                <div class="d-flex align-center mt-4 ga-2 flex-wrap">
                                    <v-btn variant="text" @click="step = 'discounts'">Back</v-btn>
                                    <v-spacer></v-spacer>
                                    <v-btn color="primary" :loading="signing" :disabled="!canSign" @click="signAndContinue">
                                        I agree, sign &amp; continue
                                    </v-btn>
                                </div>
                            </v-card-text>
                        </v-card>
                    </v-stepper-window-item>

                    <v-stepper-window-item value="payment">
                        <v-card class="mb-4 pa-4">
                            <v-card-title>Payment</v-card-title>
                            <v-card-text>
                                <div v-if="!branding.stripePublishableKey" class="text-error">
                                    Stripe publishable key is not configured.
                                </div>
                                <div v-else>
                                    <v-table v-if="riderServiceChargeCents > 0 || giftCardAppliedCents > 0" density="compact" class="mb-3">
                                        <tbody>
                                            <tr><td>Subtotal</td><td class="text-right">${{ ((amountCents + giftCardAppliedCents - riderServiceChargeCents) / 100).toFixed(2) }}</td></tr>
                                            <tr v-if="riderServiceChargeCents > 0"><td>Service charge</td><td class="text-right">${{ (riderServiceChargeCents / 100).toFixed(2) }}</td></tr>
                                            <tr v-if="giftCardAppliedCents > 0"><td>Gift card applied</td><td class="text-right">−${{ (giftCardAppliedCents / 100).toFixed(2) }}</td></tr>
                                            <tr><td><strong>Total</strong></td><td class="text-right"><strong>${{ displayAmount() }}</strong></td></tr>
                                        </tbody>
                                    </v-table>
                                    <div id="payment-element" class="mb-4"></div>
                                    <v-btn color="primary" :loading="paying" :disabled="!stripeReady" @click="pay">Pay ${{ displayAmount() }}</v-btn>
                                    <div v-if="paymentError" class="text-error mt-3">{{ paymentError }}</div>
                                </div>
                            </v-card-text>
                        </v-card>
                    </v-stepper-window-item>
                </v-stepper-window>
            </v-stepper>

            <!-- Order Summary sits below the stepper so the focus stays on the active step;
                 the running total updates live as the rider picks pass + add-ons. Hidden
                 once the purchase is complete (the success/QR card takes over). -->
            <v-card v-if="eligibleProducts.length > 0 && !completed"
                class="mt-4 pa-3" variant="outlined">
                <div class="text-overline text-medium-emphasis mb-2">Order Summary</div>
                <v-table density="compact" class="bg-transparent">
                    <tbody>
                        <tr v-if="!selectedProduct && selectedExtraLines.length === 0">
                            <td colspan="2" class="text-medium-emphasis text-caption">
                                Pick a pass to see your total.
                            </td>
                        </tr>
                        <tr v-if="selectedProduct">
                            <td>
                                {{ selectedProduct.name }}<span v-if="quantity > 1"> × {{ quantity }}</span>
                            </td>
                            <td class="text-right">${{ (passSubtotalCents / 100).toFixed(2) }}</td>
                        </tr>
                        <tr v-for="line in selectedExtraLines" :key="line.productId">
                            <td>{{ line.name }} × {{ line.quantity }}</td>
                            <td class="text-right">${{ ((line.priceCents * line.quantity) / 100).toFixed(2) }}</td>
                        </tr>
                        <tr>
                            <td><strong>Total</strong></td>
                            <td class="text-right"><strong>${{ (orderTotalCents / 100).toFixed(2) }}</strong></td>
                        </tr>
                    </tbody>
                </v-table>
            </v-card>
        </template>

        <v-card v-if="completed" variant="outlined" class="mt-6 pa-4 text-center">
            <v-alert type="success" class="mb-4">
                Reservation complete! Show this QR at the gate.
            </v-alert>
            <QrCode v-if="redemptionToken" :value="redeemUrl(redemptionToken)" :size="260" />
            <div class="text-caption text-medium-emphasis mt-3">
                Status will show as "paid" once Stripe confirms. Find it later on
                <router-link to="/User/MyPasses">My Passes</router-link>.
            </div>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>

        <v-dialog v-model="membershipGateOpen" max-width="520" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Membership required</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="membershipGateOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="mb-2">{{ membershipGateMessage }}</p>
                    <p class="text-body-2 text-medium-emphasis">
                        Add the {{ branding.membershipName }}
                        (${{ (branding.membershipPriceCents / 100).toFixed(2) }})
                        to this reservation and we'll roll it into one charge — no need to leave the page.
                    </p>
                </v-card-text>
                <v-card-actions>
                    <v-btn @click="membershipGateOpen = false">Not now</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn color="primary" :loading="creating" @click="addMembershipAndRetry">
                        Add to cart (${{ (branding.membershipPriceCents / 100).toFixed(2) }})
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted, nextTick, watch, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { PassService, type WaiverDto, type WaiverSignatureStatus } from '@/services/PassService'
import { EventService, type EventDto, type EligiblePass } from '@/services/EventService'
import { RewardService, type RiderRewardRedemption } from '@/services/RewardService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'
import RichTextView from '@/components/RichTextView.vue'
import QrCode from '@/components/QrCode.vue'
import SignaturePad from '@/components/SignaturePad.vue'
import ExtrasPicker, { type ExtraSelection } from '@/components/ExtrasPicker.vue'
import PhoneField from '@/components/PhoneField.vue'

type StepKey = 'select' | 'extras' | 'discounts' | 'waiver' | 'payment'

const route = useRoute()
const router = useRouter()
const service = new PassService()
const eventService = new EventService()
const rewardService = new RewardService()

const event = ref<EventDto | null>(null)
const loadingEvent = ref(true)

const waiver = ref<WaiverDto | null>(null)
const sigStatus = ref<WaiverSignatureStatus | null>(null)
const signing = ref(false)
const signatureDataUrl = ref<string | null>(null)
const parentName = ref('')
const parentPhone = ref('')

const riderIsMinor = computed(() => sigStatus.value?.riderIsMinor === true)
const needsEmergencyContact = computed(() =>
    branding.requireEmergencyContact && sigStatus.value?.riderHasEmergencyContact === false)
const canSign = computed(() => {
    if (!signatureDataUrl.value) return false
    if (riderIsMinor.value) {
        if (!parentName.value.trim()) return false
        if (parentPhone.value.replace(/\D/g, '').length < 7) return false
    }
    return true
})

const selectedProductId = ref<string | null>(null)
const quantity = ref(1)
const creating = ref(false)

const availableVouchers = ref<RiderRewardRedemption[]>([])
const selectedVoucherId = ref<string | null>(null)
const couponCode = ref('')
const couponError = ref('')
watch(couponCode, () => { couponError.value = '' })

const giftCardCode = ref('')
const giftCardError = ref('')
watch(giftCardCode, () => { giftCardError.value = '' })

const purchaseId = ref<string | null>(null)
const redemptionToken = ref<string | null>(null)
const clientSecret = ref<string | null>(null)
const amountCents = ref(0)
const riderServiceChargeCents = ref(0)
const giftCardAppliedCents = ref(0)
const stripeReady = ref(false)
const paying = ref(false)
const paymentError = ref<string | null>(null)
const completed = ref(false)

let stripe: any = null
let elements: any = null

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

// Surfaced when the backend rejects with "Membership required". Riders bundle
// the membership into this same checkout via the dialog's "Add to cart" button.
const membershipGateOpen = ref(false)
const membershipGateMessage = ref('')
const addMembership = ref(false)

async function addMembershipAndRetry() {
    addMembership.value = true
    membershipGateOpen.value = false
    await createIntent()
}

const eligibleProducts = computed<EligiblePass[]>(() =>
    (event.value?.eligiblePasses ?? []).filter(p => p.isActive))
const selectedProduct = computed(() => eligibleProducts.value.find(p => p.id === selectedProductId.value) ?? null)

// Show every extra that's either available outright OR has variants (variant
// inventory is checked per-variant, so the product-level remaining=0 is meaningless).
const eligibleExtras = computed(() =>
    (event.value?.eligibleExtras ?? []).filter(e => e.remaining !== 0 || e.variants.length > 0))
const extrasNeeded = computed(() => eligibleExtras.value.length > 0)
// Selections come from <ExtrasPicker>: one row per (productId, variantId?) with qty > 0.
const extraSelections = ref<ExtraSelection[]>([])

function resolvedExtraPrice(s: ExtraSelection): number {
    const product = eligibleExtras.value.find(e => e.productId === s.productId)
    if (!product) return 0
    if (s.variantId) {
        const v = product.variants.find(x => x.id === s.variantId)
        if (v) return v.priceCents
    }
    return product.priceCents
}
function resolvedExtraName(s: ExtraSelection): string {
    const product = eligibleExtras.value.find(e => e.productId === s.productId)
    if (!product) return 'Add-on'
    if (s.variantId) {
        const v = product.variants.find(x => x.id === s.variantId)
        if (v) {
            const attrs = [v.size, v.color, v.gender].filter(x => !!x).join(' / ')
            return attrs ? `${product.name} (${attrs})` : product.name
        }
    }
    return product.name
}

const extrasTotalCents = computed(() =>
    extraSelections.value.reduce((sum, s) => sum + s.quantity * resolvedExtraPrice(s), 0))
const selectedExtraLines = computed(() =>
    extraSelections.value
        .filter(s => s.quantity > 0)
        .map(s => ({
            productId: s.productId + (s.variantId ?? ''),
            name: resolvedExtraName(s),
            priceCents: resolvedExtraPrice(s),
            quantity: s.quantity,
        })))
const extrasNeedWaiver = computed(() => {
    const productIdsWithQty = new Set(extraSelections.value.filter(s => s.quantity > 0).map(s => s.productId))
    return eligibleExtras.value.some(e => productIdsWithQty.has(e.productId) && e.requiresWaiver)
})

// Sticker-price preview totals. Final amount at payment time may differ once
// vouchers / coupons / rider service charge get applied server-side.
const passSubtotalCents = computed(() =>
    (selectedProduct.value?.priceCents ?? 0) * Math.max(1, quantity.value))
const orderTotalCents = computed(() => passSubtotalCents.value + extrasTotalCents.value)

const voucherOptions = computed(() => availableVouchers.value.map(v => ({
    value: v.id,
    title: `${v.programName} — ${v.rewardPercentOff === 100 ? 'Free' : v.rewardPercentOff + '% off'}`,
})))

const waiverNeeded = computed(() => {
    if (!waiver.value) return false
    if (sigStatus.value?.hasSignedCurrent) return false
    if (selectedProduct.value?.requiresWaiver) return true
    if (event.value?.requiresWaiver) return true
    if (extrasNeedWaiver.value) return true
    return false
})

const stepperItems = computed(() => {
    const items: { title: string; value: StepKey }[] = [{ title: 'Select Pass', value: 'select' }]
    if (extrasNeeded.value) items.push({ title: 'Add-ons', value: 'extras' })
    items.push({ title: 'Discounts', value: 'discounts' })
    if (waiverNeeded.value) items.push({ title: 'Waiver', value: 'waiver' })
    items.push({ title: 'Payment', value: 'payment' })
    return items
})

const step = ref<StepKey>('select')

const maxQuantity = computed(() => {
    if (!event.value?.capacity) return 50
    return Math.max(1, event.value.capacity - (event.value.spotsReserved ?? 0))
})

const canAdvanceFromSelect = computed(() => {
    if (!selectedProductId.value || quantity.value < 1) return false
    if (!event.value) return false
    return true
})

function displayAmount() { return (amountCents.value / 100).toFixed(2) }

function formatDate(iso: string | null | undefined): string {
    if (!iso) return ''
    return dayjs.utc(iso).tz(branding.timezone || 'UTC').format('YYYY-MM-DD HH:mm')
}
function formatLong(iso: string): string {
    return dayjs.utc(iso).tz(branding.timezone || 'UTC').format('dddd, MMM D · h:mm A')
}

onMounted(async () => {
    const queryEventId = route.query.eventId as string | undefined
    if (!queryEventId) {
        flash('Pick an event first.', 'error')
        router.replace('/Events')
        return
    }
    try {
        loadingEvent.value = true
        // Pull events in a window wide enough to find any reasonable target.
        // (The server has no single-event public endpoint; the list endpoint is
        // fine for MVP.) Going from -7 days to +1 year covers same-day clicks
        // on events the calendar showed in any month.
        const tz = branding.timezone || 'UTC'
        const fromUtc = dayjs().tz(tz).startOf('day').subtract(7, 'day').utc().toISOString()
        const toUtc = dayjs().tz(tz).startOf('day').add(365, 'day').utc().toISOString()
        const evResp = await eventService.list(fromUtc, toUtc)
        const all = (evResp.data as any).data as EventDto[]
        event.value = all.find(e => e.id === queryEventId) ?? null
        if (!event.value) {
            // Stay on the page with a visible error instead of redirecting — a
            // silent bounce back to /Events with a flash gets eaten by the
            // route transition and looks like the click did nothing.
            flash('Event not found or has already ended.', 'error')
            return
        }

        const [w, s, v] = await Promise.all([
            service.getWaiver().catch(() => ({ data: { data: null } })),
            service.getMySignatureStatus().catch(() => ({ data: { data: null } })),
            rewardService.listMyRedemptions().catch(() => ({ data: { data: [] } })),
        ])
        waiver.value = (w.data as any).data
        sigStatus.value = (s.data as any).data
        availableVouchers.value = ((v.data as any).data as RiderRewardRedemption[]).filter(r => !r.redeemedAtUtc)

        // If there's only one eligible product, pre-select it.
        if (eligibleProducts.value.length === 1) {
            selectedProductId.value = eligibleProducts.value[0].id
        }
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load event.', 'error')
    } finally {
        loadingEvent.value = false
    }
})

async function advanceFromSelect() {
    if (!canAdvanceFromSelect.value) return
    if (extrasNeeded.value) {
        step.value = 'extras'
    } else {
        step.value = 'discounts'
    }
}

function advanceFromExtras() {
    step.value = 'discounts'
}

async function advanceFromDiscounts() {
    if (waiverNeeded.value) {
        step.value = 'waiver'
    } else {
        await createIntent()
    }
}

async function signAndContinue() {
    if (!canSign.value) {
        flash(riderIsMinor.value ? 'Parent name + phone and signature are required.' : 'Please draw your signature.', 'error')
        return
    }
    try {
        signing.value = true
        const r = await service.signWaiver({
            signatureDataUrl: signatureDataUrl.value!,
            parentName: riderIsMinor.value ? parentName.value.trim() : null,
            parentPhone: riderIsMinor.value ? parentPhone.value.trim() : null,
        })
        sigStatus.value = (r.data as any).data
        await createIntent()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Sign failed.', 'error')
    } finally {
        signing.value = false
    }
}

async function createIntent() {
    if (!selectedProductId.value || !event.value) return
    try {
        creating.value = true
        const extras = extraSelections.value
            .filter(s => s.quantity > 0)
            .map(s => ({ productId: s.productId, quantity: s.quantity, variantId: s.variantId ?? null }))
        const body = {
            productId: selectedProductId.value,
            validOnDate: null,
            eventId: event.value.id,
            quantity: quantity.value,
            rewardRedemptionId: selectedVoucherId.value && quantity.value === 1 ? selectedVoucherId.value : null,
            couponCode: couponCode.value.trim().length > 0 ? couponCode.value.trim() : null,
            giftCardCode: giftCardCode.value.trim().length > 0 ? giftCardCode.value.trim() : null,
            extras: extras.length > 0 ? extras : null,
            addMembership: addMembership.value || undefined,
        }
        const r = await service.createPurchase(body)
        const data = (r.data as any).data
        purchaseId.value = data.purchaseId
        redemptionToken.value = data.redemptionToken
        clientSecret.value = data.clientSecret
        amountCents.value = data.amountCents
        riderServiceChargeCents.value = data.riderServiceChargeCents ?? 0
        giftCardAppliedCents.value = data.giftCardAppliedCents ?? 0

        if (!clientSecret.value && amountCents.value === 0) {
            completed.value = true
            const msg = giftCardAppliedCents.value > 0
                ? 'Gift card covered the pass — your spot is reserved!'
                : 'Voucher applied — your spot is reserved!'
            flash(msg, 'success')
            return
        }

        step.value = 'payment'
        await nextTick()
        await mountPaymentElement()
    } catch (err: any) {
        const message = err.response?.data?.error as string | undefined
        if (message && /membership/i.test(message)) {
            membershipGateMessage.value = message
            membershipGateOpen.value = true
        } else if (message && /coupon/i.test(message)) {
            couponError.value = message
        } else if (message && /gift card/i.test(message)) {
            giftCardError.value = message
        } else {
            flash(message || 'Failed to start payment.', 'error')
        }
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
    pe.mount('#payment-element')
    stripeReady.value = true
}

async function pay() {
    if (!stripe || !elements) return
    paying.value = true
    paymentError.value = null
    try {
        const { error } = await stripe.confirmPayment({
            elements,
            confirmParams: { return_url: window.location.origin + '/User/MyPasses' },
            redirect: 'if_required',
        })
        if (error) paymentError.value = error.message || 'Payment failed.'
        else completed.value = true
    } catch (err: any) {
        paymentError.value = err?.message || 'Payment failed.'
    } finally {
        paying.value = false
    }
}

function hasBody(body: string | null | undefined): boolean {
    if (!body) return false
    return body.replace(/<[^>]+>/g, '').trim().length > 0
}

function redeemUrl(token: string): string {
    return `${window.location.protocol}//${window.location.host}/redeem/${token}`
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

watch(quantity, (q) => {
    if (q > maxQuantity.value) quantity.value = maxQuantity.value
    if (q < 1) quantity.value = 1
})
</script>

<style scoped>
.waiver-body {
    background: rgba(0, 0, 0, 0.03);
    padding: 1rem;
    border-radius: 6px;
    max-height: 300px;
    overflow-y: auto;
}
</style>
