<template>
    <div>
        <div class="text-subtitle-2 mb-1">When a repair is ready</div>
        <p class="text-body-2 text-medium-emphasis mb-2">
            Tell the customer their bike is ready as soon as staff mark the work order ready.
        </p>
        <v-switch v-model="readyEmail" color="primary" density="compact" hide-details
            label="Send an email" :disabled="saving"></v-switch>
        <v-switch v-model="readySms" color="primary" density="compact" hide-details
            label="Send a text message" :disabled="saving"></v-switch>
        <div class="text-caption text-medium-emphasis mt-1">
            Texts are billed per message and only send when SMS is set up for this track and the
            customer left a mobile number.
        </div>

        <v-divider class="my-5"></v-divider>

        <div class="text-subtitle-2 mb-1">Service reminders</div>
        <p class="text-body-2 text-medium-emphasis mb-3">
            Email a customer a while after you hand their bike back, suggesting it's due another
            look. Set 0 to turn reminders off.
        </p>

        <div class="d-flex align-center ga-3 flex-wrap">
            <v-text-field v-model.number="days" type="number" min="0" max="730"
                label="Days after pickup" suffix="days" density="compact" hide-details
                style="max-width: 200px" :disabled="saving"></v-text-field>
            <v-btn color="primary" :loading="saving" :disabled="!dirty" @click="save">Save</v-btn>
            <v-btn v-if="dirty" variant="text" :disabled="saving" @click="reset">Cancel</v-btn>
        </div>

        <div class="text-caption text-medium-emphasis mt-2">
            {{ days > 0
                ? `A repair picked up today would prompt a reminder on ${previewDate}.`
                : 'Reminders are off. No follow-up emails are sent.' }}
        </div>

        <!-- Reminders are only worth sending to a customer who left an email, and only once the
             job is actually picked up, so say so rather than letting a track wonder. -->
        <v-alert v-if="days > 0" type="info" variant="tonal" density="compact" class="mt-4">
            Sent only for work orders that were picked up and have a customer email on file.
            Customers who unsubscribed or bounced are skipped.
        </v-alert>

        <v-divider class="my-5"></v-divider>

        <div class="text-subtitle-2 mb-1">Shop labor rate</div>
        <p class="text-body-2 text-medium-emphasis mb-3">
            Your standard charge for shop time. On a work order you enter hours on a labor line and it
            bills hours times this rate, so you're not typing a price every time. Leave it blank to
            keep pricing each labor line by hand.
        </p>
        <div class="d-flex align-center ga-3 flex-wrap">
            <v-text-field v-model.number="laborRate" type="number" min="0" step="1"
                label="Per hour" prefix="$" suffix="/hr" density="compact" hide-details clearable
                placeholder="not set" style="max-width: 200px" :disabled="savingLabor"></v-text-field>
            <v-btn color="primary" :loading="savingLabor" :disabled="!laborDirty" @click="saveLabor">Save</v-btn>
            <v-btn v-if="laborDirty" variant="text" :disabled="savingLabor" @click="resetLabor">Cancel</v-btn>
        </div>
        <div class="text-caption text-medium-emphasis mt-2">{{ laborPreview }}</div>
        <div v-if="laborError" class="text-error text-caption mt-2">{{ laborError }}</div>

        <v-divider class="my-5"></v-divider>

        <div class="text-subtitle-2 mb-1">Shop supply fee</div>
        <p class="text-body-2 text-medium-emphasis mb-3">
            Adds a line to repair bills for consumables (solvent, rags, lube, disposal). Charged
            as a percentage of LABOR, not parts, so an expensive part doesn't inflate it. Set 0% to
            turn it off.
        </p>
        <div class="d-flex align-center ga-3 flex-wrap">
            <v-text-field v-model.number="feePercent" type="number" min="0" max="50" step="0.5"
                label="Percent of labor" suffix="%" density="compact" hide-details
                style="max-width: 180px" :disabled="savingFee"></v-text-field>
            <v-text-field v-model.number="feeCapDollars" type="number" min="0" step="1"
                label="Cap" prefix="$" density="compact" hide-details clearable
                placeholder="none" style="max-width: 160px" :disabled="savingFee"></v-text-field>
            <v-text-field v-model="feeLabel" label="Shown on the bill as" density="compact"
                hide-details style="max-width: 220px" :disabled="savingFee"></v-text-field>
            <v-btn color="primary" :loading="savingFee" :disabled="!feeDirty" @click="saveFee">Save</v-btn>
        </div>
        <div class="text-caption text-medium-emphasis mt-2">{{ feePreview }}</div>
        <div v-if="feeError" class="text-error text-caption mt-2">{{ feeError }}</div>

        <v-divider class="my-5"></v-divider>

        <div class="text-subtitle-2 mb-1">Platform service fee</div>
        <p class="text-body-2 text-medium-emphasis mb-3">
            Everything you sell in the shop carries the same
            {{ ((branding.serviceChargeBps ?? 0) / 100).toFixed(2) }}% service fee as events and rentals.
            That rate is set once for the whole track in
            <router-link to="/Admin/Settings/General">Settings</router-link>, and all you choose here is
            who funds it. This covers counter sales, online store orders, and the parts and labor billed
            out on a work order.
        </p>

        <v-slider v-model="shopFeePct" :min="0" :max="100" :step="5" thumb-label
            color="primary" label="Customer pays" style="max-width: 520px" :disabled="savingShopFee">
            <template #append>
                <span style="min-width: 52px" class="text-right">{{ shopFeePct }}%</span>
            </template>
        </v-slider>

        <v-alert type="info" variant="tonal" density="compact" class="mb-3" style="max-width: 520px">
            <template v-if="shopFeePct === 100">
                The customer pays the whole fee. It shows as a "Service fee" line at checkout.
            </template>
            <template v-else-if="shopFeePct === 0">
                You absorb the whole fee. The customer sees no fee line and it comes out of your margin.
            </template>
            <template v-else>
                The customer pays {{ shopFeePct }}% of the fee and you absorb the rest.
            </template>
        </v-alert>

        <div class="text-caption text-medium-emphasis mb-3">{{ shopFeePreview }}</div>

        <div class="d-flex align-center ga-3 flex-wrap">
            <v-btn color="primary" :loading="savingShopFee" :disabled="!shopFeeDirty" @click="saveShopFee">Save</v-btn>
            <v-btn v-if="shopFeeDirty" variant="text" :disabled="savingShopFee" @click="resetShopFee">Cancel</v-btn>
        </div>
        <div v-if="shopFeeError" class="text-error text-caption mt-2">{{ shopFeeError }}</div>

        <div v-if="error" class="text-error text-caption mt-2">{{ error }}</div>
        <v-snackbar v-model="snackbar" color="success" :timeout="2500">Saved.</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { TenantService } from '@/services/TenantService'
