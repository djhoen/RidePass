<template>
    <!-- Send a waiver and / or photo step for one rider to a front-desk kiosk (Script0311), then follow it live:
         Waiting for kiosk / On screen / Done / Expired. The kiosk shows it only when idle. -->
    <v-dialog :model-value="modelValue" max-width="480" @update:model-value="close">
        <v-card>
            <v-card-title>Send to kiosk</v-card-title>
            <v-card-text>
                <template v-if="!push">
                    <p class="text-body-2 mb-3">
                        {{ riderName || 'This rider' }} completes it on a kiosk at the front desk; the gate updates when it's done.
                    </p>
                    <div v-if="loading" class="text-center py-3"><v-progress-circular indeterminate size="28"></v-progress-circular></div>
                    <div v-else-if="kiosks.length === 0" class="text-medium-emphasis text-body-2">
                        No kiosk is online right now (paired and seen in the last few minutes).
                    </div>
                    <v-radio-group v-else v-model="kioskId" hide-details density="compact">
                        <v-radio v-for="k in kiosks" :key="k.id" :value="k.id">
                            <template #label>
                                <span>{{ k.name }}</span>
                                <span class="text-caption text-medium-emphasis ml-2">
                                    {{ k.busy ? 'has a request waiting' : k.idle ? 'ready' : 'in use' }}
                                    · seen {{ ago(k.lastSeenAtUtc) }}
                                </span>
                            </template>
                        </v-radio>
                    </v-radio-group>
                    <div class="d-flex ga-4 mt-2">
                        <v-checkbox v-if="allowWaiver" v-model="wantWaiver" label="Waiver" hide-details density="compact"></v-checkbox>
                        <v-checkbox v-if="allowPhoto" v-model="wantPhoto" label="Photo" hide-details density="compact"></v-checkbox>
                    </div>
                    <v-alert v-if="busyPushId" type="warning" variant="tonal" density="compact" class="mt-2">
                        {{ error }}
                        <div class="mt-2">
                            <v-btn size="small" variant="tonal" :loading="sending" @click="cancelOtherAndSend">Cancel it and send this</v-btn>
                        </div>
                    </v-alert>
                    <v-alert v-else-if="error" type="error" variant="tonal" density="compact" class="mt-2">{{ error }}</v-alert>
                </template>
                <template v-else>
                    <div class="d-flex align-center ga-3">
                        <v-progress-circular v-if="push.status === 'pending' || push.status === 'in_progress' || push.status === 'completing'"
                            indeterminate size="24"></v-progress-circular>
                        <v-icon v-else-if="push.status === 'done'" color="success">mdi-check-circle</v-icon>
                        <v-icon v-else color="error">mdi-close-circle</v-icon>
                        <div>
                            <div class="text-subtitle-1">{{ statusText }}</div>
                            <div class="text-caption text-medium-emphasis">{{ push.kioskName }} · for {{ push.riderFirstName }}</div>
                        </div>
                    </div>
                    <v-alert v-if="error" type="error" variant="tonal" density="compact" class="mt-2">{{ error }}</v-alert>
                </template>
            </v-card-text>
            <v-card-actions>
                <v-btn v-if="push && (push.status === 'pending' || push.status === 'in_progress')" variant="text" color="error"
                    :loading="cancelling" @click="cancelPush">Cancel request</v-btn>
                <v-spacer></v-spacer>
                <v-btn variant="text" @click="close(false)">{{ push ? 'Close' : 'Cancel' }}</v-btn>
                <v-btn v-if="!push" color="primary" :loading="sending" :disabled="!kioskId || (!wantWaiver && !wantPhoto)"
                    @click="send">Send</v-btn>
            </v-card-actions>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref, computed, watch, onBeforeUnmount } from 'vue'
import { KioskPushService, kioskPushStatusText, type ActiveKiosk, type KioskPushStatus, type KioskPushTarget } from '@/services/KioskService'

