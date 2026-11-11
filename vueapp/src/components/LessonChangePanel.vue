<template>
    <div>
        <div v-if="loading" class="text-center pa-6"><v-progress-circular indeterminate></v-progress-circular></div>
        <v-alert v-else-if="loadError" type="error" variant="tonal" density="compact">{{ loadError }}</v-alert>
        <template v-else-if="info">
            <div class="text-subtitle-1 font-weight-medium">{{ info.eventTitle }}</div>
            <div class="text-body-2 text-medium-emphasis mb-3">
                {{ info.tierName }}<template v-if="info.riderName"> · {{ info.riderName }}</template>
                · {{ formatTenantDateTime(info.sessionStartsAt, 'ddd, MMM D, h:mm A') }}
            </div>

            <v-alert v-if="errorText" type="error" variant="tonal" density="compact" class="mb-3">{{ errorText }}</v-alert>
            <v-alert v-if="done" type="success" variant="tonal" density="compact" class="mb-3">{{ done }}</v-alert>

            <v-alert v-if="info.weatherCancelled && info.status === 'paid'" type="warning" variant="tonal" density="compact" class="mb-3">
                This session was cancelled because of the weather. Move the booking to another date below, or take a refund.
            </v-alert>
            <v-alert v-else-if="!staff && !done && info.canRebook" type="info" variant="tonal" density="compact" class="mb-3">
                You can move this lesson to another date up to {{ info.cutoffHours }} hours before it starts.
                Lessons can't be moved on the day.
            </v-alert>
            <v-alert v-if="staff && info.hasRental" type="warning" variant="tonal" density="compact" class="mb-3">
                The buyer also rented a bike for this session. Moving the lesson doesn't move the rental: change it in
                the bike shop too.
            </v-alert>

            <p v-if="info.blockReason && !done" class="text-body-2 mb-3">{{ info.blockReason }}</p>

            <template v-if="info.canRebook">
                <div class="text-subtitle-2 mb-1">{{ staff ? 'Move to' : 'Pick a new date' }}</div>
                <v-radio-group v-model="picked" hide-details density="compact" class="mb-2">
                    <v-radio v-for="o in info.options" :key="o.tierId" :value="o.tierId">
                        <template #label>
                            <div>
                                <div>{{ formatTenantDateTime(o.startsAt, 'ddd, MMM D, h:mm A') }} · {{ o.eventTitle }}</div>
                                <div class="text-caption text-medium-emphasis">{{ o.tierName }}<template
                                        v-if="o.spotsLeft != null"> · {{ o.spotsLeft }} spot{{ o.spotsLeft === 1 ? '' : 's' }} left</template></div>
                            </div>
                        </template>
                    </v-radio>
                </v-radio-group>
                <v-btn color="primary" :disabled="!picked" :loading="saving" @click="rebook">Move to this date</v-btn>
            </template>

            <template v-if="info.canRefund">
                <v-divider class="my-4"></v-divider>
                <div class="text-subtitle-2 mb-1">Or take a refund</div>
                <p class="text-body-2 text-medium-emphasis mb-2">The full amount goes back to the card you paid with.</p>
                <v-btn variant="outlined" color="error" :loading="refunding" @click="refund">Refund {{ money(info) }}</v-btn>
            </template>
            <p v-else-if="info.refundNote && info.weatherCancelled" class="text-body-2 text-medium-emphasis mt-3">{{ info.refundNote }}</p>
        </template>
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { LessonChangeService, type LessonChangeInfo } from '@/services/LessonChangeService'
import { formatTenantDateTime } from '@/helpers/TenantTime'
import { useConfirm } from '@/composables/useConfirm'

// One lesson booking's date change (Script0319), for the rider (signed in, or by the emailed link's
// token) or for staff. The parent supplies the frame: a dialog, or the /Lesson page.
const props = defineProps<{ ticketId: string; token?: string | null; staff?: boolean }>()
const emit = defineEmits<{ (e: 'changed'): void }>()

const svc = new LessonChangeService()
const confirm = useConfirm()
const loading = ref(true)
const loadError = ref<string | null>(null)
const info = ref<LessonChangeInfo | null>(null)
const picked = ref<string | null>(null)
const saving = ref(false)
const refunding = ref(false)
const done = ref<string | null>(null)

const money = (i: LessonChangeInfo) => `$${(i.amountCents / 100).toFixed(2)}`
const errorText = ref<string | null>(null)

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const r = props.staff ? await svc.getForStaff(props.ticketId) : await svc.get(props.ticketId, props.token)
        info.value = r.data.data
        picked.value = null
    } catch (e: any) {
        loadError.value = e?.response?.data?.error || 'Could not load this booking.'
    } finally {
        loading.value = false
    }
}

onMounted(load)
watch(() => props.ticketId, () => { done.value = null; load() })

async function rebook() {
    if (!picked.value || !info.value) return
    const target = info.value.options.find(o => o.tierId === picked.value)
    saving.value = true
    errorText.value = null
    try {
        const r = props.staff
            ? await svc.rebookForStaff(props.ticketId, picked.value)
            : await svc.rebook(props.ticketId, picked.value, props.token)
        if (r.data.data) info.value = r.data.data
        picked.value = null
        done.value = target
            ? `Moved to ${formatTenantDateTime(target.startsAt, 'ddd, MMM D, h:mm A')}. The same ticket and QR code still work.`
            : 'Moved. The same ticket and QR code still work.'
        emit('changed')
    } catch (e: any) {
        done.value = null
        await load()
        alertError(e?.response?.data?.error || 'Could not move the booking.')
    } finally {
        saving.value = false
    }
}

async function refund() {
    if (!info.value) return
    const ok = await confirm({ title: 'Take a refund?', message: 'The booking is cancelled and the full amount goes back to your card.', confirmText: 'Refund' })
    if (!ok) return
    refunding.value = true
    errorText.value = null
    try {
        const r = await svc.weatherRefund(props.ticketId, props.token)
        done.value = `Refunded $${(r.data.data.refundCents / 100).toFixed(2)}. It can take a few days to reach your card.`
        await load()
        emit('changed')
    } catch (e: any) {
        alertError(e?.response?.data?.error || 'The refund didn\'t go through.')
    } finally {
        refunding.value = false
    }
}

function alertError(text: string) {
    errorText.value = text
}
</script>
