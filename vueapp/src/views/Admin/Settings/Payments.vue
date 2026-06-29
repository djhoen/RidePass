<template>
    <v-container>
        <h1 class="text-h4 mb-2">Payments</h1>
        <p class="text-body-2 text-medium-emphasis mb-6">
            Riders pay through RidePass, and RidePass pays you out via Stripe on the schedule
            agreed with your account manager. Connect a Stripe account below to enable Stripe-based
            payouts; without one, payouts are handled manually outside of Stripe.
        </p>

        <v-alert v-if="branding.stripeChargeMode === 'direct'" type="info" variant="tonal" density="comfortable" class="mb-6">
            <strong>Direct payments are enabled for your track.</strong>
            Card payments are charged directly on your own connected Stripe account (you are the merchant
            of record and funds settle to you directly). RidePass's service charge is collected automatically
            as a Stripe application fee, so there is no separate platform payout to wait for. Connect your own
            Stripe account below to start taking payments.
        </v-alert>

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

        <v-card class="mt-6">
            <v-card-item>
                <template #prepend><v-icon color="primary">mdi-ticket-percent-outline</v-icon></template>
                <v-card-title>Event admission tax</v-card-title>
                <v-card-subtitle>
                    Amusement / admission tax charged on event tickets and gate fees. This is separate
                    from concession sales tax (set under Concessions). Leave the rate at 0% for no tax.
                </v-card-subtitle>
            </v-card-item>
            <v-divider></v-divider>
            <v-card-text>
                <p class="text-caption text-medium-emphasis mb-4">
                    You collect and remit this tax to your jurisdiction; RidePass calculates and collects
                    it at checkout. Amusement tax is usually a local (city / county) rate, so confirm the
                    exact rate with your municipality.
                </p>
                <v-row>
                    <v-col cols="12" sm="4">
                        <v-text-field v-model.number="admissionTax.ratePct" type="number" min="0" max="100"
                            step="0.001" suffix="%" label="Admission tax rate" density="compact"
                            hide-details></v-text-field>
                    </v-col>
                    <v-col cols="12" sm="8">
                        <v-text-field v-model="admissionTax.jurisdictionLabel" label="Jurisdiction (optional)"
                            placeholder="e.g. City of Springfield amusement tax" density="compact"
                            hide-details></v-text-field>
                    </v-col>
                </v-row>
                <v-switch v-model="admissionTax.pricesIncludeTax" color="primary" density="compact" hide-details
                    class="mt-3" label="Ticket prices already include tax"
                    messages="On = tax is backed out of the listed price. Off = tax is added on top at checkout."></v-switch>
                <v-switch v-model="admissionTax.serviceChargeTaxable" color="primary" density="compact" hide-details
                    class="mt-3" label="Tax the rider service fee"
                    messages="Most jurisdictions tax the full admission charge (including a mandatory fee). Turn off only if yours excludes it."></v-switch>
            </v-card-text>
            <v-card-actions class="px-4 pb-4">
                <v-spacer></v-spacer>
                <v-btn color="primary" variant="flat" :loading="savingTax" @click="saveAdmissionTax">Save tax settings</v-btn>
            </v-card-actions>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { TenantService } from '@/services/TenantService'
import { TaxService } from '@/services/TaxService'
import { branding, loadBranding } from '@/stores/branding'
import { useConfirm } from '@/composables/useConfirm'

const tenantService = new TenantService()
const taxService = new TaxService()
const confirm = useConfirm()
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

// Event admission tax. Rate is edited as a percent and stored as basis points.
const savingTax = ref(false)
const admissionTax = ref({
    ratePct: 0,
    pricesIncludeTax: false,
    serviceChargeTaxable: true,
    jurisdictionLabel: '' as string,
})

async function loadAdmissionTax() {
    try {
        const res = await taxService.getAdmissionTax()
        const cfg = res.data?.data
        if (cfg) {
            admissionTax.value = {
                ratePct: (cfg.rateBps ?? 0) / 100,
                pricesIncludeTax: !!cfg.pricesIncludeTax,
                serviceChargeTaxable: cfg.serviceChargeTaxable ?? true,
                jurisdictionLabel: cfg.jurisdictionLabel ?? '',
            }
        }
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Could not load admission tax settings.'
        snackbarColor.value = 'error'
        snackbar.value = true
    }
}

async function saveAdmissionTax() {
    const pct = admissionTax.value.ratePct
    if (pct == null || isNaN(pct) || pct < 0 || pct > 100) {
        snackbarText.value = 'Enter an admission tax rate between 0% and 100%.'
        snackbarColor.value = 'error'
        snackbar.value = true
        return
    }
    try {
        savingTax.value = true
        await taxService.updateAdmissionTax({
            rateBps: Math.round(pct * 100),
            pricesIncludeTax: admissionTax.value.pricesIncludeTax,
            serviceChargeTaxable: admissionTax.value.serviceChargeTaxable,
            jurisdictionLabel: admissionTax.value.jurisdictionLabel?.trim() || null,
            isActive: true,
        })
        snackbarText.value = 'Admission tax settings saved.'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Could not save admission tax settings.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        savingTax.value = false
    }
}

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
    if (!await confirm({ message: `Disconnect Stripe? Future payouts will be handled manually outside of Stripe.`, confirmText: 'Disconnect', confirmColor: 'error' })) return
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
    await loadAdmissionTax()
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
