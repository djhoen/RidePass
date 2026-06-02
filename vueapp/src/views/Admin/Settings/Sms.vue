<template>
    <v-container>
        <h1 class="text-h4 mb-2">SMS</h1>
        <p class="text-body-2 text-medium-emphasis mb-6">
            Provision a toll-free phone number so your customers can receive text messages from your track.
        </p>

        <v-card v-if="status === null && !loadError" class="pa-6 text-center">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </v-card>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">
            {{ loadError }}
        </v-alert>

        <!-- Master not configured: platform-level issue, tenant can't fix it. -->
        <v-alert v-if="status && !status.masterConfigured" type="warning" variant="tonal">
            SMS isn't available on this platform yet. RidePass needs to configure the master Twilio account before
            tenants can provision numbers. Please contact support.
        </v-alert>

        <!-- Provisioned: show number + enable/disable switch -->
        <v-card v-if="status && status.masterConfigured && status.hasProvisionedNumber" class="mb-4">
            <v-list>
                <v-list-item>
                    <template #prepend>
                        <v-icon icon="mdi-cellphone-message" color="primary" class="mr-2"></v-icon>
                    </template>
                    <v-list-item-title class="font-weight-bold">{{ status.phoneNumber }}</v-list-item-title>
                    <v-list-item-subtitle>
                        Customers see this number on incoming texts.
                        <span v-if="status.enabled">
                            Enabled <span v-if="status.enabledAtUtc">{{ formatDate(status.enabledAtUtc) }}</span>.
                        </span>
                        <span v-else>Currently paused.</span>
                    </v-list-item-subtitle>
                    <template #append>
                        <v-switch
                            :model-value="status.enabled"
                            @update:model-value="(v: boolean | null) => toggleEnabled(!!v)"
                            color="primary"
                            :loading="toggling"
                            :disabled="toggling"
                            hide-details inset></v-switch>
                    </template>
                </v-list-item>
            </v-list>
            <v-card-text class="pt-0">
                <p class="text-caption text-medium-emphasis mb-0">
                    Tenant pays <strong>${{ (status.outboundPerSegmentCents / 100).toFixed(2) }}</strong>
                    per outbound segment (160 GSM characters = 1 segment).
                </p>
            </v-card-text>
            <v-divider></v-divider>
            <v-card-actions class="px-4">
                <v-spacer></v-spacer>
                <v-btn
                    size="small"
                    color="error"
                    variant="text"
                    :loading="releasing"
                    :disabled="toggling || releasing"
                    @click="release">
                    Release number
                </v-btn>
            </v-card-actions>
        </v-card>

        <!-- Toll-free verification: required to lift the ~10 msg/day carrier cap.
             Only visible once a number is provisioned — there's nothing to verify otherwise. -->
        <v-card v-if="status && status.hasProvisionedNumber && verification" class="mb-4">
            <v-card-title class="d-flex align-center">
                <span>Toll-free verification</span>
                <v-chip
                    :color="statusColor"
                    size="small"
                    variant="tonal"
                    class="ml-3">
                    {{ statusLabel }}
                </v-chip>
                <v-spacer></v-spacer>
                <v-btn
                    v-if="verification.status"
                    size="small"
                    variant="text"
                    :loading="refreshingVerification"
                    @click="refreshVerificationStatus">
                    Refresh status
                </v-btn>
            </v-card-title>
            <v-card-text>
                <p class="text-body-2 text-medium-emphasis mb-3">
                    Unverified toll-free numbers are capped at ~10 messages/day by carriers. Submit this
                    verification once and Twilio + carriers review it (typically 5–30 days). Edits saved as a
                    draft until you click Submit.
                </p>

                <v-alert
                    v-if="verification.rejectionReason"
                    type="error"
                    variant="tonal"
                    class="mb-4">
                    <div class="font-weight-bold mb-1">Rejected</div>
                    {{ verification.rejectionReason }}
                </v-alert>

                <v-row dense>
                    <v-col cols="12" md="6">
                        <v-text-field v-model="form.businessName" label="Business name" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="6">
                        <v-text-field v-model="form.businessWebsite" label="Website" density="compact" placeholder="https://"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="6">
                        <v-text-field v-model="form.businessStreetAddress" label="Street address" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="6" md="3">
                        <v-text-field v-model="form.businessCity" label="City" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="6" md="3">
                        <v-text-field v-model="form.businessStateProvinceRegion" label="State/Region" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="6" md="3">
                        <v-text-field v-model="form.businessPostalCode" label="Postal code" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="6" md="3">
                        <v-text-field v-model="form.businessCountry" label="Country (US, CA…)" maxlength="2" density="compact"></v-text-field>
                    </v-col>

                    <v-col cols="12"><div class="text-subtitle-2 mt-2 mb-1">Business contact</div></v-col>
                    <v-col cols="12" md="6">
                        <v-text-field v-model="form.businessContactFirstName" label="First name" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="6">
                        <v-text-field v-model="form.businessContactLastName" label="Last name" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="6">
                        <v-text-field v-model="form.businessContactEmail" label="Contact email" type="email" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="6">
                        <v-text-field v-model="form.businessContactPhone" label="Contact phone (E.164)" placeholder="+1…" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="6">
                        <v-text-field v-model="form.notificationEmail" label="Notification email (Twilio status updates)" type="email" density="compact"></v-text-field>
                    </v-col>

                    <v-col cols="12"><div class="text-subtitle-2 mt-2 mb-1">Use case</div></v-col>
                    <v-col cols="12" md="6">
                        <v-select
                            v-model="form.useCaseCategories"
                            :items="useCaseCategoryOptions"
                            label="Categories"
                            multiple chips closable-chips
                            density="compact"></v-select>
                    </v-col>
                    <v-col cols="12" md="6">
                        <v-select
                            v-model="form.messageVolume"
                            :items="messageVolumeOptions"
                            label="Monthly message volume"
                            density="compact"></v-select>
                    </v-col>
                    <v-col cols="12">
                        <v-textarea
                            v-model="form.useCaseSummary"
                            label="Use case summary (what the messages are about)"
                            rows="2" auto-grow density="compact"></v-textarea>
                    </v-col>

                    <v-col cols="12"><div class="text-subtitle-2 mt-2 mb-1">Sample messages</div></v-col>
                    <v-col v-for="(_, i) in form.productionMessageSamples" :key="`s${i}`" cols="12">
                        <v-textarea
                            v-model="form.productionMessageSamples[i]"
                            :label="`Sample ${i + 1}`"
                            rows="2" auto-grow density="compact"></v-textarea>
                    </v-col>

                    <v-col cols="12"><div class="text-subtitle-2 mt-2 mb-1">Opt-in</div></v-col>
                    <v-col cols="12" md="6">
                        <v-select
                            v-model="form.optInType"
                            :items="optInTypeOptions"
                            label="How customers opt in"
                            density="compact"></v-select>
                    </v-col>
                    <v-col v-for="(_, i) in form.optInImageUrls" :key="`u${i}`" cols="12" md="6">
                        <v-text-field
                            v-model="form.optInImageUrls[i]"
                            :label="`Opt-in screenshot URL ${i + 1}`"
                            placeholder="https://"
                            density="compact"></v-text-field>
                    </v-col>

                    <v-col cols="12">
                        <v-textarea
                            v-model="form.additionalInformation"
                            label="Additional information (optional)"
                            rows="2" auto-grow density="compact"></v-textarea>
                    </v-col>
                </v-row>

                <p v-if="verification.lastSubmittedAtUtc" class="text-caption text-medium-emphasis mt-2 mb-0">
                    Last submitted {{ formatDateTime(verification.lastSubmittedAtUtc) }}<span
                        v-if="verification.lastStatusCheckedAtUtc">, last checked {{ formatDateTime(verification.lastStatusCheckedAtUtc) }}</span>.
                </p>
            </v-card-text>

            <v-divider></v-divider>

            <v-card-actions class="px-4">
                <v-btn
                    variant="text"
                    :loading="savingVerification"
                    :disabled="submittingVerification"
                    @click="saveVerification">
                    Save draft
                </v-btn>
                <v-spacer></v-spacer>
                <v-btn
                    color="primary"
                    :loading="submittingVerification"
                    :disabled="savingVerification"
                    @click="submitVerification">
                    {{ verification.status ? 'Resubmit' : 'Submit' }}
                </v-btn>
            </v-card-actions>
        </v-card>

        <!-- Not provisioned: show search + buy flow -->
        <v-card v-if="status && status.masterConfigured && !status.hasProvisionedNumber" class="mb-4">
            <v-card-text>
                <p class="mb-4">
                    Search Twilio for an available toll-free number, then pick one to provision.
                    Toll-free numbers cost about $2/month and don't require 10DLC brand registration.
                </p>

                <div class="d-flex ga-3 align-end flex-wrap mb-4">
                    <v-text-field
                        v-model="areaCode"
                        label="Area code (optional)"
                        placeholder="833"
                        hint="800, 833, 844, 855, 866, 877, 888. Blank = any."
                        density="compact"
                        :disabled="searching || provisioningNumber !== null"
                        maxlength="3"
                        style="max-width: 240px"
                        @keyup.enter="search"></v-text-field>
                    <v-btn color="primary" variant="tonal" :loading="searching"
                        :disabled="provisioningNumber !== null" @click="search">
                        Search
                    </v-btn>
                </div>

                <v-table v-if="results.length > 0" density="compact">
                    <thead>
                        <tr>
                            <th>Number</th>
                            <th style="width: 200px">Region</th>
                            <th style="width: 200px" class="text-right"></th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="n in results" :key="n.phoneNumber">
                            <td class="font-weight-medium">{{ n.friendlyName || n.phoneNumber }}</td>
                            <td class="text-medium-emphasis">{{ n.region || 'Toll-free' }}</td>
                            <td class="text-right">
                                <v-btn size="small" color="primary"
                                    :loading="provisioningNumber === n.phoneNumber"
                                    :disabled="provisioningNumber !== null && provisioningNumber !== n.phoneNumber"
                                    @click="provision(n.phoneNumber)">
                                    Provision
                                </v-btn>
                            </td>
                        </tr>
                    </tbody>
                </v-table>

                <v-alert v-if="searched && results.length === 0 && !searching" type="info" variant="tonal" density="compact">
                    No matching numbers available. Try a different area code or leave it blank.
                </v-alert>
            </v-card-text>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import {
    SmsSettingsService,
    type SmsStatus,
    type AvailableNumber,
    type TollfreeVerification,
    type TollfreeVerificationDraft,
} from '@/services/SmsSettingsService'
import { useConfirm } from '@/composables/useConfirm'

