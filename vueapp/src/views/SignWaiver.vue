<template>
    <v-container class="sign-root py-6">
        <div v-if="loading" class="text-center py-12">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <v-alert v-else-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>

        <template v-else-if="done || info?.alreadySigned">
            <v-alert type="success" variant="tonal" class="mb-4">
                All set. Your waiver is signed, and there's no paperwork waiting for you when you arrive.
            </v-alert>
        </template>

        <template v-else-if="info">
            <h1 class="text-h5 font-weight-bold mb-1">Before your visit</h1>
            <p class="text-body-2 text-medium-emphasis mb-4">
                {{ info.recipientName ? `Hi ${info.recipientName}, ` : '' }}sign this now and you can
                skip the paperwork at the window.
            </p>

            <v-card variant="outlined" class="mb-4">
                <v-card-title class="text-subtitle-1">{{ info.waiverTitle }}</v-card-title>
                <v-card-text>
                    <div class="waiver-body" v-html="info.waiverBody"></div>
                </v-card-text>
            </v-card>

            <v-alert v-if="submitError" type="error" variant="tonal" class="mb-4">{{ submitError }}</v-alert>

            <v-card variant="outlined">
                <v-card-text>
                    <div class="d-flex ga-2 flex-wrap">
                        <v-text-field v-model="firstName" label="First name" density="compact" style="min-width: 180px" />
                        <v-text-field v-model="lastName" label="Last name" density="compact" style="min-width: 180px" />
                    </div>
                    <v-text-field v-model="birthdate" label="Birthdate" type="date" density="compact" class="mt-4"
                        hint="Needed so we know whether a parent or guardian must sign" persistent-hint />
                    <template v-if="isMinor">
                        <v-alert type="info" variant="tonal" density="compact" class="mt-4">
                            Riders under 18 need a parent or guardian to sign on their behalf.
                        </v-alert>
                        <v-text-field v-model="parentName" label="Parent / guardian name" density="compact" class="mt-4" />
                        <v-text-field v-model="parentPhone" label="Parent / guardian phone" density="compact" class="mt-4" />
                    </template>
                    <p class="text-body-2 mt-4 mb-2">
                        {{ isMinor ? 'Parent or guardian signature:' : 'Your signature:' }}
                    </p>
                    <SignaturePad v-model="signature" :disabled="saving" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn color="primary" size="large" :loading="saving" :disabled="!canSubmit"
                        @click="submit">Sign waiver</v-btn>
                </v-card-actions>
            </v-card>
        </template>
    </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import SignaturePad from '@/components/SignaturePad.vue'
import { WaiverService, type PublicSignRequestInfo } from '@/services/WaiverService'

const route = useRoute()
const service = new WaiverService()
const token = String(route.params.token ?? '')

const info = ref<PublicSignRequestInfo | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)
const done = ref(false)

const firstName = ref('')
const lastName = ref('')
const birthdate = ref('')
const parentName = ref('')
const parentPhone = ref('')
const signature = ref('')
const saving = ref(false)
const submitError = ref<string | null>(null)

const isMinor = computed(() =>
    !!birthdate.value && dayjs().diff(dayjs(birthdate.value), 'year') < 18)

const canSubmit = computed(() =>
    firstName.value.trim().length > 0
    && lastName.value.trim().length > 0
    && signature.value.length > 0
    && (!isMinor.value || (parentName.value.trim().length > 0 && parentPhone.value.trim().length >= 7)))

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const res = await service.getSignRequestByToken(token)
        info.value = res.data.data
        // Prefill the name fields from the request when we have one.
        if (info.value.recipientName && !firstName.value) {
            const parts = info.value.recipientName.trim().split(/\s+/)
            firstName.value = parts[0] ?? ''
            lastName.value = parts.slice(1).join(' ')
        }
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            ?? 'This signing link could not be loaded. It may have expired; ask the venue to send a new one.'
    } finally {
        loading.value = false
    }
}

async function submit() {
    saving.value = true
    submitError.value = null
    try {
        await service.signByToken(token, {
            firstName: firstName.value.trim(),
            lastName: lastName.value.trim(),
            birthdate: birthdate.value || null,
            signatureDataUrl: signature.value,
            parentName: isMinor.value ? parentName.value.trim() : null,
            parentPhone: isMinor.value ? parentPhone.value.trim() : null,
        })
        done.value = true
    } catch (err: any) {
        submitError.value = err.response?.data?.error
            ?? 'Your signature could not be saved. Check your connection and tap Sign waiver again.'
    } finally {
        saving.value = false
    }
}

onMounted(load)
</script>

<style scoped>
.sign-root { max-width: 720px; }
.waiver-body {
    max-height: 300px;
    overflow-y: auto;
    font-size: 14px;
    line-height: 1.5;
}
</style>
