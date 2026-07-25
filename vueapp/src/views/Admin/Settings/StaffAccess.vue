<template>
    <v-container style="max-width: 820px">
        <h1 class="text-h4 mb-1">Staff Access</h1>
        <p class="text-body-2 text-medium-emphasis mb-4">
            Limit where and when staff can take money or check people in. Refunds, counter sales,
            F&amp;B, the bike shop register, gate check-ins and cash handling are covered.
            Reports, settings, catalog and marketing are never restricted, so you can always do
            paperwork from home and always get back in to change these rules.
        </p>

        <v-alert type="info" variant="tonal" density="compact" class="mb-4">
            Not sure what to allow? Check
            <router-link to="/Admin/StaffActivity">Staff Activity</router-link>
            first: it shows the address every recorded action came from, so you can see what's
            normal before you switch enforcement on.
        </v-alert>

        <v-card class="mb-4">
            <v-card-text>
                <v-switch v-model="enforce" color="primary" hide-details inset
                    :label="enforce ? 'Enforcing: blocked outside the rules below' : 'Off: staff can work from anywhere, any time'">
                </v-switch>
            </v-card-text>
        </v-card>

        <v-card class="mb-4">
            <v-card-title class="text-subtitle-1">Allowed networks</v-card-title>
            <v-card-text>
                <p class="text-body-2 text-medium-emphasis mb-3">
                    Addresses the track operates from. Leave empty for no location rule, which is
                    the right choice if your internet connection doesn't have a fixed address.
                </p>

                <div v-if="myIp" class="d-flex align-center ga-2 mb-3 flex-wrap">
                    <v-chip size="small" variant="tonal" prepend-icon="mdi-map-marker">
                        You're connecting from {{ myIp }}
                    </v-chip>
                    <v-btn size="small" variant="tonal" color="primary" :disabled="cidrsContainMyIp"
                        @click="addMyIp">
                        {{ cidrsContainMyIp ? 'Already added' : 'Add this address' }}
                    </v-btn>
                </div>

                <div v-for="(c, i) in cidrs" :key="i" class="d-flex align-center ga-2" :class="i > 0 ? 'mt-4' : ''">
                    <v-text-field :model-value="c" density="compact" variant="outlined" hide-details
                        placeholder="203.0.113.0/24 or 203.0.113.45"
                        @update:model-value="(v: string) => cidrs[i] = v"></v-text-field>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="cidrs.splice(i, 1)"></v-btn>
                </div>

                <v-btn size="small" variant="text" prepend-icon="mdi-plus" class="mt-2"
                    @click="cidrs.push('')">Add a network</v-btn>
            </v-card-text>
        </v-card>

        <v-card class="mb-4">
            <v-card-title class="text-subtitle-1">Operating hours</v-card-title>
            <v-card-text>
                <p class="text-body-2 text-medium-emphasis mb-3">
                    Times are in your track's timezone ({{ branding.timezone || 'UTC' }}). Leave
                    both blank for no time limit. A closing time earlier than the opening time
                    means the window runs past midnight, which is how a night event works.
                </p>
                <div class="d-flex ga-3 flex-wrap" style="max-width: 420px">
                    <v-text-field v-model="hoursStart" type="time" label="Opens" density="compact"
                        variant="outlined" hide-details style="flex: 1 1 160px"></v-text-field>
                    <v-text-field v-model="hoursEnd" type="time" label="Closes" density="compact"
                        variant="outlined" hide-details style="flex: 1 1 160px"></v-text-field>
                </div>
                <v-btn v-if="hoursStart || hoursEnd" size="small" variant="text" class="mt-2"
                    @click="hoursStart = ''; hoursEnd = ''">Clear hours</v-btn>
            </v-card-text>
        </v-card>

        <v-card class="mb-4">
            <v-card-title class="text-subtitle-1">Daily alerts</v-card-title>
            <v-card-text>
                <p class="text-body-2 text-medium-emphasis mb-3">
                    Email {{ branding.contactEmail || 'the track contact address' }} when the
                    previous day's activity turns up something worth a look: cash refunds with no
                    processor record, refunds on purchases that already rode, refunds redirected to
                    store credit, store credit granted with no sale behind it, repeated manager PIN
                    failures, or a staff member working from an address you've not seen before.
                </p>
                <v-switch v-model="alertsEnabled" color="primary" hide-details inset
                    :label="alertsEnabled ? 'Sending a daily digest' : 'Not sending alerts'"></v-switch>
                <v-text-field v-if="alertsEnabled" v-model.number="alertRefundDollars" type="number"
                    min="1" prefix="$" label="Also flag anyone refunding more than this in a day"
                    density="compact" variant="outlined" hide-details class="mt-4"
                    style="max-width: 340px"></v-text-field>
                <p v-if="alertsEnabled && !branding.contactEmail" class="text-body-2 text-warning mt-3">
                    No contact email is set for this track, so nothing can be sent. Add one under
                    Settings, General first.
                </p>
            </v-card-text>
        </v-card>

        <v-alert v-if="wouldLockMeOut" type="warning" variant="tonal" density="compact" class="mb-4">
            The address you're on right now isn't in the list. You'd still be able to reach
            settings and reports, but not the register or the gate, until you connect from an
            allowed network.
        </v-alert>

        <div class="d-flex align-center ga-2">
            <v-btn color="primary" :loading="saving" :disabled="!dirty" @click="save">Save</v-btn>
            <v-btn variant="text" :disabled="!dirty || saving" @click="reset">Cancel</v-btn>
        </div>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="5000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { TenantService } from '@/services/TenantService'