const confirm = useConfirm()

const service = new SmsSettingsService()

const status = ref<SmsStatus | null>(null)
const loadError = ref<string | null>(null)

const areaCode = ref('')
const results = ref<AvailableNumber[]>([])
const searched = ref(false)
const searching = ref(false)
const provisioningNumber = ref<string | null>(null)
const toggling = ref(false)
const releasing = ref(false)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

// ── Toll-free verification state ────────────────────────────────────────────
// Three slots fixed in the form for sample messages + opt-in image URLs so
// Vuetify can render stable v-for keys; empty strings are filtered out on
// save so blank slots don't get sent to Twilio as empty samples.
const verification = ref<TollfreeVerification | null>(null)
const form = ref<TollfreeVerificationDraft>(emptyDraft())
const savingVerification = ref(false)
const submittingVerification = ref(false)
const refreshingVerification = ref(false)

const useCaseCategoryOptions = [
    'Account Notification', 'Appointments', 'Booking Confirmations',
    'Business Updates', 'Customer Care', 'Delivery Notification',
    'Events & Planning', 'Marketing/Promotional', 'Mixed (Multiple Use Cases)',
    'Order Notifications', 'Public Service Announcement', 'Rewards Program',
    'Surveys', 'System Alerts', 'Waitlist Alerts', '2FA',
]
const optInTypeOptions = [
    { title: 'Web form', value: 'WEB_FORM' },
    { title: 'Paper form', value: 'PAPER_FORM' },
    { title: 'Via text (text-to-join)', value: 'VIA_TEXT' },
    { title: 'Verbal', value: 'VERBAL' },
    { title: 'Mobile QR code', value: 'MOBILE_QR_CODE' },
]
const messageVolumeOptions = [
    { title: '10 / month', value: '10' },
    { title: '100 / month', value: '100' },
    { title: '1,000 / month', value: '1,000' },
    { title: '10,000 / month', value: '10,000' },
    { title: '100,000 / month', value: '100,000' },
    { title: '250,000 / month', value: '250,000' },
    { title: '500,000 / month', value: '500,000' },
    { title: '750,000 / month', value: '750,000' },
    { title: '1,000,000 / month', value: '1,000,000' },
]

