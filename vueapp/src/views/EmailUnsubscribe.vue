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

                    <div v-if="!allTracksDone">
                        <p class="text-body-2 text-medium-emphasis mb-2">
                            Get promotional emails from other tracks on this platform too?
                        </p>
                        <v-btn variant="tonal" :loading="acting" @click="unsubscribeAll">
                            Stop emails from all tracks
                        </v-btn>
                    </div>
                </div>
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
const errorText = ref('')
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
    try {
        await service.unsubscribe(token)
        done.value = true
    } catch (err: any) {
        errorText.value = err.response?.data?.error || 'Something went wrong.'
    } finally {
        acting.value = false
    }
}

async function unsubscribeAll() {
    acting.value = true
    try {
        await service.unsubscribeAllTracks(token)
        allTracksDone.value = true
    } catch (err: any) {
        errorText.value = err.response?.data?.error || 'Something went wrong.'
    } finally {
        acting.value = false
    }
}
</script>
