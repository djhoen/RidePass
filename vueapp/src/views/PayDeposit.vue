<template>
    <v-container class="pay-deposit" style="max-width: 560px">
        <v-card v-if="loading" class="pa-8 text-center"><v-progress-circular indeterminate color="primary" /></v-card>

        <v-alert v-else-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>

        <v-card v-else-if="info" class="pa-2">
            <v-card-title class="text-h5">Service deposit</v-card-title>
            <v-card-text>
                <p class="text-body-1 mb-1">Hi {{ info.customerName }},</p>
                <p class="text-body-2 text-medium-emphasis mb-4">
                    {{ branding.displayName }} requested a deposit for
                    {{ info.bikeDesc || 'your service order' }}.
                </p>

                <v-table density="compact" class="mb-4">
                    <tbody>
                        <tr v-for="(l, i) in info.lines" :key="i">
                            <td>{{ l.description || (l.kind === 'labor' ? 'Labor' : 'Part') }}
                                <span v-if="l.quantity > 1" class="text-medium-emphasis">× {{ l.quantity }}</span></td>
                            <td class="text-right">{{ money(l.unitPriceCents * l.quantity) }}</td>
                        </tr>
                        <tr>
                            <td class="font-weight-bold">Deposit due</td>
                            <td class="text-right font-weight-bold">{{ money(info.depositCents) }}</td>
                        </tr>
                    </tbody>
                </v-table>

                <v-alert v-if="info.cancelled" type="info" variant="tonal">
                    This service order was cancelled, so no deposit is due.
                </v-alert>
                <v-alert v-else-if="info.paid" type="success" variant="tonal">
                    This deposit has been paid. You're all set, see you at the shop!
                </v-alert>
                <template v-else>
                    <div id="deposit-payment-element" class="mb-4"></div>
                    <div v-if="payError" class="text-error text-body-2 mb-2">{{ payError }}</div>
                    <v-btn v-if="!stripeReady" block color="primary" size="large" :loading="starting" @click="startPayment">
                        Pay {{ money(info.depositCents) }}
                    </v-btn>
                    <v-btn v-else block color="primary" size="large" :loading="paying" @click="confirmPayment">
                        Pay {{ money(info.depositCents) }}
                    </v-btn>
                </template>
            </v-card-text>
        </v-card>
    </v-container>
</template>

<script setup lang="ts">
// Public page: the emailed link's token is the whole credential. No login required.
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { BikeShopService, type PublicShopDeposit } from '@/services/BikeShopService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'

const route = useRoute()
const service = new BikeShopService()
const token = route.params.token as string

const loading = ref(true)
const loadError = ref('')
const info = ref<PublicShopDeposit | null>(null)

const starting = ref(false)
const paying = ref(false)
const payError = ref('')
const stripeReady = ref(false)
let stripe: any = null
let elements: any = null

function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }

onMounted(async () => {
    try {
        info.value = (await service.getPublicDeposit(token)).data.data
    } catch (e: any) {
        loadError.value = e.response?.data?.error
            || 'This payment link is not valid. Please contact the shop if you believe this is a mistake.'
    } finally { loading.value = false }
})

async function startPayment() {
    payError.value = ''
    starting.value = true
    try {
        const r = await service.payPublicDeposit(token)
        const account = branding.stripeChargeMode === 'direct' ? branding.stripeConnectAccountId : null
        stripe = await getStripe(branding.stripePublishableKey, account)
        if (!stripe) { payError.value = 'Payments are unavailable right now. Please try again later.'; return }
        elements = stripe.elements({ clientSecret: r.data.data.clientSecret })
        elements.create('payment').mount('#deposit-payment-element')
        stripeReady.value = true
    } catch (e: any) {
        payError.value = e.response?.data?.error || 'Could not start the payment. Please try again.'
    } finally { starting.value = false }
}

async function confirmPayment() {
    if (!stripe || !elements) return
    paying.value = true
    payError.value = ''
    try {
        const { error, paymentIntent } = await stripe.confirmPayment({ elements, redirect: 'if_required' })
        if (error) {
            payError.value = error.message || 'Payment failed. Check the card details and try again.'
        } else if (paymentIntent?.status === 'succeeded') {
            if (info.value) info.value.paid = true
            stripeReady.value = false
        } else {
            payError.value = 'The payment has not settled yet. It will complete shortly; you can close this page.'
        }
    } catch (e: any) {
        payError.value = e?.message || 'Payment failed. Please try again.'
    } finally { paying.value = false }
}
</script>

<style scoped>
.pay-deposit {
    margin-top: 24px;
}
</style>