const statusLabel = computed(() => {
    const s = verification.value?.status
    if (!s) return 'Draft'
    // Twilio's enum values are upper-snake; map to friendlier UI text.
    switch (s) {
        case 'PENDING_REVIEW':   return 'Pending Twilio review'
        case 'IN_REVIEW':        return 'In Twilio review'
        case 'TWILIO_APPROVED':  return 'Twilio approved — pending carriers'
        case 'TWILIO_REJECTED':  return 'Twilio rejected'
        case 'CARRIER_APPROVED': return 'Carrier approved'
        case 'CARRIER_REJECTED': return 'Carrier rejected'
        default:                 return s
    }
})

const statusColor = computed(() => {
    const s = verification.value?.status
    if (!s) return 'default'
    if (s.endsWith('_REJECTED')) return 'error'
    if (s === 'CARRIER_APPROVED') return 'success'
    if (s === 'TWILIO_APPROVED') return 'info'
    return 'warning'
})

onMounted(async () => {
    await loadStatus()
    await loadVerification()
})

async function loadStatus() {
    try {
        const r = await service.status()
        status.value = (r.data as any).data
        loadError.value = null
    } catch (err: any) {
        loadError.value = err.response?.data?.error || 'Failed to load SMS status.'
    }
}

async function loadVerification() {
    try {
        const r = await service.getVerification()
        const data = (r.data as any).data as TollfreeVerification
        verification.value = data
        form.value = toDraft(data)
    } catch (err: any) {
        // Verification load failure isn't fatal — the rest of the page still
        // works. Show a snackbar so the admin knows the verification card
        // didn't populate.
        flash(err.response?.data?.error || 'Failed to load verification.', 'error')
    }
}

