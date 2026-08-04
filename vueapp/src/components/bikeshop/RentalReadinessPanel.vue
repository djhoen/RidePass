<template>
    <div>
        <div v-if="loading" class="text-caption text-medium-emphasis">Checking…</div>

        <template v-else-if="readiness">
            <!-- Both gates, always visible, so staff see what's outstanding before they try to
                 hand gear over rather than being refused at the button. -->
            <div class="d-flex align-center ga-2 mb-1">
                <v-icon :icon="readiness.agreementSigned ? 'mdi-check-circle' : 'mdi-alert-circle-outline'"
                    :color="readiness.agreementSigned ? 'success' : 'warning'" size="18"></v-icon>
                <span class="text-body-2">Rental agreement</span>
                <span v-if="!readiness.agreementRequired" class="text-caption text-medium-emphasis">
                    not required
                </span>
                <v-spacer></v-spacer>
                <SignAgreementDialog v-if="readiness.agreementRequired" kind="rental_agreement"
                    :rental-id="rentalId" :default-signer-name="renterName"
                    :default-signer-email="renterEmail" @signed="load" />
            </div>

            <!-- Waiver is per RIDER: a rental for three riders needs three signatures, so this
                 shows progress and names who has signed rather than a single yes/no. -->
            <div class="d-flex align-center ga-2">
                <v-icon :icon="readiness.waiverSigned ? 'mdi-check-circle' : 'mdi-alert-circle-outline'"
                    :color="readiness.waiverSigned ? 'success' : 'warning'" size="18"></v-icon>
                <span class="text-body-2">Waiver</span>
                <span v-if="!readiness.waiverRequired" class="text-caption text-medium-emphasis">
                    not required
                </span>
                <span v-else class="text-caption"
                    :class="readiness.waiverSigned ? 'text-medium-emphasis' : 'text-warning'">
                    {{ readiness.ridersSigned }} of {{ readiness.ridersRequired }}
                    rider{{ readiness.ridersRequired === 1 ? '' : 's' }} signed
                </span>
                <v-spacer></v-spacer>
                <v-btn v-if="readiness.waiverRequired && !readiness.waiverSigned && !customerWaiverWait"
                    size="small" color="primary" prepend-icon="mdi-draw-pen"
                    @click="waiverOpen = true">
                    {{ readiness.ridersSigned > 0 ? 'Sign next rider' : 'Sign waiver' }}
                </v-btn>
                <!-- Hand the waiver to the paired customer-facing display instead. -->
                <v-btn v-if="readiness.waiverRequired && !readiness.waiverSigned && shopDisplayPaired && !customerWaiverWait"
                    size="small" variant="tonal" color="primary" prepend-icon="mdi-tablet" class="ml-2"
                    @click="waiverOnCustomerScreen">
                    Customer screen
                </v-btn>
                <template v-if="customerWaiverWait">
                    <v-progress-circular indeterminate size="16" width="2" class="ml-2" />
                    <span class="text-caption ml-1">Waiting for the customer to sign…</span>
                    <v-btn size="small" variant="text" @click="cancelCustomerWaiver">Cancel</v-btn>
                </template>
            </div>
            <div v-if="cfdError" class="text-caption text-error ml-6 mt-1">{{ cfdError }}</div>

            <!-- Who's already signed, so staff know who they still need. -->
            <div v-if="readiness.signers.length > 0" class="ml-6 mt-1">
                <div v-for="s in readiness.signers" :key="s.signatureId"
                    class="text-caption text-medium-emphasis">
                    <v-icon icon="mdi-check" size="12" color="success"></v-icon>
                    {{ s.riderName || 'Rider' }}
                    <span v-if="s.signedByParent">(signed by {{ s.parentName || 'parent' }})</span>
                </div>
            </div>

            <v-alert v-if="!readiness.canCheckOut" type="warning" variant="tonal" density="compact" class="mt-3">
                <template v-if="readiness.waiverRequired && readiness.ridersOutstanding > 0">
                    {{ readiness.ridersOutstanding }} more
                    rider{{ readiness.ridersOutstanding === 1 ? '' : 's' }} must sign the waiver before
                    this gear can go out.
                </template>
                <template v-else>
                    Gear can't be checked out until both are signed.
                </template>
            </v-alert>

            <!-- For a rental booked online, nobody is at the counter to sign. Email them a link
                 so it's done before they arrive. -->
            <div v-if="!readiness.canCheckOut" class="mt-3">
                <v-btn size="small" variant="text" prepend-icon="mdi-email-fast-outline"
                    :loading="sendingLink" @click="sendLink">
                    Email the renter a signing link
                </v-btn>
                <div v-if="linkMessage" class="text-caption mt-1"
                    :class="linkError ? 'text-error' : 'text-success'">{{ linkMessage }}</div>
            </div>
        </template>

        <!-- Waiver capture, for walk-ins with no account who have never signed. -->
        <v-dialog v-model="waiverOpen" max-width="560" persistent scrollable>
            <v-card class="d-flex flex-column" style="max-height: 90vh">
                <v-card-title class="d-flex align-center" style="flex: 0 0 auto">
                    <span class="text-body-1">{{ waiver?.title || 'Waiver' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="saving"
                        @click="waiverOpen = false"></v-btn>
                </v-card-title>
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                    <div v-if="waiverLoading" class="text-center py-4">
                        <v-progress-circular indeterminate size="24"></v-progress-circular>
                    </div>
                    <template v-else>
                        <div class="waiver-body text-body-2 mb-4">{{ waiver?.body }}</div>

                        <v-row dense>
                            <v-col cols="6">
                                <v-text-field v-model="form.firstName" label="Rider first name"
                                    density="compact" hide-details :disabled="saving"></v-text-field>
                            </v-col>
                            <v-col cols="6">
                                <v-text-field v-model="form.lastName" label="Rider last name"
                                    density="compact" hide-details :disabled="saving"></v-text-field>
                            </v-col>
                        </v-row>
                        <v-text-field v-model="form.email" type="email" label="Email (optional)"
                            density="compact" class="mt-4" hide-details :disabled="saving"></v-text-field>
                        <v-text-field v-model="form.birthdate" type="date" label="Date of birth"
                            density="compact" class="mt-4" hide-details :disabled="saving"
                            hint="Needed to know whether a guardian must sign." persistent-hint></v-text-field>

                        <!-- A minor can't sign for themselves. -->
                        <v-alert v-if="isMinor" type="info" variant="tonal" density="compact" class="mt-4">
                            This rider is under 18, so a parent or guardian signs.
                        </v-alert>
                        <template v-if="isMinor">
                            <v-text-field v-model="form.parentName" label="Parent/guardian name"
                                density="compact" class="mt-4" hide-details :disabled="saving"></v-text-field>
                            <v-text-field v-model="form.parentPhone" label="Parent/guardian phone"
                                density="compact" class="mt-4" hide-details :disabled="saving"></v-text-field>
                        </template>

                        <div class="text-subtitle-2 mt-4 mb-1">
                            {{ isMinor ? 'Parent/guardian signature' : 'Signature' }}
                        </div>
                        <SignaturePad v-model="signatureDataUrl" :disabled="saving" />
                        <div v-if="error" class="text-error text-caption mt-2">{{ error }}</div>
                    </template>
                </v-card-text>
                <v-card-actions style="flex: 0 0 auto">
                    <v-spacer></v-spacer>
                    <v-btn :disabled="saving" @click="waiverOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" :disabled="!canSubmit" @click="submitWaiver">
                        Accept and sign
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue'
import dayjs from 'dayjs'
import SignaturePad from '@/components/SignaturePad.vue'
import SignAgreementDialog from '@/components/bikeshop/SignAgreementDialog.vue'
import { BikeShopService, type RentalCheckoutReadiness } from '@/services/BikeShopService'
import { WaiverService } from '@/services/WaiverService'
import {
    shopDisplayPaired, requestSignatureOnDisplay, pushShopDisplayState, idleShopDisplayState,
    type SignatureRequestHandle,
} from '@/helpers/ShopDisplay'

const props = defineProps<{
    rentalId: string
    renterName?: string | null
    renterEmail?: string | null
}>()
const emit = defineEmits<{ (e: 'changed'): void }>()

const service = new BikeShopService()
const waiverService = new WaiverService()

const loading = ref(false)
const readiness = ref<RentalCheckoutReadiness | null>(null)
const waiverOpen = ref(false)
const waiverLoading = ref(false)
const waiver = ref<{ title: string; body: string } | null>(null)
const saving = ref(false)
const error = ref('')
const signatureDataUrl = ref<string | null>(null)
const form = ref({
    firstName: '', lastName: '', email: '', birthdate: '',
    parentName: '', parentPhone: '',
})

const isMinor = computed(() =>
    !!form.value.birthdate && dayjs().diff(dayjs(form.value.birthdate), 'year') < 18)

const canSubmit = computed(() =>
    !!form.value.firstName.trim() && !!form.value.lastName.trim()
    && !!signatureDataUrl.value
    && (!isMinor.value || !!form.value.parentName.trim()))

const sendingLink = ref(false)
const linkMessage = ref('')
const linkError = ref(false)

async function sendLink() {
    sendingLink.value = true
    linkMessage.value = ''
    linkError.value = false
    try {
        await service.sendRentalSigningLink(props.rentalId)
        linkMessage.value = 'Sent. They can sign from their phone before pickup.'
    } catch (e: any) {
        linkError.value = true
        linkMessage.value = e.response?.data?.error
            || 'Could not send the link. Check the renter has an email on file.'
    } finally {
        sendingLink.value = false
    }
}

async function load() {
    loading.value = true
    try {
        readiness.value = (await service.rentalReadiness(props.rentalId)).data.data
        emit('changed')
    } catch {
        // A readiness read failing shouldn't break the row; the server still enforces the gate
        // at check-out, so the worst case is the panel stays blank.
        readiness.value = null
    } finally {
        loading.value = false
    }
}

async function loadWaiver() {
    waiverLoading.value = true
    error.value = ''
    try {
        const r = await waiverService.getActive()
        waiver.value = (r.data as any).data ?? null
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not load the waiver text.'
    } finally {
        waiverLoading.value = false
    }
}

async function submitWaiver() {
    if (!canSubmit.value) return
    saving.value = true
    error.value = ''
    try {
        await service.signRentalWaiver(props.rentalId, {
            firstName: form.value.firstName.trim(),
            lastName: form.value.lastName.trim(),
            email: form.value.email.trim() || null,
            birthdate: form.value.birthdate || null,
            signatureDataUrl: signatureDataUrl.value!,
            signedByParent: isMinor.value,
            parentName: form.value.parentName.trim() || null,
            parentPhone: form.value.parentPhone.trim() || null,
        })
        waiverOpen.value = false
        await load()
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not save the waiver. Try again.'
    } finally {
        saving.value = false
    }
}

// ── Waiver on the paired customer-facing display ─────────────────────────
const customerWaiverWait = ref(false)
const cfdError = ref('')
let cfdHandle: SignatureRequestHandle | null = null

async function waiverOnCustomerScreen() {
    cfdError.value = ''
    if (!waiver.value) await loadWaiver()
    if (!waiver.value) {
        cfdError.value = 'No waiver is published for this track yet.'
        return
    }
    customerWaiverWait.value = true
    try {
        cfdHandle = requestSignatureOnDisplay({
            docKind: 'waiver',
            title: waiver.value.title,
            body: waiver.value.body,
            // Prefill from the renter; for the SECOND rider onward they retype, same as the dialog.
            signerName: readiness.value?.ridersSigned ? null : props.renterName ?? null,
            signerEmail: readiness.value?.ridersSigned ? null : props.renterEmail ?? null,
        })
        const resp = await cfdHandle.promise
        if (!resp) return   // cancelled by the cashier
        const first = (resp.firstName ?? '').trim()
        const last = (resp.lastName ?? '').trim()
        if (!first || !last || !resp.signatureDataUrl) {
            cfdError.value = 'The signature came back incomplete. Try again or sign on this screen.'
            return
        }
        await service.signRentalWaiver(props.rentalId, {
            firstName: first,
            lastName: last,
            email: (resp.email ?? '').trim() || null,
            birthdate: resp.birthdate || null,
            signatureDataUrl: resp.signatureDataUrl,
            signedByParent: resp.signedByParent ?? false,
            parentName: (resp.parentName ?? '').trim() || null,
            parentPhone: (resp.parentPhone ?? '').trim() || null,
        })
        await load()
    } catch (e: any) {
        cfdError.value = e.response?.data?.error || 'Could not complete the customer-screen waiver. Try again.'
    } finally {
        customerWaiverWait.value = false
        cfdHandle = null
        // Whatever happened, don't leave the waiver stuck on the customer's screen.
        pushShopDisplayState(idleShopDisplayState()).catch(() => { /* best-effort */ })
    }
}

function cancelCustomerWaiver() { cfdHandle?.cancel() }
onUnmounted(() => cfdHandle?.cancel())

// Seed the rider name from the renter so staff usually only sign, not retype.
watch(waiverOpen, open => {
    if (!open) return
    signatureDataUrl.value = null
    const parts = (props.renterName ?? '').trim().split(/\s+/)
    form.value = {
        firstName: parts[0] ?? '',
        lastName: parts.slice(1).join(' '),
        email: props.renterEmail ?? '',
        birthdate: '', parentName: '', parentPhone: '',
    }
    loadWaiver()
})

watch(() => props.rentalId, load, { immediate: true })
defineExpose({ reload: load })
</script>

<style scoped>
.waiver-body { white-space: pre-wrap; }
</style>
