<template>
    <v-container class="sign-root py-6">
        <div v-if="loading" class="text-center py-12">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <v-alert v-else-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>

        <template v-else-if="info">
            <h1 class="text-h5 font-weight-bold mb-1">Before you ride</h1>
            <p class="text-body-2 text-medium-emphasis mb-4">
                {{ info.renterName ? `Hi ${info.renterName}, ` : '' }}sign these and your gear will be
                ready to collect without paperwork at the counter.
            </p>

            <!-- What they're signing for. -->
            <v-card variant="outlined" class="mb-4">
                <v-card-text class="py-3">
                    <div v-for="(it, i) in info.items" :key="i" class="text-body-2">
                        {{ it.quantity }}x {{ it.name }}<span v-if="it.variantLabel"> ({{ it.variantLabel }})</span>
                    </div>
                    <div class="text-caption text-medium-emphasis mt-1">
                        {{ formatWhen(info.startsAt) }} to {{ formatWhen(info.endsAt) }}
                    </div>
                    <div v-if="info.depositCents > 0" class="text-caption text-medium-emphasis">
                        Refundable deposit: {{ money(info.depositCents) }}
                    </div>
                </v-card-text>
            </v-card>

            <v-alert v-if="info.closed" type="info" variant="tonal" class="mb-4">
                This rental is already {{ info.status }}, so there's nothing left to sign.
            </v-alert>

            <template v-else>
                <v-alert v-if="allDone" type="success" variant="tonal" class="mb-4">
                    All signed. You're good to collect your gear.
                </v-alert>

                <!-- Rental agreement -->
                <v-card v-if="info.agreement" variant="outlined" class="mb-4">
                    <v-card-title class="d-flex align-center text-body-1">
                        <v-icon :icon="agreementSigned ? 'mdi-check-circle' : 'mdi-file-document-outline'"
                            :color="agreementSigned ? 'success' : undefined" size="20" class="mr-2"></v-icon>
                        {{ info.agreement.title }}
                    </v-card-title>
                    <v-card-text>
                        <div v-if="agreementSigned" class="text-body-2 text-success">Signed. Thank you.</div>
                        <template v-else>
                            <div class="doc-body text-body-2 mb-4">{{ info.agreement.body }}</div>
                            <v-text-field v-model="agreeName" label="Your full name" density="compact"
                                hide-details :disabled="savingAgreement"></v-text-field>
                            <div class="text-subtitle-2 mt-4 mb-1">Signature</div>
                            <SignaturePad v-model="agreeSignature" :disabled="savingAgreement" />
                            <div v-if="agreementError" class="text-error text-caption mt-2">{{ agreementError }}</div>
                            <v-btn color="primary" class="mt-3" :loading="savingAgreement"
                                :disabled="!canSignAgreement" @click="submitAgreement">
                                Accept and sign
                            </v-btn>
                        </template>
                    </v-card-text>
                </v-card>

                <!-- Liability waiver -->
                <v-card v-if="info.waiverRequired && info.waiver" variant="outlined" class="mb-4">
                    <v-card-title class="d-flex align-center text-body-1">
                        <v-icon :icon="waiverSigned ? 'mdi-check-circle' : 'mdi-file-document-outline'"
                            :color="waiverSigned ? 'success' : undefined" size="20" class="mr-2"></v-icon>
                        {{ info.waiver.title }}
                    </v-card-title>
                    <v-card-text>
                        <div v-if="waiverSigned" class="text-body-2 text-success">Signed. Thank you.</div>
                        <template v-else>
                            <div class="doc-body text-body-2 mb-4">{{ info.waiver.body }}</div>
                            <v-row dense>
                                <v-col cols="6">
                                    <v-text-field v-model="w.firstName" label="Rider first name"
                                        density="compact" hide-details :disabled="savingWaiver"></v-text-field>
                                </v-col>
                                <v-col cols="6">
                                    <v-text-field v-model="w.lastName" label="Rider last name"
                                        density="compact" hide-details :disabled="savingWaiver"></v-text-field>
                                </v-col>
                            </v-row>
                            <v-text-field v-model="w.birthdate" type="date" label="Date of birth"
                                density="compact" class="mt-4" hide-details :disabled="savingWaiver"></v-text-field>

                            <v-alert v-if="isMinor" type="info" variant="tonal" density="compact" class="mt-4">
                                This rider is under 18, so a parent or guardian signs.
                            </v-alert>
                            <template v-if="isMinor">
                                <v-text-field v-model="w.parentName" label="Parent/guardian name"
                                    density="compact" class="mt-4" hide-details :disabled="savingWaiver"></v-text-field>
                                <v-text-field v-model="w.parentPhone" label="Parent/guardian phone"
                                    density="compact" class="mt-4" hide-details :disabled="savingWaiver"></v-text-field>
                            </template>

                            <div class="text-subtitle-2 mt-4 mb-1">
                                {{ isMinor ? 'Parent/guardian signature' : 'Signature' }}
                            </div>
                            <SignaturePad v-model="waiverSignature" :disabled="savingWaiver" />
                            <div v-if="waiverError" class="text-error text-caption mt-2">{{ waiverError }}</div>
                            <v-btn color="primary" class="mt-3" :loading="savingWaiver"
                                :disabled="!canSignWaiver" @click="submitWaiver">
                                Accept and sign
                            </v-btn>
                        </template>
                    </v-card-text>
                </v-card>
            </template>
        </template>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import SignaturePad from '@/components/SignaturePad.vue'
