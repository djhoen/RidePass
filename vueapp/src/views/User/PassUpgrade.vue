<template>
    <v-container>
        <v-btn variant="text" size="small" prepend-icon="mdi-arrow-left" to="/User/MyPasses" class="mb-2">
            My Passes
        </v-btn>
        <h1 class="text-h4 mb-6">Upgrade your pass</h1>

        <v-progress-circular v-if="loading" indeterminate color="primary" />

        <v-alert v-else-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>

        <v-alert v-else-if="offers.length === 0" type="info" variant="tonal">
            There are no upgrades available for this pass right now.
        </v-alert>

        <template v-else>
            <!-- Paid, so the rider sees the new pass on My Passes; nothing else to do here. -->
            <v-card v-if="purchased" variant="tonal" color="success" class="pa-4">
                <div class="d-flex align-center">
                    <v-icon class="mr-3">mdi-check-decagram</v-icon>
                    <div>
                        <div class="text-h6">You're upgraded.</div>
                        <div class="text-body-2">
                            Your new pass is on
                            <router-link to="/User/MyPasses">My Passes</router-link>,
                            with a new QR code. The old one no longer works.
                        </div>
                    </div>
                </div>
            </v-card>

            <template v-else>
                <p class="text-body-2 text-medium-emphasis mb-4">
                    You currently hold <strong>{{ offers[0].fromProductName }}</strong>.
                </p>

                <v-row>
                    <v-col v-for="o in offers" :key="o.pathId" cols="12" md="6">
                        <v-card variant="outlined" class="pa-4 h-100 d-flex flex-column">
                            <div class="text-h6 mb-1">{{ o.toProductName }}</div>
                            <div v-if="o.toProductDescription" class="text-body-2 mb-2">
                                {{ o.toProductDescription }}
                            </div>
                            <div class="text-caption text-medium-emphasis mb-3">
                                <div v-if="o.toProductKind === 'credits' && o.toProductTotalCredits != null">
                                    {{ o.toProductTotalCredits }} ride credits
                                </div>
                                <div v-else>Unlimited rides while valid</div>
                                <div>
                                    Valid {{ dateOnly(o.toValidFromDate) }} to {{ dateOnly(o.toValidToDate) }}
                                </div>
                            </div>
                            <div class="text-h5 mb-3">
                                {{ o.priceCents <= 0 ? 'Free' : `$${(o.priceCents / 100).toFixed(2)}` }}
                            </div>
                            <v-spacer />
                            <v-btn color="primary" block :disabled="!!selected" :loading="creating === o.pathId"
                                @click="start(o)">
                                {{ o.priceCents <= 0 ? 'Upgrade' : 'Upgrade for $' + (o.priceCents / 100).toFixed(2) }}
                            </v-btn>
                        </v-card>
                    </v-col>
                </v-row>

                <!-- What they give up. Stated before payment, not after. -->
                <v-alert type="warning" variant="tonal" density="compact" class="mt-4">
                    Upgrading replaces your current pass. It stops working right away, you get a new
                    QR code, and the price you already paid is not refunded.
                    <span v-if="hasCredits">Ride credits left on your current pass do not carry over.</span>
                </v-alert>

                <!-- Checkout, shown only once an upgrade is picked. -->
                <v-card v-if="selected && clientSecret" variant="outlined" class="pa-4 mt-4">
                    <div class="text-h6 mb-3">Pay for {{ selected.toProductName }}</div>
                    <div v-if="!branding.stripePublishableKey" class="text-error">
                        Stripe publishable key is not configured. Please contact the track.
                    </div>
                    <template v-else>
                        <div id="upgrade-pay-element" class="mb-4"></div>
                        <v-btn color="primary" :loading="paying" :disabled="!stripeReady" @click="pay">
                            Pay ${{ (selected.priceCents / 100).toFixed(2) }}
                        </v-btn>
                        <v-btn variant="text" class="ml-2" :disabled="paying" @click="cancelCheckout">
                            Choose a different upgrade
                        </v-btn>
                        <div v-if="paymentError" class="text-error mt-3">{{ paymentError }}</div>
                    </template>
                </v-card>
            </template>
        </template>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
import { useRoute } from 'vue-router'
import { SeasonPassService, type UpgradeOfferItem } from '@/services/SeasonPassService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'

const route = useRoute()
const service = new SeasonPassService()
const passId = String(route.params.passPurchaseId || '')

const offers = ref<UpgradeOfferItem[]>([])
const loading = ref(true)
const loadError = ref('')

const creating = ref<string | null>(null)
const selected = ref<UpgradeOfferItem | null>(null)
const clientSecret = ref<string | null>(null)
const stripeReady = ref(false)
const paying = ref(false)
const paymentError = ref<string | null>(null)
const purchased = ref(false)
let stripe: any = null
let elements: any = null

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const hasCredits = computed(() => offers.value.some(o => o.toProductKind === 'credits'))

function dateOnly(d: string) { return String(d).substring(0, 10) }

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        const { data } = await service.myUpgrades()
        // The endpoint returns offers across all my passes; this page is about one of them.
        offers.value = data.data.filter(o => o.passPurchaseId === passId)
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            ?? 'Could not load your upgrade options. Refresh to try again.'
    } finally {
        loading.value = false
    }
}

async function start(o: UpgradeOfferItem) {
    creating.value = o.pathId
    paymentError.value = null
    try {
        const { data } = await service.buyUpgrade(passId, o.pathId)
        if (!data.data.clientSecret) {
            // Free upgrade: the server already completed it.
            purchased.value = true
            return
        }
        selected.value = o
        clientSecret.value = data.data.clientSecret
        await nextTick()
        await mountPayElement()
    } catch (err: any) {
        flash(err.response?.data?.error
            ?? 'Could not start the upgrade. You have not been charged; please try again.', 'error')
    } finally {
        creating.value = null
    }
}

async function mountPayElement() {
    if (!clientSecret.value) return
    // Direct-charge tenants confirm on their own connected account; platform tenants pass none.
    const stripeAccount = branding.stripeChargeMode === 'direct' ? branding.stripeConnectAccountId : null
    stripe = await getStripe(branding.stripePublishableKey, stripeAccount)
    if (!stripe) { paymentError.value = 'Payment could not be set up. Please contact the track.'; return }
    elements = stripe.elements({ clientSecret: clientSecret.value })
    elements.create('payment').mount('#upgrade-pay-element')
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
        if (error) {
            paymentError.value = error.message || 'Your payment was not completed. Please try again.'
        } else {
            purchased.value = true
        }
    } catch (err: any) {
        paymentError.value = err?.message || 'Your payment was not completed. Please try again.'
    } finally {
        paying.value = false
    }
}

// The pending replacement pass stays pending and is swept up like any other abandoned
// checkout, so backing out here is safe; the old pass is untouched until payment lands.
function cancelCheckout() {
    selected.value = null
    clientSecret.value = null
    stripeReady.value = false
    stripe = null
    elements = null
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

onMounted(() => {
    // Return from a redirect-based method (3DS / wallet): Stripe sends the browser back to
    // My Passes, so the only redirect landing here is a same-tab retry. Load normally.
    load()
})
</script>
