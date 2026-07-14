<template>
    <!-- Chromeless "Calendar + events" widget: a carousel of upcoming events above a
         month calendar. Framed on a track's own site via embed.js. Any event opens the
         (also chromeless) checkout at /embed/event/:id inside the same iframe. -->
    <div class="embed-calendar pa-3">
        <div v-if="loading" class="text-center py-8">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <template v-else>
            <div v-if="loadError" class="text-center text-error py-8">{{ loadError }}</div>

            <template v-else>
            <!-- Upcoming carousel (shared card design with the events widget) -->
            <div v-if="upcoming.length" class="mb-5">
                <div class="text-subtitle-1 font-weight-bold mb-2">Upcoming events</div>
                <EmbedEventCarousel :events="upcoming" />
            </div>

            <!-- Month calendar -->
            <EventCalendar :month-start="monthStart" :events="events" :timezone="tz"
                :min-month="windowMinMonth" :max-month="windowMaxMonth"
                @update:month-start="monthStart = $event" @select="openEvent" />

            <div v-if="events.length === 0" class="text-center text-medium-emphasis py-4">
                No upcoming events.
            </div>
            </template>
        </template>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { EventService, type EventDto } from '@/services/EventService'
import { branding } from '@/stores/branding'
import EventCalendar from '@/components/EventCalendar.vue'
import EmbedEventCarousel from '@/components/EmbedEventCarousel.vue'

const route = useRoute()
const router = useRouter()
const eventService = new EventService()

const events = ref<EventDto[]>([])
const loading = ref(true)
const loadError = ref('')
const monthStart = ref(dayjs().startOf('month').format('YYYY-MM-DD'))
// Bound calendar navigation to the window we actually fetch below, so the visitor can't
// page into an empty grid outside the loaded range.
const windowMinMonth = dayjs().startOf('month').format('YYYY-MM-DD')
const windowMaxMonth = dayjs().add(12, 'month').startOf('month').format('YYYY-MM-DD')

// Optional widget config (mirrors the events widget):
//   data-limit="10"        -> cap the carousel
//   data-event-type="race" -> show only one event type (by code)
const limit = (() => {
    const n = parseInt(String(route.query.limit ?? ''), 10)
    return Number.isFinite(n) && n > 0 ? n : null
})()
const typeCode = (() => {
    const t = String(route.query.type ?? '').trim().toLowerCase()
    return t || null
})()

const tz = computed(() => branding.timezone || 'UTC')

// Carousel = future events only, soonest first, capped by the optional limit. The
// calendar gets the full set so the month grid is complete.
const upcoming = computed(() => {
    // An event stays in the strip until the day after it ends, so a race running right now is still
    // shown to someone deciding whether to head out.
    const floorIso = dayjs().startOf('day').utc().toISOString()
    const list = events.value
        .filter(e => (e.endsAtUtc ?? e.startsAtUtc) >= floorIso)
        .sort((a, b) => a.startsAtUtc.localeCompare(b.startsAtUtc))
    return limit ? list.slice(0, limit) : list
})

function openEvent(e: EventDto) { router.push(`/embed/event/${e.id}`) }

onMounted(async () => {
    try {
        // One generous window covers the carousel (today forward) and ~a year of
        // calendar navigation without refetching.
        const from = dayjs().startOf('month').utc().toISOString()
        const to = dayjs().add(12, 'month').endOf('month').utc().toISOString()
        const r = await eventService.list(from, to)
        let list = ((r.data as any).data as EventDto[]).filter(e => e.status === 'scheduled')
        if (typeCode) list = list.filter(e => (e.eventTypeCode || '').toLowerCase() === typeCode)
        events.value = list
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            || 'Could not load events. Refresh the page to try again.'
    } finally {
        loading.value = false
    }
})
</script>