import { BikeShopService } from '@/services/BikeShopService'

// Public page: the emailed link's token is the whole credential. No login required, and the
// token only ever reaches this page for this one rental.
const route = useRoute()
const service = new BikeShopService()
const token = route.params.token as string

const loading = ref(true)
const loadError = ref('')
const info = ref<any>(null)

const agreeName = ref('')
const agreeSignature = ref<string | null>(null)
const savingAgreement = ref(false)
const agreementError = ref('')

const w = ref({ firstName: '', lastName: '', birthdate: '', parentName: '', parentPhone: '' })
const waiverSignature = ref<string | null>(null)
const savingWaiver = ref(false)
const waiverError = ref('')

const agreementSigned = computed(() => !!info.value?.agreementSigned)
const waiverSigned = computed(() => !!info.value?.waiverSigned)
const allDone = computed(() =>
    (!info.value?.agreement || agreementSigned.value)
    && (!info.value?.waiverRequired || waiverSigned.value))

const isMinor = computed(() =>
    !!w.value.birthdate && dayjs().diff(dayjs(w.value.birthdate), 'year') < 18)

const canSignAgreement = computed(() => !!agreeName.value.trim() && !!agreeSignature.value)
const canSignWaiver = computed(() =>
    !!w.value.firstName.trim() && !!w.value.lastName.trim() && !!waiverSignature.value
    && (!isMinor.value || !!w.value.parentName.trim()))

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        info.value = (await service.getRentalSigning(token)).data.data
        // Seed names from the rental so most renters only have to sign.
        if (!agreeName.value) agreeName.value = info.value.renterName ?? ''
        if (!w.value.firstName && info.value.renterName) {
            const parts = String(info.value.renterName).trim().split(/\s+/)
            w.value.firstName = parts[0] ?? ''
            w.value.lastName = parts.slice(1).join(' ')
        }
    } catch (e: any) {
        loadError.value = e.response?.data?.error
            || 'This signing link isn’t valid. Ask the shop to send a new one.'
    } finally {
        loading.value = false
    }
}

async function submitAgreement() {
    if (!canSignAgreement.value) return
    savingAgreement.value = true
    agreementError.value = ''
    try {
        await service.signRentalAgreementPublic(token, {
            signerName: agreeName.value.trim(),
            signatureDataUrl: agreeSignature.value!,
        })
        await load()
    } catch (e: any) {
        agreementError.value = e.response?.data?.error || 'Could not save your signature. Try again.'
    } finally {
        savingAgreement.value = false
    }
}

async function submitWaiver() {
    if (!canSignWaiver.value) return
    savingWaiver.value = true
    waiverError.value = ''
    try {
        await service.signRentalWaiverPublic(token, {
            firstName: w.value.firstName.trim(),
            lastName: w.value.lastName.trim(),
            birthdate: w.value.birthdate || null,
            signatureDataUrl: waiverSignature.value!,
            signedByParent: isMinor.value,
            parentName: w.value.parentName.trim() || null,
            parentPhone: w.value.parentPhone.trim() || null,
        })
        await load()
    } catch (e: any) {
        waiverError.value = e.response?.data?.error || 'Could not save your signature. Try again.'
    } finally {
        savingWaiver.value = false
    }
}

function money(c: number): string { return `$${(c / 100).toFixed(2)}` }
function formatWhen(iso: string): string { return dayjs(iso).format('MMM D, h:mm A') }

onMounted(load)
</script>

<style scoped>
.sign-root { max-width: 640px; }
.doc-body { white-space: pre-wrap; max-height: 320px; overflow-y: auto; }
</style>
