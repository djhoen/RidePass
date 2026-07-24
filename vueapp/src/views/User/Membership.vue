<template>
    <v-container style="max-width: 640px">
        <h1 class="text-h4 mb-4">Membership</h1>

        <v-progress-circular v-if="loading" indeterminate color="primary"></v-progress-circular>

        <template v-else>
            <v-alert v-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>

            <!-- Sales-disabled fallback. -->
            <v-card v-else-if="!status?.enabled" class="pa-6 text-center" variant="outlined">
                <v-icon size="48" color="grey" class="mb-2">mdi-card-account-details-outline</v-icon>
                <p class="text-body-2">This track doesn't sell memberships.</p>
            </v-card>

            <template v-else>
                <!-- Active membership card. -->
                <v-card v-if="status.active" class="mb-4 pa-4" variant="tonal" color="success">
                    <div class="d-flex align-center">
                        <v-icon class="mr-2">mdi-check-decagram</v-icon>
                        <div>
                            <div class="text-h6">{{ status.active.name }} — active</div>
                            <div class="text-body-2">
                                <span v-if="status.active.validToUtc">
                                    Valid through {{ formatDate(status.active.validToUtc) }}
                                </span>
                                <span v-else>Lifetime — never expires.</span>
                            </div>
                        </div>
                    </div>
                </v-card>

                <!-- Buy / renew card. -->
                <v-card v-if="!purchased" class="mb-4 pa-4">
                    <v-card-title>{{ status.active ? 'Renew membership' : 'Buy membership' }}</v-card-title>
                    <v-card-text>
                        <v-card variant="outlined" class="pa-3 mb-4">
                            <div class="d-flex align-center">
                                <div>
                                    <div class="text-h6">{{ status.name }}</div>
                                    <div class="text-caption text-medium-emphasis">
                                        {{ status.durationKind === 'yearly' ? 'Yearly · valid 365 days from purchase' : 'One-time · lifetime' }}
                                    </div>
                                </div>
                                <v-spacer></v-spacer>
                                <div class="text-h5">${{ (status.priceCents / 100).toFixed(2) }}</div>
                            </div>
                        </v-card>

                        <div v-if="requiredFor.length > 0" class="text-caption text-medium-emphasis mb-3">
                            Required for: {{ requiredFor.join(', ') }}.
                        </div>

                        <div v-if="!clientSecret">
                            <v-btn color="primary" :loading="creating" @click="createIntent">
                                Continue to Payment
                            </v-btn>
                        </div>
                        <div v-else>
                            <div v-if="!branding.stripePublishableKey" class="text-error">Stripe publishable key is not configured.</div>
                            <template v-else>
                                <div id="membership-pay-element" class="mb-4"></div>
                                <v-btn color="primary" :loading="paying" :disabled="!stripeReady" @click="pay">
                                    Pay ${{ (amountCents / 100).toFixed(2) }}
                                </v-btn>
                                <div v-if="paymentError" class="text-error mt-3">{{ paymentError }}</div>
                            </template>
                        </div>
                    </v-card-text>
                </v-card>

                <v-card v-else class="mb-4 pa-4" variant="tonal" color="success">
                    <div class="d-flex align-center">
                        <v-icon class="mr-2">mdi-check-decagram</v-icon>
                        <div>
                            <div class="text-h6">You're a member!</div>
                            <div v-if="returnTo" class="text-body-2">
                                <router-link :to="returnTo">Return to your purchase →</router-link>
                            </div>
                        </div>
                    </div>
                </v-card>

                <!-- Purchase history (paid only). -->
                <v-card v-if="paidHistory.length > 0" class="pa-4">
                    <v-card-title>History</v-card-title>
                    <v-table density="compact">
                        <thead>
                            <tr>
                                <th>Bought</th>
                                <th>Valid</th>
                                <th class="text-right">Amount</th>
                                <th>Status</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="h in paidHistory" :key="h.id">
                                <td>{{ formatDate(h.createdAtUtc) }}</td>
                                <td>
                                    {{ formatDate(h.validFromUtc) }}
                                    <span v-if="h.validToUtc"> → {{ formatDate(h.validToUtc) }}</span>
                                    <span v-else> → lifetime</span>
                                </td>
                                <td class="text-right">${{ (h.amountCents / 100).toFixed(2) }}</td>
                                <td>
                                    <v-chip size="small" :color="statusColor(h.status)">{{ h.status }}</v-chip>
                                </td>
                            </tr>
                        </tbody>
                    </v-table>
                </v-card>
            </template>
        </template>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
        <InlineAuthDialog v-model="authDialogOpen" @authed="onAuthed" />
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import authHelper from '@/helpers/AuthHelper'
import InlineAuthDialog from '@/components/InlineAuthDialog.vue'
import { MembershipService, type MembershipStatus } from '@/services/MembershipService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'

