<template>
    <v-container style="max-width: 720px">
        <h1 class="text-h4 mb-4">My Season Passes</h1>

        <v-card v-if="loading" class="pa-6 text-center">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </v-card>
        <v-card v-else-if="passes.length === 0" class="pa-6 text-center text-medium-emphasis">
            You don't have any season passes yet.
            <div v-if="branding.seasonPassesEnabled" class="mt-3">
                <v-btn to="/SeasonPasses" color="primary">Browse passes</v-btn>
            </div>
            <div v-else class="text-caption mt-3">
                This track isn't selling season passes right now.
            </div>
        </v-card>

        <v-card v-for="p in passes" :key="p.id" class="mb-4 pa-4">
            <div class="d-flex align-center ga-4 flex-wrap">
                <QrCode :value="String(p.redemptionToken)" :size="160" />
                <div class="flex-grow-1">
                    <strong class="text-h6">{{ p.productName }}</strong>
                    <div v-if="holderName(p)" class="text-caption text-medium-emphasis">
                        For {{ holderName(p) }}
                    </div>
                    <div class="text-caption mt-1">
                        Valid {{ formatDate(p.validFromDate) }} to {{ formatDate(p.validToDate) }}
                    </div>
                    <div class="text-caption">
                        <span v-if="p.productKind === 'unlimited'">Unlimited rides</span>
                        <span v-else-if="p.productKind === 'days_of_week'">{{ daysLabel(p.validDaysOfWeek) }} only</span>
                        <span v-else-if="p.productKind === 'credits'">{{ p.creditsRemaining ?? 0 }} credits remaining</span>
                    </div>
                    <v-chip size="small" :color="p.status === 'paid' ? 'success' : 'warning'" class="mt-2">
                        {{ p.status }}
                    </v-chip>
                </div>
            </div>

            <!-- Paid but never registered: the gate will turn this rider away, so make the fix
                 obvious here rather than letting them find out at the gate. Happens when a
                 redirect-based payment took the buyer out of checkout before the register step. -->
            <v-alert v-if="p.status === 'paid' && !p.registrationComplete" type="warning" variant="tonal"
                density="compact" class="mt-3">
                <div class="text-body-2">
                    This pass isn't ready to use yet. Add the holder's details{{ p.requiresWaiver ? ', photo, and waiver signature' : ' and photo' }}
                    so the gate can check them in.
                </div>
                <v-btn color="warning" size="small" class="mt-2" @click="openRegister(p)">
                    Finish registration
                </v-btn>
            </v-alert>
            <p v-else class="text-caption text-medium-emphasis mt-3">
                Show this QR to the gate worker on your event day.
            </p>
        </v-card>

        <v-dialog v-model="registerOpen" max-width="520" persistent>
            <v-card v-if="registering">
                <v-card-title class="d-flex align-center">
                    <span>Finish registration</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="closeRegister"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 text-medium-emphasis mb-3">
                        {{ registering.productName }} — tell us who this pass is for.
                    </p>
                    <v-row dense>
                        <v-col cols="6">
                            <v-text-field v-model="form.firstName" label="First name" density="compact" hide-details></v-text-field>
                        </v-col>
                        <v-col cols="6">
                            <v-text-field v-model="form.lastName" label="Last name" density="compact" hide-details></v-text-field>
                        </v-col>
                    </v-row>
                    <v-text-field v-model="form.birthdate" type="date" label="Date of birth" density="compact"
                        class="mt-4" :max="todayIso" hide-details></v-text-field>

                    <div class="text-caption text-medium-emphasis mt-3 mb-1">
                        Photo — the gate checks this against the person at the gate.
                    </div>
                    <PhotoCapture v-model="form.photoDataUrl" />

                    <template v-if="registering.requiresWaiver">
                        <div class="text-caption text-medium-emphasis mt-3 mb-1">
                            {{ isMinor(form.birthdate) ? 'Holder is under 18 — a parent/guardian must sign' : 'Signature' }}
                        </div>
                        <v-row v-if="isMinor(form.birthdate)" dense>
                            <v-col cols="6">
                                <v-text-field v-model="form.parentName" label="Parent/guardian name"
                                    density="compact" hide-details></v-text-field>
                            </v-col>
                            <v-col cols="6">
                                <v-text-field v-model="form.parentPhone" type="tel" label="Parent/guardian phone"
                                    density="compact" hide-details></v-text-field>
                            </v-col>
                        </v-row>
                        <div :class="isMinor(form.birthdate) ? 'mt-3' : ''">
                            <SignaturePad v-model="form.signatureDataUrl" />
                        </div>
                    </template>

                    <div v-if="formError" class="text-error text-body-2 mt-3">{{ formError }}</div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="saving" @click="closeRegister">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="saveRegistration">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import dayjs from 'dayjs'
