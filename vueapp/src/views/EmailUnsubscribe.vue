<template>
    <v-container class="py-12" style="max-width: 560px;">
        <v-card class="pa-6">
            <div v-if="loading" class="text-center py-8">
                <v-progress-circular indeterminate></v-progress-circular>
            </div>

            <template v-else-if="errorText">
                <h1 class="text-h5 mb-2">Unsubscribe link invalid</h1>
                <p class="text-body-2 text-medium-emphasis">{{ errorText }}</p>
            </template>

            <template v-else-if="status">
                <h1 class="text-h5 mb-2">
                    {{ status.tenantDisplayName ? `${status.tenantDisplayName} emails` : 'Marketing emails' }}
                </h1>
                <p class="text-body-2 mb-4"><strong>{{ status.email }}</strong></p>

                <v-alert v-if="actionError" type="error" variant="tonal" density="compact" class="mb-4">
                    {{ actionError }}
                </v-alert>

                <div v-if="!done">
                    <p class="mb-4">
                        Stop receiving promotional emails{{ status.tenantDisplayName ? ` from ${status.tenantDisplayName}` : '' }}?
                        You'll still get receipts and account emails.
                    </p>
                    <v-btn color="error" :loading="acting" @click="unsubscribe">Unsubscribe</v-btn>
                </div>

                <div v-else>
                    <v-alert type="success" variant="tonal" class="mb-4">
                        {{ allTracksDone
                            ? "Done. You won't receive promotional emails from any track on this platform."
                            : `Done. You won't receive promotional emails${status.tenantDisplayName ? ` from ${status.tenantDisplayName}` : ''}.` }}
                    </v-alert>

                    <div v-if="!allTracksDone" class="d-flex flex-column ga-4">
                        <div>
                            <v-btn variant="tonal" :loading="acting" @click="resubscribe">
                                Changed your mind? Resubscribe
                            </v-btn>
                        </div>
                        <div>
                            <p class="text-body-2 text-medium-emphasis mb-2">
                                Get promotional emails from other tracks on this platform too?
                            </p>
                            <v-btn variant="text" :loading="acting" @click="unsubscribeAll">
                                Stop emails from all tracks
                            </v-btn>
                        </div>
                    </div>
                </div>

                <p class="text-caption text-medium-emphasis mt-6">
                    This only affects promotional emails. Newsletter and event-reminder emails are
                    managed from their own unsubscribe links or your profile.
                </p>
            </template>
        </v-card>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { SuppressionService, type MarketingUnsubStatus } from '@/services/SuppressionService'

const route = useRoute()
const service = new SuppressionService()

const loading = ref(true)
const acting = ref(false)
// errorText is only for a bad/expired link (the load failed). A failed unsubscribe POST
// goes to actionError so we don't repaint a valid link as "invalid".
const errorText = ref('')
const actionError = ref('')
const status = ref<MarketingUnsubStatus | null>(null)
const done = ref(false)
const allTracksDone = ref(false)

const token = (route.query.token as string) || ''

onMounted(async () => {
    if (!token) {
        errorText.value = 'This link is missing its token.'
        loading.value = false
        return
    }
    try {
        const r = await service.status(token)
        status.value = (r.data as any).data
        // If they're already suppressed, show the confirmed state straight away.
        done.value = status.value!.unsubscribed
    } catch (err: any) {
        errorText.value = err.response?.data?.error || 'This link is no longer valid.'
    } finally {
        loading.value = false
    }
})

async function unsubscribe() {
    acting.value = true
    actionError.value = ''
    try {
        await service.unsubscribe(token)
        done.value = true
    } catch (err: any) {
        actionError.value = err.response?.data?.error || 'Could not unsubscribe you. Please try again, or contact the track.'
    } finally {
        acting.value = false
    }
}

async function resubscribe() {
    acting.value = true
    actionError.value = ''
    try {
        await service.resubscribe(token)
        done.value = false
    } catch (err: any) {
        actionError.value = err.response?.data?.error || 'Could not resubscribe you. Please try again, or contact the track.'
    } finally {
        acting.value = false
    }
}

async function unsubscribeAll() {
    acting.value = true
    actionError.value = ''
    try {
        await service.unsubscribeAllTracks(token)
        allTracksDone.value = true
    } catch (err: any) {
        actionError.value = err.response?.data?.error || 'Could not update your preferences. Please try again, or contact the track.'
    } finally {
        acting.value = false
    }
}
</script>