const route = useRoute()
const service = new MembershipService()
const authDialogOpen = ref(false)
function onAuthed() { createIntent() }

const status = ref<MembershipStatus | null>(null)
const loading = ref(true)
const loadError = ref('')

const creating = ref(false)
const clientSecret = ref<string | null>(null)
const amountCents = ref(0)
const stripeReady = ref(false)
const paying = ref(false)
const paymentError = ref<string | null>(null)
const purchased = ref(false)
let stripe: any = null
let elements: any = null

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

// `next=` query param lets gated checkouts deep-link a rider here and bring them back.
const returnTo = computed(() => (route.query.next as string | undefined) || null)

const requiredFor = computed(() => {
    if (!status.value) return []
    const out: string[] = []
    if (status.value.requiredForPass) out.push('pass reservations')
    if (status.value.requiredForEventTicket) out.push('event tickets')
    if (status.value.requiredForSeasonPass) out.push('season passes')
    if (status.value.requiredForExtras) out.push('add-ons')
    return out
})

const paidHistory = computed(() =>
    (status.value?.history ?? []).filter(h => h.status === 'paid' || h.status === 'refunded'))

function formatDate(iso: string): string {
    return dayjs.utc(iso).tz(branding.timezone || 'UTC').format('YYYY-MM-DD')
}
function statusColor(s: string): string {
    switch (s) {
        case 'paid': return 'success'
        case 'pending': return 'warning'
        case 'failed': return 'error'
        case 'refunded': return 'grey'
        case 'cancelled': return 'orange'
        default: return 'default'
    }
}

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        const r = await service.getStatus()
        status.value = (r.data as any).data
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            || 'Could not load membership details. Refresh to try again, or check your connection.'
    } finally {
        loading.value = false
    }
}

async function createIntent() {
    // The price card renders for anonymous visitors (status endpoint is public);
    // buying needs an account, so sign in / sign up inline and resume the purchase.
    if (!authHelper.isAuthenticated()) {
        authDialogOpen.value = true
        return
    }
    creating.value = true
    try {
        const r = await service.buy()
        const data = (r.data as any).data
        clientSecret.value = data.clientSecret
        amountCents.value = data.amountCents
        await nextTick()
        await mountPayElement()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not start payment.', 'error')
    } finally {
        creating.value = false
    }
}

async function mountPayElement() {
    if (!clientSecret.value) return
    // Direct-charge tenants confirm on their own connected account; platform tenants pass no account.
    const stripeAccount = branding.stripeChargeMode === 'direct' ? branding.stripeConnectAccountId : null
    stripe = await getStripe(branding.stripePublishableKey, stripeAccount)
    if (!stripe) { paymentError.value = 'Stripe not available.'; return }
    elements = stripe.elements({ clientSecret: clientSecret.value })
    const pe = elements.create('payment')
    pe.mount('#membership-pay-element')
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
            purchased.value = true
            await load()
        }
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

onMounted(async () => {
    // Return from a redirect-based payment method (3DS / bank redirect): Stripe sends the
    // browser back here with these params. Most cards resolve inline in pay() and never
    // hit this path.
    const params = new URLSearchParams(window.location.search)
    const redirectStatus = params.get('redirect_status')
    if (params.get('payment_intent') && redirectStatus) {
        if (redirectStatus === 'succeeded') {
            purchased.value = true
            flash('Membership purchased.', 'success')
        } else {
            flash('Your payment was not completed. Please try again.', 'error')
        }
        history.replaceState(null, '', window.location.pathname)
    }
    await load()
})
</script>