async function saveVerification() {
    if (savingVerification.value) return
    savingVerification.value = true
    try {
        const r = await service.saveVerification(scrubDraft(form.value))
        const data = (r.data as any).data as TollfreeVerification
        verification.value = data
        form.value = toDraft(data)
        flash('Draft saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        savingVerification.value = false
    }
}

async function submitVerification() {
    if (submittingVerification.value) return
    // Save the current form state first so what's submitted matches what's
    // on screen — otherwise an admin who edited but didn't click "Save draft"
    // gets a stale snapshot submitted to Twilio.
    submittingVerification.value = true
    try {
        await service.saveVerification(scrubDraft(form.value))
        const r = await service.submitVerification()
        const data = (r.data as any).data as TollfreeVerification
        verification.value = data
        form.value = toDraft(data)
        flash('Submitted to Twilio.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Submission failed.', 'error')
    } finally {
        submittingVerification.value = false
    }
}

async function refreshVerificationStatus() {
    if (refreshingVerification.value) return
    refreshingVerification.value = true
    try {
        const r = await service.refreshVerification()
        const data = (r.data as any).data as TollfreeVerification
        verification.value = data
        flash('Status refreshed.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Refresh failed.', 'error')
    } finally {
        refreshingVerification.value = false
    }
}

function emptyDraft(): TollfreeVerificationDraft {
    return {
        businessName: null, businessWebsite: null,
        businessStreetAddress: null, businessCity: null,
        businessStateProvinceRegion: null, businessPostalCode: null, businessCountry: null,
        businessContactFirstName: null, businessContactLastName: null,
        businessContactEmail: null, businessContactPhone: null,
        notificationEmail: null,
        useCaseCategories: [], useCaseSummary: null,
        productionMessageSamples: ['', '', ''],
        optInType: null, optInImageUrls: ['', ''],
        messageVolume: null, additionalInformation: null,
    }
}

function toDraft(v: TollfreeVerification): TollfreeVerificationDraft {
    return {
        businessName: v.businessName,
        businessWebsite: v.businessWebsite,
        businessStreetAddress: v.businessStreetAddress,
        businessCity: v.businessCity,
        businessStateProvinceRegion: v.businessStateProvinceRegion,
        businessPostalCode: v.businessPostalCode,
        businessCountry: v.businessCountry,
        businessContactFirstName: v.businessContactFirstName,
        businessContactLastName: v.businessContactLastName,
        businessContactEmail: v.businessContactEmail,
        businessContactPhone: v.businessContactPhone,
        notificationEmail: v.notificationEmail,
        useCaseCategories: v.useCaseCategories ?? [],
        useCaseSummary: v.useCaseSummary,
        // Pad the arrays back to 3 / 2 fixed slots so the form has stable
        // rows even when the saved row has fewer entries.
        productionMessageSamples: padArray(v.productionMessageSamples ?? [], 3),
        optInType: v.optInType,
        optInImageUrls: padArray(v.optInImageUrls ?? [], 2),
        messageVolume: v.messageVolume,
        additionalInformation: v.additionalInformation,
    }
}

function padArray(arr: string[], len: number): string[] {
    const out = arr.slice(0, len)
    while (out.length < len) out.push('')
    return out
}

function scrubDraft(d: TollfreeVerificationDraft): TollfreeVerificationDraft {
    // Drop blank rows from the fixed-length arrays before sending; Twilio
    // wouldn't accept empty samples / image URLs anyway, and the saved row
    // looks cleaner without them.
    return {
        ...d,
        productionMessageSamples: d.productionMessageSamples.filter(s => s.trim().length > 0),
        optInImageUrls: d.optInImageUrls.filter(u => u.trim().length > 0),
    }
}

async function search() {
    if (searching.value) return
    searching.value = true
    searched.value = false
    try {
        const r = await service.search(areaCode.value.trim() || null, 10)
        results.value = (r.data as any).data
        searched.value = true
    } catch (err: any) {
        // 429 from rate-limit middleware comes through as a generic error;
        // surface it specifically so the admin knows to slow down.
        const status = err.response?.status
        if (status === 429) {
            flash('Too many searches — wait a minute and try again.', 'error')
        } else {
            flash(err.response?.data?.error || 'Search failed.', 'error')
        }
    } finally {
        searching.value = false
    }
}

async function provision(phoneNumber: string) {
    if (provisioningNumber.value !== null) return
    const ok = await confirm({
        title: 'Provision this number?',
        message: `Provision ${phoneNumber} for your track? Twilio bills about $2/month for this number.`,
        confirmText: 'Provision',
    })
    if (!ok) return
    provisioningNumber.value = phoneNumber
    try {
        await service.provision(phoneNumber)
        flash(`${phoneNumber} provisioned and enabled.`, 'success')
        results.value = []
        searched.value = false
        await loadStatus()
    } catch (err: any) {
        const code = err.response?.status
        if (code === 429) {
            flash('Provisioning rate-limited — wait a minute before trying again.', 'error')
        } else {
            flash(err.response?.data?.error || 'Provisioning failed.', 'error')
        }
    } finally {
        provisioningNumber.value = null
    }
}

async function release() {
    if (releasing.value) return
    // Released numbers go back to Twilio's inventory — no guarantee of
    // re-availability, so make the warning explicit. Red confirm button
    // signals destructive.
    const ok = await confirm({
        title: 'Release this number?',
        message: 'It goes back to Twilio\'s pool permanently. Conversation history stays, '
            + 'but future sends will stop until you provision a new number.',
        confirmText: 'Release',
        confirmColor: 'error',
    })
    if (!ok) return
    releasing.value = true
    try {
        await service.release()
        flash('Number released.', 'success')
        await loadStatus()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Release failed.', 'error')
    } finally {
        releasing.value = false
    }
}

async function toggleEnabled(next: boolean) {
    if (toggling.value) return
    toggling.value = true
    try {
        if (next) await service.enable()
        else await service.disable()
        flash(next ? 'SMS enabled.' : 'SMS paused.', 'success')
        await loadStatus()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Toggle failed.', 'error')
    } finally {
        toggling.value = false
    }
}

function formatDate(utc: string): string {
    return dayjs.utc(utc).local().format('MMM D, YYYY')
}

function formatDateTime(utc: string): string {
    return dayjs.utc(utc).local().format('MMM D, YYYY h:mm A')
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
