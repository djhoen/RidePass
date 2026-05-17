<template>
    <v-container style="max-width: 720px">
        <h1 class="text-h4 mb-4">{{ pageTitle }}</h1>
        <BuyAdmissionFlow :event-id="eventId" :event="event" :kind-filter="kindFilter" />
    </v-container>
</template>

<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import BuyAdmissionFlow from '@/components/BuyAdmissionFlow.vue'
import { EventService, type EventDto } from '@/services/EventService'
import { branding } from '@/stores/branding'

// Route page just resolves params and delegates to the reusable flow component.
// The same component is mounted in a dialog from the home page for in-context purchase.
const route = useRoute()
const eventService = new EventService()
const eventId = route.params.eventId as string
const event = ref<EventDto | null>(null)
const kindFilter = computed<'spectator_pass' | 'race_entry' | null>(() => {
    const k = (route.query.kind as string | undefined)?.toLowerCase()
    return k === 'spectator_pass' || k === 'race_entry' ? k : null
})
const pageTitle = computed(() => {
    if (kindFilter.value === 'race_entry') return 'Buy Race Entry'
    if (kindFilter.value === 'spectator_pass') return 'Buy Ticket'
    return 'Buy Admission'
})

// Fetch the event so BuyAdmissionFlow can show eligibleExtras on the Add-ons step.
// The list endpoint is fine here — the per-event payload includes eligibleExtras.
onMounted(async () => {
    try {
        const tz = branding.timezone || 'UTC'
        const fromUtc = dayjs().tz(tz).startOf('day').subtract(7, 'day').utc().toISOString()
        const toUtc = dayjs().tz(tz).startOf('day').add(365, 'day').utc().toISOString()
        const r = await eventService.list(fromUtc, toUtc)
        const all = (r.data as any).data as EventDto[]
        event.value = all.find(e => e.id === eventId) ?? null
    } catch {
        event.value = null
    }
})
</script>
