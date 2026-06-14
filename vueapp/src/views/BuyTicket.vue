<template>
    <v-container style="max-width: 720px">
        <h1 class="text-h4 mb-4">{{ pageTitle }}</h1>
        <BuyAdmissionFlow :event-id="eventId" :event="event" :kind-filter="kindFilter" />
    </v-container>
</template>

<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import BuyAdmissionFlow from '@/components/BuyAdmissionFlow.vue'
import { EventService, type EventDto } from '@/services/EventService'

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
// Use the public single-event endpoint so this works for signed-out racers too
// (they create their account inline). The payload includes eligibleExtras/passes.
onMounted(async () => {
    try {
        const r = await eventService.getPublic(eventId)
        event.value = (r.data as any).data as EventDto
    } catch {
        event.value = null
    }
})
</script>