const props = withDefaults(defineProps<{
    modelValue: boolean
    target: KioskPushTarget | null
    riderName?: string | null
    allowWaiver?: boolean
    allowPhoto?: boolean
}>(), { riderName: null, allowWaiver: true, allowPhoto: true })
const emit = defineEmits<{
    (e: 'update:modelValue', v: boolean): void
    /** Sent: the screen keeps following it (useKioskPushWatch) after this dialog closes. */
    (e: 'sent', pushId: string): void
    /** The kiosk finished while this dialog was open: refresh what the gate shows. */
    (e: 'done', pushId: string): void
}>()

const service = new KioskPushService()
const kiosks = ref<ActiveKiosk[]>([])
const loading = ref(false)
const kioskId = ref<string | null>(null)
const wantWaiver = ref(false)
const wantPhoto = ref(false)
const sending = ref(false)
const cancelling = ref(false)
const error = ref<string | null>(null)
const busyPushId = ref<string | null>(null)
const push = ref<KioskPushStatus | null>(null)
let timer: ReturnType<typeof setInterval> | null = null

const statusText = computed(() => push.value ? kioskPushStatusText(push.value) : '')

function ago(utc: string | null) {
    if (!utc) return 'never'
    const s = Math.max(0, Math.round((Date.now() - new Date(utc).getTime()) / 1000))
    return s < 60 ? 'just now' : `${Math.round(s / 60)} min ago`
}

function stop() { if (timer) { clearInterval(timer); timer = null } }

async function loadKiosks() {
    loading.value = true
    try {
        kiosks.value = (await service.activeKiosks()).data.data
        if (!kioskId.value || !kiosks.value.some(k => k.id === kioskId.value))
            kioskId.value = kiosks.value.find(k => !k.busy)?.id ?? kiosks.value[0]?.id ?? null
    } catch (err: any) {
        error.value = err.response?.data?.error || 'Couldn’t load kiosks.'
    } finally {
        loading.value = false
    }
}

watch(() => props.modelValue, open => {
    if (!open) { stop(); return }
    push.value = null
    error.value = null
    busyPushId.value = null
    wantWaiver.value = props.allowWaiver
    wantPhoto.value = !props.allowWaiver && props.allowPhoto
    void loadKiosks()
}, { immediate: true })

async function send() {
    if (!props.target || !kioskId.value) return
    sending.value = true
    error.value = null
    busyPushId.value = null
    try {
        push.value = (await service.send(kioskId.value, props.target, wantWaiver.value, wantPhoto.value)).data.data
        emit('sent', push.value.id)
        follow()
    } catch (err: any) {
        error.value = err.response?.data?.error || 'Couldn’t send it to the kiosk.'
        if (err.response?.status === 409) busyPushId.value = err.response?.data?.data?.activePushId ?? null
    } finally {
        sending.value = false
    }
}

async function cancelOtherAndSend() {
    if (!busyPushId.value) return
    try { await service.cancel(busyPushId.value) } catch { /* it may have just finished */ }
    await send()
}

function follow() {
    stop()
    timer = setInterval(async () => {
        if (!push.value) return
        try {
            push.value = (await service.status(push.value.id)).data.data
            if (['done', 'cancelled', 'expired'].includes(push.value.status)) {
                stop()
                if (push.value.status === 'done') emit('done', push.value.id)
            }
        } catch { /* try again on the next tick */ }
    }, 2000)
}

async function cancelPush() {
    if (!push.value) return
    cancelling.value = true
    try {
        await service.cancel(push.value.id)
        push.value = (await service.status(push.value.id)).data.data
        stop()
    } catch (err: any) {
        error.value = err.response?.data?.error || 'Couldn’t cancel it.'
    } finally {
        cancelling.value = false
    }
}

function close(v: boolean) {
    if (!v) { stop(); emit('update:modelValue', false) }
}

onBeforeUnmount(stop)
</script>