import { branding, loadBranding } from '@/stores/branding'

const tenantService = new TenantService()

const days = ref(0)
const readyEmail = ref(true)
const readySms = ref(false)
const original = ref({ days: 0, readyEmail: true, readySms: false })
const saving = ref(false)
const error = ref('')
const snackbar = ref(false)

const dirty = computed(() =>
    days.value !== original.value.days
    || readyEmail.value !== original.value.readyEmail
    || readySms.value !== original.value.readySms)
const previewDate = computed(() => dayjs().add(days.value || 0, 'day').format('MMM D, YYYY'))

function reset() {
    days.value = original.value.days
    readyEmail.value = original.value.readyEmail
    readySms.value = original.value.readySms
}

async function save() {
    if (!dirty.value) return
    saving.value = true
    error.value = ''
    try {
        await tenantService.updateShopNotifications({
            readyNotifyEmail: readyEmail.value,
            readyNotifySms: readySms.value,
            serviceReminderDays: days.value || 0,
        })
        original.value = { days: days.value, readyEmail: readyEmail.value, readySms: readySms.value }
        await loadBranding()
        snackbar.value = true
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not save that. Try again.'
    } finally {
        saving.value = false
    }
}

// ── Shop labor rate ───────────────────────────────────────────────────────────────────
// null = no rate set (labor lines take a typed price); a number is dollars/hour.
const laborRate = ref<number | null>(null)
const laborOriginal = ref<number | null>(null)
const savingLabor = ref(false)
const laborError = ref('')

// Treat null / '' / NaN uniformly as "not set" so clearing the field reads as a real change.
function normRate(v: number | null): number | null {
    return v == null || Number.isNaN(v) || v <= 0 ? null : v
}
const laborDirty = computed(() => normRate(laborRate.value) !== normRate(laborOriginal.value))
const laborPreview = computed(() => {
    const r = normRate(laborRate.value)
    return r == null
        ? 'No rate set. Labor lines take a typed price.'
        : `A 1.5-hour job bills $${(r * 1.5).toFixed(2)} of labor.`
})

function resetLabor() { laborRate.value = laborOriginal.value }

async function saveLabor() {
    if (!laborDirty.value) return
    savingLabor.value = true
    laborError.value = ''
    try {
        const r = normRate(laborRate.value)
        await tenantService.updateShopLaborRate({ rateCents: r == null ? null : Math.round(r * 100) })
        laborOriginal.value = r
        laborRate.value = r
        await loadBranding()
        snackbar.value = true
    } catch (e: any) {
        laborError.value = e.response?.data?.error || 'Could not save the labor rate. Try again.'
    } finally {
        savingLabor.value = false
    }
}

