<template>
    <div>
        <!-- Trigger doubles as the status: green once signed against the current terms. -->
        <v-btn size="small" :variant="signed ? 'tonal' : 'flat'" :color="signed ? 'success' : 'primary'"
            :prepend-icon="signed ? 'mdi-check-circle' : 'mdi-draw-pen'" :loading="loading"
            :disabled="customerWait" @click="openDialog">
            {{ signed ? 'Signed' : signLabel }}
        </v-btn>
        <!-- Hand the reading + signing to the paired customer-facing display instead. -->
        <v-btn v-if="!signed && shopDisplayPaired && !customerWait" size="small" variant="tonal"
            color="primary" prepend-icon="mdi-tablet" class="ml-2" @click="signOnCustomerScreen">
            Customer screen
        </v-btn>
        <template v-if="customerWait">
            <v-progress-circular indeterminate size="16" width="2" class="ml-2" />
            <span class="text-caption ml-1">Waiting for the customer to sign…</span>
            <v-btn size="small" variant="text" @click="cancelCustomerSign">Cancel</v-btn>
        </template>
        <span v-if="cfdError" class="text-caption text-error ml-2">{{ cfdError }}</span>
        <span v-if="signed && latest" class="text-caption text-medium-emphasis ml-2">
            {{ latest.signerName }} · {{ formatWhen(latest.signedAt) }}
        </span>

        <v-dialog v-model="open" max-width="640" persistent scrollable>
            <v-card class="d-flex flex-column" style="max-height: 90vh">
                <v-card-title class="d-flex align-center" style="flex: 0 0 auto">
                    <span class="text-body-1">{{ agreement?.title || 'Agreement' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="saving"
                        @click="open = false"></v-btn>
                </v-card-title>

                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                    <v-alert v-if="!agreement" type="warning" variant="tonal" class="mb-3">
                        No agreement has been published yet. Add one in Bike Shop settings before
                        collecting signatures.
                    </v-alert>

                    <template v-else>
                        <!-- The customer reads this on the tablet, so it gets real space. -->
                        <div class="agreement-body text-body-2 mb-4">{{ agreement.body }}</div>

                        <v-text-field v-model="signerName" label="Name of person signing"
                            density="compact" hide-details :disabled="saving"></v-text-field>
                        <v-text-field v-model="signerEmail" type="email" label="Email (optional)"
                            density="compact" class="mt-4" hide-details :disabled="saving"></v-text-field>

                        <div class="text-subtitle-2 mt-4 mb-1">Signature</div>
                        <SignaturePad v-model="signatureDataUrl" :disabled="saving" />

                        <div v-if="error" class="text-error text-caption mt-2">{{ error }}</div>

                        <!-- Previous signatures matter when terms have been re-published: they
                             show what was agreed to before, and no longer satisfy the gate. -->
                        <template v-if="priorSignatures.length">
                            <v-divider class="my-4"></v-divider>
                            <div class="text-caption text-medium-emphasis mb-1">Earlier signatures</div>
                            <div v-for="sg in priorSignatures" :key="sg.id" class="text-caption text-medium-emphasis">
                                {{ sg.signerName }} · v{{ sg.agreementVersion }} · {{ formatWhen(sg.signedAt) }}
                                <span v-if="agreement && sg.agreementVersion !== agreement.version"
                                    class="text-warning"> (older terms)</span>
                            </div>
                        </template>
                    </template>
                </v-card-text>

                <v-card-actions style="flex: 0 0 auto">
                    <v-spacer></v-spacer>
                    <v-btn :disabled="saving" @click="open = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" :disabled="!canSubmit" @click="submit">
                        Accept and sign
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue'
import { formatTenantDateTime } from '@/helpers/TenantTime'
import SignaturePad from '@/components/SignaturePad.vue'
import { BikeShopService, type ShopAgreement, type ShopAgreementSignature } from '@/services/BikeShopService'
import {
    shopDisplayPaired, requestSignatureOnDisplay, pushShopDisplayState, idleShopDisplayState,
    type SignatureRequestHandle,
} from '@/helpers/ShopDisplay'

// Signed IN PERSON on the shop's device, with the customer present. Exactly one owner.
const props = defineProps<{
    kind: 'rental_agreement' | 'work_order_terms'
    workOrderId?: string | null
    rentalId?: string | null
    defaultSignerName?: string | null
    defaultSignerEmail?: string | null
}>()
const emit = defineEmits<{ (e: 'signed'): void }>()

const service = new BikeShopService()

const open = ref(false)
const loading = ref(false)
const saving = ref(false)
const error = ref('')
const agreement = ref<ShopAgreement | null>(null)
const signatures = ref<ShopAgreementSignature[]>([])
const signerName = ref('')
const signerEmail = ref('')
const signatureDataUrl = ref<string | null>(null)

const signLabel = computed(() =>
    props.kind === 'rental_agreement' ? 'Sign rental agreement' : 'Sign authorization')

// Only a signature against the CURRENT version counts: re-published terms are different terms,
// which is exactly what the server's checkout gate enforces.
const latest = computed(() =>
    signatures.value.find(s => agreement.value && s.agreementVersion === agreement.value.version) ?? null)
const signed = computed(() => latest.value !== null)
const priorSignatures = computed(() => signatures.value.filter(s => s !== latest.value))

const canSubmit = computed(() =>
    !!agreement.value && !!signerName.value.trim() && !!signatureDataUrl.value)

async function load() {
    loading.value = true
    error.value = ''
    try {
        const r = await service.getAgreementForSigning(props.kind, {
            workOrderId: props.workOrderId ?? undefined,
            rentalId: props.rentalId ?? undefined,
        })
        agreement.value = r.data.data.agreement
        signatures.value = r.data.data.signatures ?? []
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not load the agreement. Try again.'
    } finally {
        loading.value = false
    }
}

async function openDialog() {
    signerName.value = props.defaultSignerName ?? ''
    signerEmail.value = props.defaultSignerEmail ?? ''
    signatureDataUrl.value = null
    error.value = ''
    await load()
    open.value = true
}

async function submit() {
    if (!canSubmit.value) return
    saving.value = true
    error.value = ''
    try {
        await service.signAgreement(props.kind, {
            workOrderId: props.workOrderId ?? null,
            rentalId: props.rentalId ?? null,
            signerName: signerName.value.trim(),
            signerEmail: signerEmail.value.trim() || null,
            signatureDataUrl: signatureDataUrl.value!,
        })
        await load()
        open.value = false
        emit('signed')
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not save the signature. Try again.'
    } finally {
        saving.value = false
    }
}

// ── Sign on the paired customer-facing display ────────────────────────────
const customerWait = ref(false)
const cfdError = ref('')
let cfdHandle: SignatureRequestHandle | null = null

async function signOnCustomerScreen() {
    cfdError.value = ''
    if (!agreement.value) await load()
    if (!agreement.value) {
        cfdError.value = 'No agreement has been published yet. Add one in Bike Shop settings.'
        return
    }
    customerWait.value = true
    try {
        cfdHandle = requestSignatureOnDisplay({
            docKind: props.kind,
            title: agreement.value.title,
            body: agreement.value.body,
            signerName: props.defaultSignerName ?? null,
            signerEmail: props.defaultSignerEmail ?? null,
        })
        const resp = await cfdHandle.promise
        if (!resp) return   // cancelled by the cashier
        const name = (resp.signerName ?? '').trim()
        if (!name || !resp.signatureDataUrl) {
            cfdError.value = 'The signature came back incomplete. Try again or sign on this screen.'
            return
        }
        await service.signAgreement(props.kind, {
            workOrderId: props.workOrderId ?? null,
            rentalId: props.rentalId ?? null,
            signerName: name,
            signerEmail: (resp.signerEmail ?? '').trim() || null,
            signatureDataUrl: resp.signatureDataUrl,
        })
        await load()
        emit('signed')
    } catch (e: any) {
        cfdError.value = e.response?.data?.error || 'Could not complete the customer-screen signature. Try again.'
    } finally {
        customerWait.value = false
        cfdHandle = null
        // Whatever happened, don't leave the document stuck on the customer's screen.
        pushShopDisplayState(idleShopDisplayState()).catch(() => { /* best-effort */ })
    }
}

function cancelCustomerSign() { cfdHandle?.cancel() }
onUnmounted(() => cfdHandle?.cancel())

function formatWhen(iso: string): string { return formatTenantDateTime(iso, 'MMM D, YYYY h:mm A') }

// Load quietly on mount so the button can show signed state without being opened.
watch(() => [props.workOrderId, props.rentalId, props.kind], load, { immediate: true })
defineExpose({ reload: load })
</script>

<style scoped>
/* Preserve the paragraph breaks a tenant typed into the terms. */
.agreement-body { white-space: pre-wrap; }
</style>