import { SeasonPassService, type MySeasonPass } from '@/services/SeasonPassService'
import QrCode from '@/components/QrCode.vue'
import PhotoCapture from '@/components/PhotoCapture.vue'
import SignaturePad from '@/components/SignaturePad.vue'
import { branding } from '@/stores/branding'

const service = new SeasonPassService()
const passes = ref<MySeasonPass[]>([])
const loading = ref(false)

const registerOpen = ref(false)
const registering = ref<MySeasonPass | null>(null)
const saving = ref(false)
const formError = ref('')
const form = reactive({
    firstName: '', lastName: '', birthdate: '',
    photoDataUrl: null as string | null,
    signatureDataUrl: null as string | null,
    parentName: '', parentPhone: '',
})

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')
const todayIso = dayjs().format('YYYY-MM-DD')

function formatDate(iso: string): string { return dayjs(iso).format('MMM D, YYYY') }
function daysLabel(days: number[] | null): string {
    if (!days || days.length === 0) return 'Selected days'
    const names = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
    return days.slice().sort((a, b) => a - b).map(d => names[d]).join('/')
}
function holderName(p: MySeasonPass): string {
    return [p.holderFirstName, p.holderLastName].filter(Boolean).join(' ').trim()
}
function isMinor(birthdate: string): boolean {
    if (!birthdate) return false
    return dayjs().diff(dayjs(birthdate), 'year') < 18
}

function openRegister(p: MySeasonPass) {
    registering.value = p
    form.firstName = p.holderFirstName ?? ''
    form.lastName = p.holderLastName ?? ''
    form.birthdate = ''
    form.photoDataUrl = null
    form.signatureDataUrl = null
    form.parentName = ''
    form.parentPhone = ''
    formError.value = ''
    registerOpen.value = true
}

function closeRegister() {
    registerOpen.value = false
    registering.value = null
}

async function saveRegistration() {
    if (!registering.value) return
    formError.value = ''
    const who = form.firstName.trim() || 'This holder'
    if (!form.firstName.trim() || !form.lastName.trim()) {
        formError.value = "Enter the holder's first and last name."; return
    }
    if (!form.photoDataUrl) {
        formError.value = `${who} needs a photo — the gate uses it to verify the pass holder.`; return
    }
    if (registering.value.requiresWaiver) {
        if (!form.birthdate) { formError.value = `${who} needs a date of birth to sign the waiver.`; return }
        if (!form.signatureDataUrl) { formError.value = `${who} needs to sign the waiver.`; return }
        if (isMinor(form.birthdate) && !form.parentName.trim()) {
            formError.value = `A parent/guardian name is required for ${who}.`; return
        }
    }

    saving.value = true
    try {
        await service.completeRegistration([{
            purchaseId: registering.value.id,
            firstName: form.firstName.trim(),
            lastName: form.lastName.trim(),
            birthdate: form.birthdate || null,
            photoDataUrl: form.photoDataUrl,
            waiverSignatureDataUrl: registering.value.requiresWaiver ? form.signatureDataUrl : null,
            parentGuardianName: registering.value.requiresWaiver && isMinor(form.birthdate)
                ? form.parentName.trim() : null,
            parentGuardianPhone: registering.value.requiresWaiver && isMinor(form.birthdate)
                ? form.parentPhone.trim() || null : null,
        }])
        closeRegister()
        flash('Pass registered — you\'re good to go.', 'success')
        await load()
    } catch (err: any) {
        formError.value = err.response?.data?.error
            || 'Could not save the registration. Please try again.'
    } finally {
        saving.value = false
    }
}

async function load() {
    loading.value = true
    try {
        const r = await service.listMine()
        passes.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error
            || 'Could not load your season passes. Refresh to try again, or check your connection.', 'error')
    } finally {
        loading.value = false
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

onMounted(async () => {
    // A season-pass checkout using a redirect-based payment method (3DS, wallet) lands back here.
    // Surface the outcome so a failed payment isn't silently shown as "no new pass" and a succeeded
    // one explains the brief delay before the pass appears (the webhook finalizes it).
    const params = new URLSearchParams(window.location.search)
    const redirectStatus = params.get('redirect_status')
    if (params.get('payment_intent') && redirectStatus) {
        flash(redirectStatus === 'succeeded'
            ? 'Payment received. Finish registration below so your pass is ready for the gate.'
            : 'Your payment was not completed. Please try again.',
            redirectStatus === 'succeeded' ? 'success' : 'error')
        history.replaceState(null, '', window.location.pathname)
    }

    await load()
})
</script>
