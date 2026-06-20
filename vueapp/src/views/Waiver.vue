<template>
    <v-container style="max-width: 720px">
        <h1 class="text-h4 mb-4">Waiver</h1>

        <div v-if="loading" class="d-flex justify-center py-8">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <template v-else>
            <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

            <v-alert v-else-if="alreadySigned" type="success" variant="tonal" class="mb-4">
                Your waiver is on file. You can close this page or
                <a href="javascript:void(0)" @click="goBack">return to where you were</a>.
            </v-alert>

            <v-card v-else-if="waiver" class="pa-4">
                <v-card-title>{{ waiver.title }}</v-card-title>
                <v-card-subtitle class="mb-2">Version {{ waiver.version }}</v-card-subtitle>
                <v-card-text>
                    <v-alert v-if="riderIsMinor" type="info" variant="tonal" density="compact" class="mb-3">
                        You're under 18 — a parent or guardian must sign on your behalf.
                        Please hand the device to them and have them fill in their info below.
                    </v-alert>
                    <div v-if="hasBody(waiver.body)" class="waiver-body">
                        <RichTextView :html="waiver.body" />
                    </div>
                    <div v-else class="text-medium-emphasis">
                        The track hasn't filled in waiver text yet. Ask them to.
                    </div>

                    <v-row v-if="riderIsMinor" class="mt-2">
                        <v-col cols="12" md="6">
                            <v-text-field v-model="parentName" label="Parent / guardian name" required></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <PhoneField v-model="parentPhone" label="Parent / guardian phone" required />
                        </v-col>
                    </v-row>

                    <div class="mt-4">
                        <div class="text-subtitle-2 mb-1">
                            {{ riderIsMinor ? 'Parent signs below' : 'Sign below' }}
                        </div>
                        <SignaturePad v-model="signatureDataUrl" />
                    </div>

                    <div class="d-flex align-center mt-4 ga-2 flex-wrap">
                        <v-btn variant="text" @click="goBack">Cancel</v-btn>
                        <v-spacer></v-spacer>
                        <v-btn color="primary" :loading="signing" :disabled="!canSign" @click="sign">
                            I agree, sign
                        </v-btn>
                    </div>
                </v-card-text>
            </v-card>

            <v-card v-else class="pa-4">
                <v-card-text class="text-medium-emphasis">
                    No active waiver to sign right now.
                </v-card-text>
            </v-card>
        </template>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { PassService, type WaiverDto, type WaiverSignatureStatus } from '@/services/PassService'
import RichTextView from '@/components/RichTextView.vue'
import SignaturePad from '@/components/SignaturePad.vue'
import PhoneField from '@/components/PhoneField.vue'
import authHelper from '@/helpers/AuthHelper'

const route = useRoute()
const router = useRouter()
const passService = new PassService()

const loading = ref(true)
const loadError = ref('')
const waiver = ref<WaiverDto | null>(null)
const sigStatus = ref<WaiverSignatureStatus | null>(null)

const parentName = ref('')
const parentPhone = ref('')
const signatureDataUrl = ref<string | null>(null)
const signing = ref(false)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const alreadySigned = computed(() => !!sigStatus.value?.hasSignedCurrent)

const riderIsMinor = computed(() => sigStatus.value?.riderIsMinor === true)

const canSign = computed(() => {
    if (!signatureDataUrl.value) return false
    if (riderIsMinor.value) {
        return parentName.value.trim().length > 0 && parentPhone.value.trim().length > 0
    }
    return true
})

function hasBody(html: string | null | undefined): boolean {
    if (!html) return false
    return html.replace(/<[^>]+>/g, '').trim().length > 0
}

async function load() {
    if (!authHelper.isAuthenticated()) {
        router.push(`/Login?next=${encodeURIComponent(currentPath())}`)
        return
    }
    loading.value = true
    loadError.value = ''
    try {
        const [w, s] = await Promise.all([
            passService.getWaiver(),
            passService.getMySignatureStatus(),
        ])
        waiver.value = (w.data as any).data
        sigStatus.value = (s.data as any).data
    } catch (err: any) {
        // A 404 means this track has no active waiver configured: fall through to the
        // "No active waiver to sign" state. Any other failure is a real error we must
        // surface rather than silently showing "no waiver".
        if (err.response?.status === 404) {
            waiver.value = null
            sigStatus.value = null
        } else {
            loadError.value = err.response?.data?.error
                || 'Could not load the waiver. Refresh to try again, or check your connection.'
        }
    } finally {
        loading.value = false
    }
}

function currentPath(): string {
    return route.fullPath
}

function goBack() {
    const next = (route.query.next as string | undefined) || '/'
    router.push(next)
}

async function sign() {
    if (!signatureDataUrl.value) return
    try {
        signing.value = true
        await passService.signWaiver({
            signatureDataUrl: signatureDataUrl.value,
            parentName: riderIsMinor.value ? parentName.value.trim() : null,
            parentPhone: riderIsMinor.value ? parentPhone.value.trim() : null,
        })
        snackbarText.value = 'Waiver signed.'
        snackbarColor.value = 'success'
        snackbar.value = true
        // Brief pause so the user sees the toast, then go back to where they came from.
        setTimeout(goBack, 700)
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Failed to sign waiver.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        signing.value = false
    }
}

onMounted(load)
</script>

<style scoped>
.waiver-body {
    max-height: 50vh;
    overflow-y: auto;
    padding: 12px;
    border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
    border-radius: 4px;
    margin-bottom: 8px;
}
</style>