import { branding, loadBranding } from '@/stores/branding'

const tenantService = new TenantService()

const enforce = ref(false)
const cidrs = ref<string[]>([])
const hoursStart = ref('')
const hoursEnd = ref('')
const myIp = ref<string | null>(null)
const alertsEnabled = ref(false)
const alertRefundDollars = ref(500)
const saving = ref(false)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')
function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

function reset() {
    enforce.value = branding.staffAccessPolicyMode === 1
    cidrs.value = [...(branding.staffAllowedCidrs ?? [])]
    hoursStart.value = branding.staffHoursStart ?? ''
    hoursEnd.value = branding.staffHoursEnd ?? ''
    alertsEnabled.value = branding.staffAlertsEnabled
    alertRefundDollars.value = Math.round((branding.staffAlertRefundCents ?? 50000) / 100)
}

const cleanCidrs = computed(() => cidrs.value.map(c => c.trim()).filter(c => c.length > 0))

const alertRefundCents = computed(() => Math.max(100, Math.round((alertRefundDollars.value || 0) * 100)))

const dirty = computed(() =>
    enforce.value !== (branding.staffAccessPolicyMode === 1)
    || JSON.stringify(cleanCidrs.value) !== JSON.stringify(branding.staffAllowedCidrs ?? [])
    || hoursStart.value !== (branding.staffHoursStart ?? '')
    || hoursEnd.value !== (branding.staffHoursEnd ?? '')
    || alertsEnabled.value !== branding.staffAlertsEnabled
    || alertRefundCents.value !== branding.staffAlertRefundCents)

/** Exact-match only. A /24 containing the address won't light this up, which is deliberate:
 *  claiming "already added" for a range we haven't actually verified would be worse than a
 *  redundant entry the server dedupes anyway. */
const cidrsContainMyIp = computed(() =>
    myIp.value !== null && cleanCidrs.value.some(c => c === myIp.value || c === `${myIp.value}/32`))

/** Advisory only, and same exact-match caveat: warn when enforcing with a non-empty list that
 *  doesn't obviously include where the admin is sitting. */
const wouldLockMeOut = computed(() =>
    enforce.value && cleanCidrs.value.length > 0 && myIp.value !== null && !cidrsContainMyIp.value)

function addMyIp() {
    if (!myIp.value || cidrsContainMyIp.value) return
    cidrs.value.push(myIp.value)
}

async function save() {
    saving.value = true
    try {
        await tenantService.updateStaffAccessPolicy({
            mode: enforce.value ? 1 : 0,
            allowedCidrs: cleanCidrs.value,
            hoursStart: hoursStart.value || null,
            hoursEnd: hoursEnd.value || null,
            alertsEnabled: alertsEnabled.value,
            alertRefundCents: alertRefundCents.value,
        })
        await loadBranding()
        reset()
        flash('Staff access rules saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || "Couldn't save the staff access rules. Check the values and try again.", 'error')
    } finally {
        saving.value = false
    }
}

onMounted(async () => {
    reset()
    try {
        const r = await tenantService.getMyIpAddress()
        myIp.value = r.data.data.ipAddress
    } catch {
        // The helper chip is a convenience; the form works without knowing the caller's address.
        myIp.value = null
    }
})
</script>
