<template>
    <v-container style="max-width: 720px">
        <h1 class="text-h4 mb-4">Season Passes</h1>

        <div v-if="loading" class="text-center py-8">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>
        <v-alert v-else-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>
        <v-card v-else-if="products.length === 0" class="pa-6 text-center text-medium-emphasis">
            No season passes available right now.
        </v-card>

        <v-card v-for="p in products" :key="p.id" class="mb-4 pa-4">
            <div class="d-flex align-center">
                <div class="flex-grow-1">
                    <strong class="text-h6">{{ p.name }}</strong>
                    <div v-if="p.description" class="text-caption text-medium-emphasis">{{ p.description }}</div>
                    <div class="text-caption mt-1">
                        Valid {{ formatDate(p.validFromDate) }} – {{ formatDate(p.validToDate) }}
                        <span v-if="p.kind === 'days_of_week'"> · {{ daysLabel(p.validDaysOfWeek) }} only</span>
                        <span v-else-if="p.kind === 'credits'"> · {{ p.totalCredits }} ride credits</span>
                        <span v-else> · unlimited rides</span>
                    </div>
                </div>
                <div class="text-right">
                    <div class="text-h5">${{ (p.priceCents / 100).toFixed(2) }}</div>
                    <v-btn color="primary" class="mt-1" @click="openPhotoStep(p)">Buy</v-btn>
                </div>
            </div>
        </v-card>

        <v-dialog v-model="photoStepOpen" max-width="480" persistent>
            <v-card v-if="selectedProduct">
                <v-card-title class="d-flex align-center">
                    <span>Take a photo</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="photoStepOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 text-medium-emphasis mb-3">
                        We snap a photo so the gate worker can confirm you're the pass holder.
                        Use a recent, well-lit selfie of just your face.
                    </p>
                    <PhotoCapture v-model="photoDataUrl" />

                    <div v-if="needsWaiverSigning && waiver" class="mt-4">
                        <div class="text-subtitle-2 mb-1">{{ waiver.title }}</div>
                        <div class="text-caption text-medium-emphasis mb-2" style="max-height: 160px; overflow-y: auto; white-space: pre-wrap; border: 1px solid rgba(0,0,0,0.12); border-radius: 4px; padding: 8px;">{{ waiver.body }}</div>
                        <v-text-field v-if="waiverIsMinor" v-model="parentName" label="Parent/guardian name"
                            density="compact" hide-details class="mb-2"></v-text-field>
                        <v-text-field v-if="waiverIsMinor" v-model="parentPhone" label="Parent/guardian phone"
                            density="compact" hide-details class="mb-2"></v-text-field>
                        <div class="text-caption mb-1">Sign below to agree to the waiver:</div>
                        <SignaturePad v-model="signatureDataUrl" />
                    </div>
                    <div v-else-if="selectedProduct.requiresWaiver && waiver" class="mt-4 text-caption text-success">
                        Waiver already signed.
                    </div>

                    <v-text-field v-model="couponCode" label="Promo code (optional)"
                        placeholder="SUMMER25" density="compact" class="mt-3"
                        :hide-details="false" :error-messages="couponError ? [couponError] : []"></v-text-field>
                    <v-text-field v-model="giftCardCode" label="Gift card code (optional)"
                        placeholder="GIFT-XXXXXXXX" density="compact" class="mt-3"
                        :hide-details="false" :error-messages="giftCardError ? [giftCardError] : []"></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="photoStepOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="busyId === selectedProduct.id"
                        :disabled="!photoDataUrl || (needsWaiverSigning && !signatureDataUrl)"
                        @click="buy(selectedProduct)">Continue to payment</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="payOpen" persistent max-width="500">
            <v-card v-if="purchaseInFlight">
                <v-card-title class="d-flex align-center">
                    <span>Pay for {{ purchaseInFlight.productName }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="payOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-table v-if="purchaseInFlight.riderServiceChargeCents > 0 || purchaseInFlight.giftCardAppliedCents > 0" density="compact" class="mb-3">
                        <tbody>
                            <tr><td>Subtotal</td><td class="text-right">${{ ((purchaseInFlight.amountCents + purchaseInFlight.giftCardAppliedCents - purchaseInFlight.riderServiceChargeCents) / 100).toFixed(2) }}</td></tr>
                            <tr v-if="purchaseInFlight.riderServiceChargeCents > 0"><td>Service charge</td><td class="text-right">${{ (purchaseInFlight.riderServiceChargeCents / 100).toFixed(2) }}</td></tr>
                            <tr v-if="purchaseInFlight.giftCardAppliedCents > 0"><td>Gift card applied</td><td class="text-right">−${{ (purchaseInFlight.giftCardAppliedCents / 100).toFixed(2) }}</td></tr>
                            <tr><td><strong>Total</strong></td><td class="text-right"><strong>${{ (purchaseInFlight.amountCents / 100).toFixed(2) }}</strong></td></tr>
                        </tbody>
                    </v-table>
                    <div id="season-payment-element" class="mb-4"></div>
                    <v-btn color="primary" :loading="paying" :disabled="!stripeReady" @click="pay">
                        Pay ${{ (purchaseInFlight.amountCents / 100).toFixed(2) }}
                    </v-btn>
                    <div v-if="paymentError" class="text-error mt-3">{{ paymentError }}</div>
                </v-card-text>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted, nextTick, watch } from 'vue'
import { useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { SeasonPassService, type SeasonPassProduct } from '@/services/SeasonPassService'
import { WaiverService, type WaiverDto } from '@/services/WaiverService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'
import PhotoCapture from '@/components/PhotoCapture.vue'
import SignaturePad from '@/components/SignaturePad.vue'

const router = useRouter()
const service = new SeasonPassService()
const waiverService = new WaiverService()

const products = ref<SeasonPassProduct[]>([])
const loading = ref(false)
const loadError = ref('')
const busyId = ref<string | null>(null)
const photoStepOpen = ref(false)
const photoDataUrl = ref<string | null>(null)
const selectedProduct = ref<SeasonPassProduct | null>(null)
const couponCode = ref('')
const couponError = ref('')
watch(couponCode, () => { couponError.value = '' })

const giftCardCode = ref('')
const giftCardError = ref('')
watch(giftCardCode, () => { giftCardError.value = '' })

// Waiver step (only when the pass requires one and the rider hasn't signed the current version).
const waiver = ref<WaiverDto | null>(null)
const needsWaiverSigning = ref(false)
const waiverIsMinor = ref(false)
const signatureDataUrl = ref<string | null>(null)
const parentName = ref('')
const parentPhone = ref('')

async function openPhotoStep(p: SeasonPassProduct) {
    selectedProduct.value = p
    photoDataUrl.value = null
    signatureDataUrl.value = null
    parentName.value = ''
    parentPhone.value = ''
    waiver.value = null
    needsWaiverSigning.value = false
    waiverIsMinor.value = false
    photoStepOpen.value = true
    if (p.requiresWaiver) {
        try {
            waiver.value = ((await waiverService.getActive()).data as any).data
            const status = ((await waiverService.getMySignatureFor(waiver.value!.id)).data as any).data
            needsWaiverSigning.value = !status.hasSignedCurrent
            waiverIsMinor.value = status.riderIsMinor
        } catch {
            // No active waiver configured or fetch failed: leave signing off. The server still
            // gates the purchase if a waiver is genuinely required.
        }
    }
}

const payOpen = ref(false)
const purchaseInFlight = ref<{ purchaseId: string; productName: string; amountCents: number; riderServiceChargeCents: number; giftCardAppliedCents: number; clientSecret: string } | null>(null)
const stripeReady = ref(false)
const paying = ref(false)
const paymentError = ref<string | null>(null)
let stripe: any = null
let elements: any = null

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

function formatDate(iso: string): string { return dayjs(iso).format('MMM D, YYYY') }
function daysLabel(days: number[] | null): string {
    if (!days || days.length === 0) return ''
    const names = ['Sun','Mon','Tue','Wed','Thu','Fri','Sat']
    return days.slice().sort().map(d => names[d]).join('/')
}

onMounted(async () => {
    // Feature off: bounce to home so the public Buy page isn't reachable directly.
    if (branding.loaded && !branding.seasonPassesEnabled) {
        router.replace('/')
        return
    }
    loading.value = true
    loadError.value = ''
    try {
        const r = await service.listActive()
        products.value = (r.data as any).data
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            || 'Could not load season passes. Refresh to try again, or check your connection.'
    } finally { loading.value = false }
})

async function buy(p: SeasonPassProduct) {
    if (!photoDataUrl.value) {
        flash('Please take your photo first.', 'error')
        return
    }
    if (needsWaiverSigning.value) {
        if (!signatureDataUrl.value) { flash('Please sign the waiver to continue.', 'error'); return }
        if (waiverIsMinor.value && (!parentName.value.trim() || parentPhone.value.trim().length < 7)) {
            flash('Riders under 18 need a parent or guardian name and phone number.', 'error'); return
        }
    }
    busyId.value = p.id
    try {
        // Sign the waiver first so it's on file before the purchase (which the server requires).
        if (needsWaiverSigning.value && waiver.value) {
            await waiverService.sign(waiver.value.id, {
                signatureDataUrl: signatureDataUrl.value!,
                parentName: waiverIsMinor.value ? parentName.value.trim() : null,
                parentPhone: waiverIsMinor.value ? parentPhone.value.trim() : null,
            })
            needsWaiverSigning.value = false
        }
        const r = await service.buy(p.id, photoDataUrl.value,
            couponCode.value.trim().length > 0 ? couponCode.value.trim() : null,
            giftCardCode.value.trim().length > 0 ? giftCardCode.value.trim() : null)
        const data = (r.data as any).data
        purchaseInFlight.value = { ...data, productName: p.name }
        photoStepOpen.value = false

        // If gift card fully covered the pass, server returned no clientSecret — go straight to receipt.
        if (!data.clientSecret && data.amountCents === 0) {
            flash('Gift card covered the pass — your pass is ready!', 'success')
            router.push('/User/SeasonPasses')
            return
        }

        payOpen.value = true
        await nextTick()
        await mountStripe()
    } catch (err: any) {
        const message = err.response?.data?.error as string | undefined
        if (message && /coupon/i.test(message)) {
            couponError.value = message
        } else if (message && /gift card/i.test(message)) {
            giftCardError.value = message
        } else {
            flash(message || 'Could not start purchase.', 'error')
        }
    } finally {
        busyId.value = null
    }
}

async function mountStripe() {
    if (!purchaseInFlight.value) return
    stripe = await getStripe(branding.stripePublishableKey)
    if (!stripe) { paymentError.value = 'Stripe not available.'; return }
    elements = stripe.elements({ clientSecret: purchaseInFlight.value.clientSecret })
    const pe = elements.create('payment')
    pe.mount('#season-payment-element')
    stripeReady.value = true
}

async function pay() {
    if (!stripe || !elements) return
    paying.value = true
    paymentError.value = null
    try {
        const { error } = await stripe.confirmPayment({
            elements,
            confirmParams: { return_url: window.location.origin + '/User/SeasonPasses' },
            redirect: 'if_required',
        })
        if (error) paymentError.value = error.message || 'Payment failed.'
        else router.push('/User/SeasonPasses')
    } catch (err: any) {
        paymentError.value = err?.message || 'Payment failed.'
    } finally {
        paying.value = false
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
