<template>
    <!-- Chromeless "Calendar + events" widget: a carousel of upcoming events above a
         month calendar. Framed on a track's own site via embed.js. Any event opens the
         (also chromeless) checkout at /embed/event/:id inside the same iframe. -->
    <div class="embed-calendar pa-3">
        <div v-if="loading" class="text-center py-8">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <template v-else>
            <!-- Upcoming carousel -->
            <div v-if="upcoming.length" class="mb-5">
                <div class="text-subtitle-1 font-weight-bold mb-2">Upcoming events</div>
                <v-slide-group show-arrows>
                    <v-slide-group-item v-for="e in upcoming" :key="e.id">
                        <v-card class="embed-cal-card mr-3" @click="openEvent(e)">
                            <div class="embed-cal-image" :style="imageStyle(e)">
                                <div class="embed-cal-datebadge">
                                    <div class="embed-cal-day">{{ formatDay(e.startsAtUtc) }}</div>
                                    <div class="embed-cal-month">{{ formatMonth(e.startsAtUtc) }}</div>
                                </div>
                            </div>
                            <v-card-text class="pa-3">
                                <div class="text-subtitle-2 font-weight-bold embed-cal-title">{{ e.title }}</div>
                                <div class="text-caption text-medium-emphasis d-flex align-center ga-1 mt-1">
                                    <v-icon icon="mdi-calendar-clock" size="13"></v-icon>
                                    <span>{{ formatWhen(e.startsAtUtc) }}</span>
                                </div>
                                <v-chip size="x-small" variant="tonal" class="mt-2"
                                    :style="{ backgroundColor: e.eventTypeColor + '22', color: e.eventTypeColor }">
                                    {{ e.eventTypeName }}
                                </v-chip>
                            </v-card-text>
                        </v-card>
                    </v-slide-group-item>
                </v-slide-group>
            </div>

            <!-- Month calendar -->
            <EventCalendar :month-start="monthStart" :events="events" :timezone="tz"
                @update:month-start="monthStart = $event" @select="openEvent" />

            <div v-if="events.length === 0" class="text-center text-medium-emphasis py-4">
                No upcoming events.
            </div>
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

const route = useRoute()
const router = useRouter()
const eventService = new EventService()

const events = ref<EventDto[]>([])
const loading = ref(true)
const monthStart = ref(dayjs().startOf('month').format('YYYY-MM-DD'))

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
    const nowIso = dayjs().utc().toISOString()
    const list = events.value
        .filter(e => e.startsAtUtc >= nowIso)
        .sort((a, b) => a.startsAtUtc.localeCompare(b.startsAtUtc))
    return limit ? list.slice(0, limit) : list
})

const apiUrl: string = (import.meta as any).env?.VITE_API_ENDPOINT ?? ''
function absoluteUrl(url: string | null | undefined): string | null {
    if (!url) return null
    if (/^https?:\/\//i.test(url)) return url
    try { return `${new URL(apiUrl, window.location.origin).origin}${url}` } catch { return url }
}
function imageStyle(e: EventDto) {
    const u = absoluteUrl(e.imageUrl ?? e.eventTypeImageUrl)
    return u ? { backgroundImage: `url(${u})` } : { backgroundColor: e.eventTypeColor || '#1976D2' }
}
function formatDay(utc: string): string { return dayjs.utc(utc).tz(tz.value).format('D') }
function formatMonth(utc: string): string { return dayjs.utc(utc).tz(tz.value).format('MMM').toUpperCase() }
function formatWhen(utc: string): string { return dayjs.utc(utc).tz(tz.value).format('ddd, MMM D · h:mm A') }

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
    } catch (err) {
        console.error('Failed to load embed calendar', err)
    } finally {
        loading.value = false
    }
})
</script>

<style scoped>
.embed-cal-card { width: 220px; cursor: pointer; transition: transform 0.15s ease; }
.embed-cal-card:hover { transform: translateY(-2px); }
.embed-cal-image {
    position: relative;
    height: 110px;
    background-size: cover;
    background-position: center;
    border-top-left-radius: inherit;
    border-top-right-radius: inherit;
}
.embed-cal-datebadge {
    position: absolute;
    top: -6px;
    left: 12px;
    background: #000;
    color: #fff;
    padding: 6px 10px 5px;
    text-align: center;
    border-radius: 4px;
    line-height: 1;
    min-width: 44px;
    box-shadow: 1px 2px 5px 0 rgba(0, 0, 0, 0.34);
}
.embed-cal-day { font-size: 1.4rem; font-weight: 700; line-height: 1; }
.embed-cal-month { font-size: 0.65rem; letter-spacing: 0.08em; opacity: 0.85; }
.embed-cal-title { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
</style>
