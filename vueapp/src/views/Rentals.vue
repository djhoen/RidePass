<template>
    <v-container style="max-width: 720px">
        <h1 class="text-h4 mb-4">Rentals</h1>

        <div v-if="loading" class="text-center py-8">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>
        <v-card v-else-if="!branding.rentalsEnabled || products.length === 0" class="pa-6 text-center text-medium-emphasis">
            No rentals available right now.
        </v-card>

        <v-card v-for="p in products" :key="p.id" class="mb-4 pa-4">
            <div class="d-flex align-center">
                <div class="flex-grow-1">
                    <strong class="text-h6">{{ p.name }}</strong>
                    <div v-if="p.description" class="text-caption text-medium-emphasis">{{ p.description }}</div>
                    <div class="text-caption mt-1">
                        ${{ (p.dailyRateCents / 100).toFixed(2) }}/day
                        <span v-if="p.depositCents > 0"> · ${{ (p.depositCents / 100).toFixed(2) }} deposit (refunded at return)</span>
                    </div>
                </div>
                <v-btn color="primary" @click="openBookDialog(p)">Book</v-btn>
            </div>
        </v-card>

        <v-dialog v-model="bookOpen" max-width="540" persistent>
            <v-card v-if="selected">
                <v-card-title>Book {{ selected.name }}</v-card-title>
                <v-card-text>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="form.startDate" type="date" label="Start date" density="compact"
                                :min="todayIso"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="form.endDate" type="date" label="End date" density="compact"
                                :min="form.startDate || todayIso"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-text-field v-model.number="form.quantity" type="number" min="1" max="10" class="mt-6"
                        label="Quantity" density="compact"></v-text-field>

                    <v-text-field v-model="couponCode" label="Promo code (optional)"
                        placeholder="SUMMER25" density="compact" class="mt-6"
                        :error-messages="couponError ? [couponError] : []"></v-text-field>
                    <v-text-field v-model="giftCardCode" label="Gift card code (optional)"
                        placeholder="GIFT-XXXXXXXX" density="compact" class="mt-6"
                        :error-messages="giftCardError ? [giftCardError] : []"></v-text-field>

                    <v-divider class="my-3"></v-divider>
                    <div class="text-body-2">
                        <div class="d-flex"><span>{{ daysCount }} day{{ daysCount === 1 ? '' : 's' }} × {{ form.quantity }} unit{{ form.quantity === 1 ? '' : 's' }} × ${{ (selected.dailyRateCents / 100).toFixed(2) }}</span>
                            <v-spacer></v-spacer>
                            <strong>${{ (rentalSubtotalCents / 100).toFixed(2) }}</strong>
                        </div>
                        <div v-if="selected.depositCents > 0" class="d-flex">
                            <span>Deposit ({{ form.quantity }} × ${{ (selected.depositCents / 100).toFixed(2) }})</span>
                            <v-spacer></v-spacer>
                            <strong>${{ ((selected.depositCents * form.quantity) / 100).toFixed(2) }}</strong>
                        </div>
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="bookOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="creating" :disabled="!canBook" @click="createIntent">
                        Continue to Payment
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="payOpen" persistent max-width="500">
            <v-card v-if="purchaseInFlight">
                <v-card-title>Pay for {{ purchaseInFlight.productName }}</v-card-title>
                <v-card-text>
                    <v-table density="compact" class="mb-3">
                        <tbody>
                            <tr><td>Rental fee</td><td class="text-right">${{ (purchaseInFlight.rentalFeeCents / 100).toFixed(2) }}</td></tr>
                            <tr v-if="purchaseInFlight.depositCents > 0"><td>Deposit (refunded at return)</td><td class="text-right">${{ (purchaseInFlight.depositCents / 100).toFixed(2) }}</td></tr>
                            <tr v-if="purchaseInFlight.giftCardAppliedCents > 0"><td>Gift card applied</td><td class="text-right">−${{ (purchaseInFlight.giftCardAppliedCents / 100).toFixed(2) }}</td></tr>
                            <tr><td><strong>Total</strong></td><td class="text-right"><strong>${{ (purchaseInFlight.amountCents / 100).toFixed(2) }}</strong></td></tr>
                        </tbody>
                    </v-table>
                    <div id="rental-payment-element" class="mb-4"></div>
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
import { ref, computed, onMounted, watch, nextTick } from 'vue'
import { useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { RentalService, type RentalProduct } from '@/services/RentalService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'

const service = new RentalService()
const router = useRouter()

const products = ref<RentalProduct[]>([])
const loading = ref(false)

const todayIso = dayjs().format('YYYY-MM-DD')

const bookOpen = ref(false)
const selected = ref<RentalProduct | null>(null)
const form = ref({
    startDate: todayIso,
    endDate: todayIso,
    quantity: 1,
})
const couponCode = ref('')
const couponError = ref('')
const giftCardCode = ref('')
const giftCardError = ref('')
watch(couponCode, () => { couponError.value = '' })
watch(giftCardCode, () => { giftCardError.value = '' })

const creating = ref(false)
const payOpen = ref(false)
const purchaseInFlight = ref<{
    productName: string
    amountCents: number
    rentalFeeCents: number
    depositCents: number
    giftCardAppliedCents: number
    clientSecret: string
} | null>(null)
const stripeReady = ref(false)
const paying = ref(false)
const paymentError = ref<string | null>(null)
let stripe: any = null
let elements: any = null

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const daysCount = computed(() => {
    const s = dayjs(form.value.startDate)
    const e = dayjs(form.value.endDate)
    if (!s.isValid() || !e.isValid() || e.isBefore(s, 'day')) return 0
    return e.diff(s, 'day') + 1
})

const rentalSubtotalCents = computed(() =>
    selected.value ? selected.value.dailyRateCents * daysCount.value * form.value.quantity : 0
)

const canBook = computed(() => {
    if (!selected.value) return false
    if (daysCount.value < 1) return false
    if (form.value.quantity < 1) return false
    return true
})

onMounted(async () => {
    loading.value = true
    try {
        const r = await service.listActive()
        products.value = (r.data as any).data
    } finally { loading.value = false }
})

function openBookDialog(p: RentalProduct) {
    selected.value = p
    form.value = { startDate: todayIso, endDate: todayIso, quantity: 1 }
    couponCode.value = ''
    giftCardCode.value = ''
    bookOpen.value = true
}

async function createIntent() {
    if (!selected.value || !canBook.value) return
    creating.value = true
    try {
        const r = await service.buy({
            productId: selected.value.id,
            startDate: form.value.startDate,
            endDate: form.value.endDate,
            quantity: form.value.quantity,
            couponCode: couponCode.value.trim() || null,
            giftCardCode: giftCardCode.value.trim() || null,
        })
        const data = (r.data as any).data
        purchaseInFlight.value = {
            productName: selected.value.name,
            amountCents: data.amountCents,
            rentalFeeCents: data.rentalFeeCents,
            depositCents: data.depositCents,
            giftCardAppliedCents: data.giftCardAppliedCents,
            clientSecret: data.clientSecret,
        }
        bookOpen.value = false

        // Free fast-path: gift card fully covered. Skip Stripe.
        if (!data.clientSecret && data.amountCents === 0) {
            flash('Gift card covered the booking — your rental is reserved!', 'success')
            router.push('/User/MyPasses')
            return
        }

        payOpen.value = true
        await nextTick()
        await mountStripe()
    } catch (err: any) {
        const message = err.response?.data?.error as string | undefined
        if (message && /coupon/i.test(message)) couponError.value = message
        else if (message && /gift card/i.test(message)) giftCardError.value = message
        else flash(message || 'Could not start booking.', 'error')
    } finally {
        creating.value = false
    }
}

async function mountStripe() {
    if (!purchaseInFlight.value) return
    stripe = await getStripe(branding.stripePublishableKey)
    if (!stripe) { paymentError.value = 'Stripe not available.'; return }
    elements = stripe.elements({ clientSecret: purchaseInFlight.value.clientSecret })
    const pe = elements.create('payment')
    pe.mount('#rental-payment-element')
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
        else router.push('/User/MyPasses')
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
