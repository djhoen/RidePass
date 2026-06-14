<template>
    <v-container>
        <h1 class="text-h4 mb-2">Payments</h1>
        <p class="text-body-2 text-medium-emphasis mb-6">
            Riders pay through RidePass, and RidePass pays you out via Stripe on the schedule
            agreed with your account manager. Connect a Stripe account below to enable Stripe-based
            payouts; without one, payouts are handled manually outside of Stripe.
        </p>

        <v-card class="pa-4">
            <v-card-text>
                <div v-if="!branding.stripeConnectAccountId" class="d-flex flex-column ga-3">
                    <div class="d-flex align-center ga-2">
                        <v-icon color="grey">mdi-credit-card-outline</v-icon>
                        <span>No Stripe payout account connected.</span>
                    </div>
                    <div>
                        <v-btn color="primary" :loading="connectLoading" @click="connectStripe">
                            Connect Stripe for payouts
                        </v-btn>
                    </div>
                </div>
                <div v-else class="d-flex flex-column ga-3">
                    <div class="d-flex align-center ga-2">
                        <v-icon :color="connectStatusColor">{{ connectStatusIcon }}</v-icon>
                        <span>
                            <strong>Connected:</strong>
                            <code class="ml-1">{{ branding.stripeConnectAccountId }}</code>
                        </span>
                        <v-chip :color="connectStatusColor" size="small" class="ml-2">
                            {{ connectStatusLabel }}
                        </v-chip>
                    </div>
                    <div class="text-caption text-medium-emphasis">
                        {{ connectStatusHint }}
                    </div>
                    <div class="d-flex ga-2 flex-wrap">
                        <v-btn v-if="branding.stripeConnectStatus !== 'active'" color="primary"
                            :loading="connectLoading" @click="connectStripe">
                            Continue onboarding
                        </v-btn>
                        <v-btn variant="tonal" :loading="refreshLoading" @click="refreshConnectStatus">
                            Refresh status
                        </v-btn>
                        <v-btn variant="tonal" color="info" :loading="testLoading" @click="testStripe">
                            Test connection
                        </v-btn>
                        <v-btn variant="text" color="error" :loading="disconnectLoading" @click="disconnectStripe">
                            Disconnect
                        </v-btn>
                    </div>
                    <v-alert v-if="testResult" :type="testResult.ok ? 'success' : 'error'" variant="tonal"
                        density="compact" closable @click:close="testResult = null">
                        <template v-if="testResult.ok && testResult.data">
                            Round-trip OK. Charges enabled:
                            <strong>{{ testResult.data.chargesEnabled ? 'yes' : 'no' }}</strong>,
                            payouts enabled:
                            <strong>{{ testResult.data.payoutsEnabled ? 'yes' : 'no' }}</strong>.
                            Available: <strong>{{ formatMoney(testResult.data.availableCents, testResult.data.currency) }}</strong>,
                            pending: <strong>{{ formatMoney(testResult.data.pendingCents, testResult.data.currency) }}</strong>.
                        </template>
                        <template v-else>
                            {{ testResult.message }}
                        </template>
                    </v-alert>
                </div>
            </v-card-text>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { TenantService } from '@/services/TenantService'
import { branding, loadBranding } from '@/stores/branding'

const tenantService = new TenantService()
const route = useRoute()
const router = useRouter()

const connectLoading = ref(false)
const refreshLoading = ref(false)
const disconnectLoading = ref(false)
const testLoading = ref(false)

type ConnectTestData = {
    accountId: string
    chargesEnabled: boolean
    payoutsEnabled: boolean
    availableCents: number
    pendingCents: number
    currency: string
}
const testResult = ref<{ ok: boolean; message?: string; data?: ConnectTestData } | null>(null)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const connectStatusLabel = computed(() => {
    switch (branding.stripeConnectStatus) {
        case 'active': return 'Active'
        case 'pending': return 'Pending KYC'
        case 'restricted': return 'Restricted'
        default: return 'Unknown'
    }
})

const connectStatusColor = computed(() => {
    switch (branding.stripeConnectStatus) {
        case 'active': return 'success'
        case 'pending': return 'warning'
        case 'restricted': return 'error'
        default: return 'grey'
    }
})

const connectStatusIcon = computed(() => {
    switch (branding.stripeConnectStatus) {
        case 'active': return 'mdi-check-circle'
        case 'pending': return 'mdi-clock-outline'
        case 'restricted': return 'mdi-alert'
        default: return 'mdi-help-circle-outline'
    }
})

const connectStatusHint = computed(() => {
    switch (branding.stripeConnectStatus) {
        case 'active':
            return 'Stripe payouts are ready. RidePass can send your owed balance directly to your bank via Stripe Transfer.'
        case 'pending':
            return 'Stripe still needs identity / banking information. Click "Continue onboarding" to finish, until then RidePass payouts will be handled manually.'
        case 'restricted':
            return 'Stripe flagged the account (KYC incomplete or a capability disabled). Stripe payouts will resume once cleared.'
        default:
            return ''
    }
})

async function connectStripe() {
    try {
        connectLoading.value = true
        const res = await tenantService.startStripeConnectOnboarding()
        const url = res.data?.data?.onboardingUrl
        if (url) {
            window.location.href = url
        } else {
            snackbarText.value = 'Could not get onboarding URL.'
            snackbarColor.value = 'error'
            snackbar.value = true
        }
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Could not start Stripe onboarding.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        connectLoading.value = false
    }
}

async function refreshConnectStatus() {
    try {
        refreshLoading.value = true
        await tenantService.refreshStripeConnectStatus()
        await loadBranding()
        snackbarText.value = 'Stripe status refreshed.'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Could not refresh status.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        refreshLoading.value = false
    }
}

async function testStripe() {
    try {
        testLoading.value = true
        testResult.value = null
        const res = await tenantService.testStripeConnect()
        testResult.value = { ok: true, data: res.data?.data as ConnectTestData }
    } catch (err: any) {
        testResult.value = {
            ok: false,
            message: err.response?.data?.error || 'Test call failed.',
        }
    } finally {
        testLoading.value = false
    }
}

function formatMoney(cents: number, currency: string): string {
    try {
        return new Intl.NumberFormat(undefined, { style: 'currency', currency: currency || 'USD' })
            .format((cents ?? 0) / 100)
    } catch {
        return `${((cents ?? 0) / 100).toFixed(2)} ${currency}`
    }
}

async function disconnectStripe() {
    if (!confirm('Disconnect Stripe? Future payouts will be handled manually outside of Stripe.')) return
    try {
        disconnectLoading.value = true
        await tenantService.disconnectStripeConnect()
        await loadBranding()
        snackbarText.value = 'Stripe disconnected.'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Could not disconnect.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        disconnectLoading.value = false
    }
}

onMounted(async () => {
    if (!branding.loaded) await loadBranding()
    // Stripe-hosted onboarding redirects back here with ?stripe=connect_complete
    // (or =connect_refresh). The webhook keeps status in sync but can lag a few seconds —
    // poll explicitly so the UI reflects the new state immediately.
    const stripeParam = route.query.stripe as string | undefined
    if (stripeParam === 'connect_complete' || stripeParam === 'connect_refresh') {
        await refreshConnectStatus()
        router.replace({ query: { ...route.query, stripe: undefined } })
    }
})
</script>
