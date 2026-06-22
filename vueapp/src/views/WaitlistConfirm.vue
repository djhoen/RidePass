<template>
    <v-container style="max-width: 540px">
        <v-progress-circular v-if="loading" indeterminate color="primary"></v-progress-circular>

        <template v-else-if="details">
            <!-- Pre-paid alternates auto-confirm at promotion time. Show the success state
                 and link to MyPasses for the QR. -->
            <v-card v-if="details.status === 'confirmed'" class="pa-4 text-center">
                <v-icon size="48" color="success" class="mb-2">mdi-check-decagram</v-icon>
                <h1 class="text-h5 mb-2">You're confirmed!</h1>
                <p class="text-body-2 text-medium-emphasis mb-3">
                    Your spot at <strong>{{ details.eventTitle }}</strong> on {{ formatLong(details.eventStartsAtUtc) }} is locked in.
                </p>
                <v-btn color="primary" to="/User/MyPasses">View My Passes</v-btn>
            </v-card>

            <v-card v-else-if="(details.status === 'expired' || expiredNow) && !clientSecret" class="pa-4 text-center">
                <v-icon size="48" color="grey" class="mb-2">mdi-clock-outline</v-icon>
                <h1 class="text-h5 mb-2">This window has closed</h1>
                <p class="text-body-2 text-medium-emphasis">
                    The spot has rolled to the next person in line.
                </p>
                <v-btn variant="text" to="/Events" class="mt-3">Back to Events</v-btn>
            </v-card>

            <v-card v-else-if="details.status !== 'promoted'" class="pa-4 text-center">
                <v-icon size="48" color="grey" class="mb-2">mdi-information-outline</v-icon>
                <h1 class="text-h5 mb-2">Nothing to confirm</h1>
                <p class="text-body-2 text-medium-emphasis">
                    This waitlist entry is {{ details.status }}.
                </p>
            </v-card>

            <template v-else>
                <v-card class="pa-4 mb-4" variant="tonal" color="warning">
                    <div class="text-overline" style="opacity: 0.85">A spot opened!</div>
                    <div class="text-h5 font-weight-bold">{{ details.eventTitle }}</div>
                    <div v-if="details.tierName" class="text-body-2 mt-1">
                        <v-icon size="small" class="mr-1">mdi-tag</v-icon>{{ details.tierName }}
                    </div>
                    <div class="text-body-2 mt-1">
                        <v-icon size="small" class="mr-1">mdi-calendar</v-icon>{{ formatLong(details.eventStartsAtUtc) }}
                    </div>
                    <div v-if="details.eventLocationLabel" class="text-body-2">
                        <v-icon size="small" class="mr-1">mdi-map-marker</v-icon>{{ details.eventLocationLabel }}
                    </div>
                    <v-divider class="my-3"></v-divider>
                    <div class="text-body-2">
                        <v-icon size="small" class="mr-1">mdi-clock-alert</v-icon>
                        <strong>{{ remainingLabel }}</strong> to confirm
                    </div>
                </v-card>

                <v-card v-if="!clientSecret" class="pa-4 mb-4">
                    <v-card-title>Confirm + Pay</v-card-title>
                    <v-card-text>
                        <!-- Day-pass alternates pick a product first. -->
                        <template v-if="!details.tierId">
                            <p class="text-body-2 mb-2">Pick the pass you want to use:</p>
                            <div v-if="details.eligiblePasses.length === 0" class="text-medium-emphasis">
                                No passes are currently accepted at this event, so there's nothing to claim here.
                                <div class="mt-3"><v-btn variant="tonal" to="/Events">Back to Events</v-btn></div>
                            </div>
                            <v-radio-group v-else v-model="selectedProductId">
                                <v-radio v-for="p in details.eligiblePasses" :key="p.id" :value="p.id">
                                    <template #label>
                                        <span><strong>{{ p.name }}</strong> — ${{ (p.priceCents / 100).toFixed(2) }}</span>
                                    </template>
                                </v-radio>
                            </v-radio-group>
                        </template>

                        <!-- Tier-based: amount is fixed. -->
                        <template v-else>
                            <div class="d-flex align-center text-body-1 mb-3">
                                <span>{{ details.tierName }}</span>
                                <v-spacer></v-spacer>
                                <strong>${{ ((details.tierPriceCents ?? 0) / 100).toFixed(2) }}</strong>
                            </div>
                        </template>

                        <v-btn v-if="details.tierId || details.eligiblePasses.length > 0"
                            color="primary" :loading="creating" :disabled="!canPay" @click="createPayIntent">
                            Continue to Payment
                        </v-btn>
                    </v-card-text>
                </v-card>

                <v-card v-else class="pa-4 mb-4">
                    <v-card-title>Pay</v-card-title>
                    <v-card-text>
                        <div v-if="!branding.stripePublishableKey" class="text-error">
                            Stripe publishable key is not configured.
                        </div>
                        <template v-else>
                            <div id="waitlist-pay-element" class="mb-4"></div>
                            <v-btn color="primary" :loading="paying" :disabled="!stripeReady" @click="pay">
                                Pay ${{ (amountCents / 100).toFixed(2) }} &amp; confirm
                            </v-btn>
                            <div v-if="paymentError" class="text-error mt-3">{{ paymentError }}</div>
                        </template>
                    </v-card-text>
                </v-card>
            </template>
        </template>

        <v-card v-else-if="loadError" class="pa-4 text-center">
            <v-icon size="48" color="error" class="mb-2">mdi-alert-circle-outline</v-icon>
            <p class="text-body-2">{{ loadError }}</p>
        </v-card>

        <v-card v-else class="pa-4 text-center">
            <v-icon size="48" color="grey" class="mb-2">mdi-link-off</v-icon>
            <p class="text-body-2 text-medium-emphasis">This confirm link isn't valid (or has already been used).</p>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, nextTick, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { WaitlistService, type ConfirmDetails } from '@/services/WaitlistService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'

