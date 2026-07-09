<template>
    <!-- Shared horizontal carousel of upcoming-event cards for the chromeless embed
         widgets (Events list + Calendar). Each card opens the chromeless checkout at
         /embed/event/:id inside the same iframe. Card markup lives here so the two
         widgets stay visually identical.

         Sizing: the visible cards always divide the container width exactly (1-4 per
         view depending on iframe width), so the strip spans the full widget with no
         dead space on the right. Overflow scrolls with snap; the arrows overlay the
         edges instead of reserving columns, which would break the exact fill. -->
    <div class="rp-carousel-wrap">
        <div ref="scroller" class="rp-carousel" @scroll.passive="updateArrows">
            <router-link v-for="e in events" :key="e.id" :to="`/embed/event/${e.id}`" class="rp-slide">
                <v-card class="h-100 embed-event-card">
                    <div class="embed-event-image" :style="imageStyle(e)">
                        <div class="embed-event-datebadge">
                            <div class="embed-event-day">{{ formatDay(e.startsAtUtc) }}</div>
                            <div class="embed-event-month">{{ formatMonth(e.startsAtUtc) }}</div>
                        </div>
                    </div>
                    <v-card-text class="pa-3">
                        <div class="text-subtitle-1 font-weight-bold mb-1 embed-event-title">{{ e.title }}</div>
                        <div class="text-caption text-medium-emphasis d-flex align-center ga-1">
                            <v-icon icon="mdi-calendar-clock" size="14"></v-icon>
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
        </div>

        <v-btn v-if="canPrev" icon="mdi-chevron-left" size="small" class="rp-carousel-arrow rp-carousel-prev"
            aria-label="Previous events" @click="page(-1)"></v-btn>
        <v-btn v-if="canNext" icon="mdi-chevron-right" size="small" class="rp-carousel-arrow rp-carousel-next"
            aria-label="More events" @click="page(1)"></v-btn>
    </div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, onBeforeUnmount, nextTick } from 'vue'
import dayjs from 'dayjs'
import type { EventDto } from '@/services/EventService'
import { branding } from '@/stores/branding'

const props = defineProps<{ events: EventDto[] }>()

const scroller = ref<HTMLElement | null>(null)
const canPrev = ref(false)
const canNext = ref(false)

function updateArrows() {
    const el = scroller.value
    if (!el) { canPrev.value = canNext.value = false; return }
    canPrev.value = el.scrollLeft > 1
    canNext.value = el.scrollLeft + el.clientWidth < el.scrollWidth - 1
}
function page(dir: 1 | -1) {
    const el = scroller.value
    if (el) el.scrollBy({ left: dir * el.clientWidth, behavior: 'smooth' })
}

let resizeObserver: ResizeObserver | null = null
onMounted(() => {
    resizeObserver = new ResizeObserver(() => updateArrows())
    if (scroller.value) resizeObserver.observe(scroller.value)
    updateArrows()
})
onBeforeUnmount(() => { resizeObserver?.disconnect() })
watch(() => props.events, () => nextTick(updateArrows), { deep: false })

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
</script>

<style scoped>
.rp-carousel-wrap { position: relative; }
.rp-carousel {
    --rp-per-view: 4;
    --rp-gap: 12px;
    display: flex;
    gap: var(--rp-gap);
    overflow-x: auto;
    scroll-snap-type: x mandatory;
    /* Swipe/trackpad still scrolls; the visible scrollbar is just chrome the widget
       doesn't need (the overlay arrows + snap are the affordance). */
    scrollbar-width: none;
}
.rp-carousel::-webkit-scrollbar { display: none; }
/* Media queries inside the iframe track the iframe (= widget div) width. */
@media (max-width: 1099px) { .rp-carousel { --rp-per-view: 3; } }
@media (max-width: 799px)  { .rp-carousel { --rp-per-view: 2; } }
@media (max-width: 519px)  { .rp-carousel { --rp-per-view: 1; } }
.rp-slide {
    flex: 0 0 calc((100% - (var(--rp-per-view) - 1) * var(--rp-gap)) / var(--rp-per-view));
    scroll-snap-align: start;
    text-decoration: none;
    color: inherit;
    display: block;
}
.rp-carousel-arrow {
    position: absolute;
    top: 65px; /* image-band center, clear of title/text */
    transform: translateY(-50%);
    z-index: 1;
    opacity: 0.92;
}
.rp-carousel-prev { left: 4px; }
.rp-carousel-next { right: 4px; }

.embed-event-card { transition: transform 0.15s ease; }
.embed-event-card:hover { transform: translateY(-2px); }
.embed-event-title { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
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
