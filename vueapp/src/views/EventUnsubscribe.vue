<template>
    <v-container class="py-12" style="max-width: 560px;">
        <v-card class="pa-6">
            <div v-if="loading" class="text-center py-8">
                <v-progress-circular indeterminate></v-progress-circular>
            </div>

            <template v-else-if="errorText">
                <h1 class="text-h5 mb-2">Link is no longer valid</h1>
                <p class="text-body-2 text-medium-emphasis">{{ errorText }}</p>
            </template>

            <template v-else-if="status">
                <h1 class="text-h5 mb-2">{{ status.tenantDisplayName }} event updates</h1>
                <p class="text-body-2 mb-4"><strong>{{ status.email }}</strong></p>

                <v-alert v-if="actionError" type="error" variant="tonal" density="compact" class="mb-3">
                    {{ actionError }}
                </v-alert>

                <div v-if="status.subscribed">
                    <p class="mb-4">Stop receiving event notifications from this track?</p>
                    <v-btn color="error" :loading="acting" @click="unsubscribe">Unsubscribe</v-btn>
                </div>
                <div v-else>
                    <v-alert type="success" variant="tonal" class="mb-3">
                        You've been unsubscribed. You won't receive any more event updates from this track.
                    </v-alert>
                    <v-btn variant="tonal" :loading="acting" @click="resubscribe">Changed your mind? Resubscribe</v-btn>
                </div>
            </template>
        </v-card>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { EventSubscriptionService, type EventSubscriptionStatus } from '@/services/EventSubscriptionService'

const route = useRoute()
const service = new EventSubscriptionService()

const loading = ref(true)
const acting = ref(false)
const errorText = ref('')
const actionError = ref('')
const status = ref<EventSubscriptionStatus | null>(null)
const token = route.params.token as string

onMounted(async () => {
    try {
        const r = await service.unsubscribeStatus(token)
        status.value = (r.data as any).data
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
        if (status.value) status.value.subscribed = false
    } catch (err: any) {
        actionError.value = err.response?.data?.error
            || 'Could not unsubscribe you. Please try again, or contact the track if it keeps happening.'
    } finally { acting.value = false }
}

async function resubscribe() {
    acting.value = true
    actionError.value = ''
    try {
        await service.resubscribe(token)
        if (status.value) status.value.subscribed = true
    } catch (err: any) {
        actionError.value = err.response?.data?.error
            || 'Could not resubscribe you. Please try again, or contact the track if it keeps happening.'
    } finally { acting.value = false }
}
</script>