const route = useRoute()
const router = useRouter()
const service = new WaitlistService()

const token = route.params.token as string
const details = ref<ConfirmDetails | null>(null)
const loading = ref(true)
const loadError = ref('')

const selectedProductId = ref<string | null>(null)
const creating = ref(false)

const clientSecret = ref<string | null>(null)
const amountCents = ref(0)
const stripeReady = ref(false)
const paying = ref(false)
const paymentError = ref<string | null>(null)
let stripe: any = null
let elements: any = null

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

// Live countdown — re-renders every 5s so the deadline label stays fresh.
const now = ref(Date.now())
let tick: number | null = null
onMounted(() => { tick = window.setInterval(() => { now.value = Date.now() }, 5000) })
onUnmounted(() => { if (tick !== null) clearInterval(tick) })

const expiredNow = computed(() => {
    if (!details.value?.confirmDeadlineUtc) return false
    return now.value > new Date(details.value.confirmDeadlineUtc).getTime()
})

const remainingLabel = computed(() => {
    if (!details.value?.confirmDeadlineUtc) return ''
    const ms = new Date(details.value.confirmDeadlineUtc).getTime() - now.value
    if (ms <= 0) return 'expired'
    const mins = Math.floor(ms / 60000)
    const secs = Math.floor((ms % 60000) / 1000)
    return mins > 0 ? `${mins}m ${secs}s` : `${secs}s`
})

const canPay = computed(() => {
    if (!details.value) return false
    if (details.value.tierId) return true
    return !!selectedProductId.value
})

function formatLong(iso: string): string {
    return dayjs.utc(iso).tz(branding.timezone || 'UTC').format('dddd, MMM D · h:mm A')
}

onMounted(async () => {
    try {
        const r = await service.confirmDetails(token)
        details.value = (r.data as any).data

        // If exactly one eligible pass, pre-select it.
        if (details.value && !details.value.tierId && details.value.eligiblePasses.length === 1) {
            selectedProductId.value = details.value.eligiblePasses[0].id
        }
    } catch (err: any) {
        if (err.response?.status === 401) {
            router.push({ path: '/Login', query: { next: route.fullPath } })
            return
        }
        details.value = null
        // 400 / 404 are a genuinely invalid or already-used link: show the friendly
        // "isn't valid" card. A 500 / network failure is a real error and must not be
        // masked as an invalid link, or the rider may lose their promoted spot.
        const st = err.response?.status
        if (st !== 400 && st !== 404) {
            loadError.value = err.response?.data?.error
                || 'Could not load your waitlist confirmation. Refresh to try again, or use the link from your email.'
        }
    } finally {
        loading.value = false
    }
})

async function createPayIntent() {
    if (!details.value || !canPay.value) return
    creating.value = true
    try {
        const r = await service.confirmAndPay(token, {
            passProductId: details.value.tierId ? null : selectedProductId.value,
        })
        const data = (r.data as any).data
        clientSecret.value = data.clientSecret
        amountCents.value = data.amountCents
        await nextTick()
        await mountStripe()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not start payment.', 'error')
    } finally {
        creating.value = false
    }
}

async function mountStripe() {
    if (!clientSecret.value) return
    stripe = await getStripe(branding.stripePublishableKey)
    if (!stripe) { paymentError.value = 'Stripe not available.'; return }
    elements = stripe.elements({ clientSecret: clientSecret.value })
    const pe = elements.create('payment')
    pe.mount('#waitlist-pay-element')
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
