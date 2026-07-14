<template>
    <!-- Chromeless events widget: framed on a track's own website via embed.js.
         A horizontal carousel of upcoming events (shared with the calendar widget's
         strip); each card opens the (also chromeless) checkout at /embed/event/:id
         inside the same iframe. -->
    <div class="embed-events pa-3">
        <div v-if="loading" class="text-center py-8">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <template v-else>
            <div v-if="loadError" class="text-center text-error py-8">{{ loadError }}</div>

            <EmbedEventCarousel v-else-if="events.length > 0" :events="events" />
            <div v-else class="text-center text-medium-emphasis py-8">
                No upcoming events.
            </div>
        </template>
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import { EventService, type EventDto } from '@/services/EventService'
import EmbedEventCarousel from '@/components/EmbedEventCarousel.vue'

const route = useRoute()
const eventService = new EventService()
const events = ref<EventDto[]>([])
const loading = ref(true)
const loadError = ref('')

// Optional widget config passed by embed.js as query params:
//   data-limit="6"        -> cap the number of events shown
//   data-event-type="race" -> show only one event type (by code)
const limit = (() => {
    const n = parseInt(String(route.query.limit ?? ''), 10)
    return Number.isFinite(n) && n > 0 ? n : null
})()
const typeCode = (() => {
    const t = String(route.query.type ?? '').trim().toLowerCase()
    return t || null
})()

onMounted(async () => {
    try {
        const from = dayjs().startOf('day').utc().toISOString()
        const to = dayjs().add(120, 'day').utc().toISOString()
        const r = await eventService.list(from, to)
        let list = ((r.data as any).data as EventDto[]).filter(e => e.status === 'scheduled')
        if (typeCode) list = list.filter(e => (e.eventTypeCode || '').toLowerCase() === typeCode)
        if (limit) list = list.slice(0, limit)
        events.value = list
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            || 'Could not load events. Refresh the page to try again.'
    } finally {
        loading.value = false
    }
})
</script>
