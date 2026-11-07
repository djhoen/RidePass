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
            <h1 class="text-h4 font-display mb-1">Before your visit</h1>
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
                    <!-- A link for a specific rider: the server already knows who it is for and how it must be
                         signed, so the page says so up front and asks for no birthdate. -->
                    <v-alert v-if="info.signAs === 'guardian'" type="info" variant="tonal" density="compact" class="mb-4">
                        <template v-if="riderName">You're signing for {{ riderName }} as their parent or guardian.</template>
                        <template v-else>You're signing for the rider as their parent or guardian. Enter the rider's name below.</template>
                    </v-alert>
                    <v-alert v-else-if="info.signAs === 'adult'" type="info" variant="tonal" density="compact" class="mb-4">
                        {{ riderName ? `Sign as yourself, ${riderName}.` : 'Sign as yourself.' }}
                    </v-alert>
                    <div v-if="showNameFields" class="d-flex ga-2 flex-wrap">
                        <v-text-field v-model="firstName" :label="info.signAs === 'guardian' ? 'Rider\'s first name' : 'First name'"
                            density="compact" style="min-width: 180px" />
                        <v-text-field v-model="lastName" :label="info.signAs === 'guardian' ? 'Rider\'s last name' : 'Last name'"
                            density="compact" style="min-width: 180px" />
                    </div>
                    <v-text-field v-if="!info.signAs" v-model="birthdate" label="Birthdate" type="date" density="compact" class="mt-4"
                        hint="Needed so we know whether a parent or guardian must sign" persistent-hint />
                    <template v-if="isMinor">
                        <v-alert v-if="!info.signAs" type="info" variant="tonal" density="compact" class="mt-4">
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

// The rider's names as the server holds them; blank counts as unknown (the server's test too).
const riderFirst = computed(() => info.value?.riderFirstName?.trim() || null)
const riderLast = computed(() => info.value?.riderLastName?.trim() || null)
const riderName = computed(() => riderFirst.value && riderLast.value ? `${riderFirst.value} ${riderLast.value}` : null)
// Name fields: always for an untargeted link; for a targeted one only when the server lacks the name.
const showNameFields = computed(() => !info.value?.signAs || !riderFirst.value || !riderLast.value)
// A targeted link: the server's mode. Otherwise from the birthdate typed in.
const isMinor = computed(() => info.value?.signAs
    ? info.value.signAs === 'guardian'
    : !!birthdate.value && dayjs().diff(dayjs(birthdate.value), 'year') < 18)

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
        // The rider the link is for: the name the signature is recorded under (the server uses it anyway).
        if (info.value.signAs) {
            // Targeted: the rider's own name (never the recipient's: for a guardian link that's the parent).
            firstName.value = info.value.riderFirstName?.trim() ?? ''
            lastName.value = info.value.riderLastName?.trim() ?? ''
            // A Gate Sale guardian link sent to a known account: their name and phone are already on file.
            if (info.value.signAs === 'guardian') {
                if (!parentName.value) parentName.value = info.value.guardianName?.trim() ?? ''
                if (!parentPhone.value) parentPhone.value = info.value.guardianPhone?.trim() ?? ''
            }
        } else if (info.value.recipientName && !firstName.value) {
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