// ── Shop supply fee ───────────────────────────────────────────────────────────────────
const feePercent = ref(0)
const feeCapDollars = ref<number | null>(null)
const feeLabel = ref('Shop supplies')
const feeOriginal = ref({ percent: 0, cap: null as number | null, label: 'Shop supplies' })
const savingFee = ref(false)
const feeError = ref('')

const feeDirty = computed(() =>
    feePercent.value !== feeOriginal.value.percent
    || (feeCapDollars.value ?? null) !== feeOriginal.value.cap
    || feeLabel.value !== feeOriginal.value.label)

// Worked example on a typical job, so the number means something before it hits a real bill.
const feePreview = computed(() => {
    if (!feePercent.value) return 'No supply fee is added to repair bills.'
    const raw = 120 * (feePercent.value / 100)
    const capped = feeCapDollars.value != null ? Math.min(raw, feeCapDollars.value) : raw
    return `On $120 of labor this adds $${capped.toFixed(2)}`
        + (feeCapDollars.value != null ? `, never more than $${feeCapDollars.value.toFixed(2)}.` : '.')
})

async function saveFee() {
    if (!feeDirty.value) return
    savingFee.value = true
    feeError.value = ''
    try {
        await tenantService.updateShopSupplyFee({
            bps: Math.round((feePercent.value || 0) * 100),
            capCents: feeCapDollars.value == null ? null : Math.round(feeCapDollars.value * 100),
            label: feeLabel.value.trim() || 'Shop supplies',
        })
        feeOriginal.value = {
            percent: feePercent.value, cap: feeCapDollars.value ?? null, label: feeLabel.value,
        }
        await loadBranding()
        snackbar.value = true
    } catch (e: any) {
        feeError.value = e.response?.data?.error || 'Could not save the fee. Try again.'
    } finally {
        savingFee.value = false
    }
}

// ── Platform service fee split ────────────────────────────────────────────────────────
// Percent (not bps) for the slider; converted back on save. Default 0 matches the column
// default: turning the charge on must never silently put a fee line in front of a walk-in.
const shopFeePct = ref(0)
const shopFeeOriginal = ref(0)
const savingShopFee = ref(false)
const shopFeeError = ref('')

const shopFeeDirty = computed(() => shopFeePct.value !== shopFeeOriginal.value)

// Worked example on a round number, using the same floor-then-floor math as the server
// (Services/Payments/ServiceChargeSplit.cs), so the preview can't disagree with the receipt.
const shopFeePreview = computed(() => {
    const base = 10000
    const full = Math.floor((base * (branding.serviceChargeBps ?? 0)) / 10000)
    const customer = Math.floor((full * shopFeePct.value * 100) / 10000)
    return customer === 0
        ? `On a $100 sale the fee is $${(full / 100).toFixed(2)} and the customer is charged $100.00.`
        : `On a $100 sale the fee is $${(full / 100).toFixed(2)}, of which the customer pays `
          + `$${(customer / 100).toFixed(2)}, so they are charged $${((base + customer) / 100).toFixed(2)} before tax.`
})

function resetShopFee() { shopFeePct.value = shopFeeOriginal.value }

async function saveShopFee() {
    if (!shopFeeDirty.value) return
    savingShopFee.value = true
    shopFeeError.value = ''
    try {
        await tenantService.updateShopServiceCharge({ buyerPaidBps: shopFeePct.value * 100 })
        shopFeeOriginal.value = shopFeePct.value
        await loadBranding()
        snackbar.value = true
    } catch (e: any) {
        shopFeeError.value = e.response?.data?.error
            || 'Could not save who pays the service fee. Try again.'
    } finally {
        savingShopFee.value = false
    }
}

onMounted(() => {
    shopFeePct.value = Math.round((branding.shopBuyerPaidServiceChargeBps ?? 0) / 100)
    shopFeeOriginal.value = shopFeePct.value
    laborRate.value = branding.shopLaborRateCents == null ? null : branding.shopLaborRateCents / 100
    laborOriginal.value = laborRate.value
    feePercent.value = (branding.shopSupplyFeeBps ?? 0) / 100
    feeCapDollars.value = branding.shopSupplyFeeCapCents == null
        ? null : branding.shopSupplyFeeCapCents / 100
    feeLabel.value = branding.shopSupplyFeeLabel ?? 'Shop supplies'
    feeOriginal.value = {
        percent: feePercent.value, cap: feeCapDollars.value, label: feeLabel.value,
    }
    days.value = branding.shopServiceReminderDays ?? 0
    readyEmail.value = branding.shopReadyNotifyEmail ?? true
    readySms.value = branding.shopReadyNotifySms ?? false
    original.value = { days: days.value, readyEmail: readyEmail.value, readySms: readySms.value }
})
</script>
