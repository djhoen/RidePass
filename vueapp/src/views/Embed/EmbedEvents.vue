<template>
    <!-- Chromeless events widget: framed on a track's own website via embed.js.
         Lists upcoming events; each opens the (also chromeless) checkout at
         /embed/event/:id inside the same iframe. -->
    <div class="embed-events pa-3">
        <div v-if="loading" class="text-center py-8">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <template v-else>
            <v-row v-if="events.length > 0" dense>
                <v-col v-for="e in events" :key="e.id" cols="12" sm="6">
                    <router-link :to="`/embed/event/${e.id}`" class="embed-event-link">
                        <v-card class="h-100 embed-event-card">
                            <div class="embed-event-image" :style="imageStyle(e)">
                                <div class="embed-event-datebadge">
                                    <div class="embed-event-day">{{ formatDay(e.startsAtUtc) }}</div>
                                    <div class="embed-event-month">{{ formatMonth(e.startsAtUtc) }}</div>
                                </div>
                            </div>
                            <v-card-text class="pa-3">
                                <div class="text-subtitle-1 font-weight-bold mb-1">{{ e.title }}</div>
                                <div class="text-caption text-medium-emphasis d-flex align-center ga-1">
                                    <v-icon :icon="'mdi-calendar-clock'" size="14"></v-icon>
                                    <span>{{ formatWhen(e.startsAtUtc) }}</span>
                                </div>
                                <div v-if="e.locationLabel" class="text-caption text-medium-emphasis d-flex align-center ga-1 mt-1">
                                    <v-icon icon="mdi-map-marker" size="14"></v-icon>
                                    <span>{{ e.locationLabel }}</span>
                                </div>
                                <v-chip size="x-small" variant="tonal" class="mt-2"
                                    :style="{ backgroundColor: e.eventTypeColor + '22', color: e.eventTypeColor }">
                                    {{ e.eventTypeName }}
                                </v-chip>
                            </v-card-text>
                        </v-card>
                    </router-link>
                </v-col>
            </v-row>
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
import { branding } from '@/stores/branding'

const route = useRoute()
const eventService = new EventService()
const events = ref<EventDto[]>([])
const loading = ref(true)

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

function tz(): string { return branding.timezone || 'UTC' }
function formatDay(utc: string): string { return dayjs.utc(utc).tz(tz()).format('D') }
function formatMonth(utc: string): string { return dayjs.utc(utc).tz(tz()).format('MMM').toUpperCase() }
function formatWhen(utc: string): string { return dayjs.utc(utc).tz(tz()).format('ddd, MMM D · h:mm A') }

onMounted(async () => {
    try {
        const from = dayjs().utc().toISOString()
        const to = dayjs().add(120, 'day').utc().toISOString()
        const r = await eventService.list(from, to)
        let list = ((r.data as any).data as EventDto[]).filter(e => e.status === 'scheduled')
        if (typeCode) list = list.filter(e => (e.eventTypeCode || '').toLowerCase() === typeCode)
        if (limit) list = list.slice(0, limit)
        events.value = list
    } catch (err) {
        console.error('Failed to load embed events', err)
    } finally {
        loading.value = false
    }
})
</script>

<style scoped>
.embed-event-link { text-decoration: none; color: inherit; display: block; height: 100%; }
.embed-event-card { transition: transform 0.15s ease; }
.embed-event-card:hover { transform: translateY(-2px); }
.embed-event-image {
    position: relative;
    height: 130px;
    background-size: cover;
    background-position: center;
    border-top-left-radius: inherit;
    border-top-right-radius: inherit;
}
.embed-event-datebadge {
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
.embed-event-day { font-size: 1.4rem; font-weight: 700; line-height: 1; }
.embed-event-month { font-size: 0.65rem; letter-spacing: 0.08em; opacity: 0.85; }
</style>
