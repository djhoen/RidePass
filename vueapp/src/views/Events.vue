<template>
    <div class="events-page">
        <!-- ── HERO BAND ────────────────────────────────────────────────────
             Title + subtitle over a darkened motocross photo. Apex uses the
             platform hero image; a tenant uses its own hero. Mirrors the mock's
             "EVENTS CALENDAR" band. -->
        <section class="events-hero" :style="heroStyle">
            <div class="events-hero-overlay">
                <v-container>
                    <h1 class="text-h3 font-weight-bold text-white font-display mb-2">Events Calendar</h1>
                    <p class="text-h6 text-white mb-0" style="max-width: 620px; opacity: 0.92">
                        {{ heroSubtitle }}
                    </p>
                </v-container>
            </div>
        </section>

        <v-container>
            <!-- ── OUT-OF-COUNTRY NOTICE ───────────────────────────────────
                 Shown when the visitor's IP resolves outside the US. We default
                 to race events only and let them enter a zip to see what's near
                 a location they care about. -->
            <v-alert v-if="outOfCountry" type="info" variant="tonal" class="my-4" icon="mdi-map-marker-radius">
                <div class="d-flex flex-wrap align-center ga-3">
                    <span class="flex-grow-1">
                        Looks like you're outside the US. Showing race events. Enter a US zip code to
                        find events near a specific location.
                    </span>
                    <div class="d-flex ga-2" style="min-width: 260px">
                        <v-text-field v-model="zipInput" label="US zip code" density="compact" hide-details
                            style="max-width: 160px" @keyup.enter="applyZip"></v-text-field>
                        <v-btn color="primary" :loading="geolocating" @click="applyZip">Go</v-btn>
                    </div>
                </div>
            </v-alert>

            <!-- ── UPCOMING EVENTS CAROUSEL ────────────────────────────────
                 Same horizontal scroll-snap carousel as the home page, bound to
                 the currently-filtered upcoming events. -->
            <section v-if="upcomingEvents.length > 0" class="mt-8 mb-10">
                <div class="d-flex align-center mb-4 ga-2">
                    <h2 class="text-h5 font-weight-bold font-display">Upcoming Events</h2>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" color="primary" append-icon="mdi-arrow-right" @click="viewMode = 'list'">
                        View All Events
                    </v-btn>
                </div>
                <div class="carousel-wrap">
                    <button v-show="canScrollLeft" type="button" class="carousel-arrow carousel-arrow--left"
                        aria-label="Previous events" @click="scrollCarousel(-1)">
                        <v-icon size="32">mdi-chevron-left</v-icon>
                    </button>
                    <button v-show="canScrollRight" type="button" class="carousel-arrow carousel-arrow--right"
                        aria-label="Next events" @click="scrollCarousel(1)">
                        <v-icon size="32">mdi-chevron-right</v-icon>
                    </button>
                    <div ref="carouselTrack" class="carousel-track" @scroll.passive="updateScrollState">
                        <div v-for="e in upcomingEvents" :key="e.id" class="carousel-card">
                            <component :is="linkTag(e)" v-bind="linkProps(e)" class="event-card-link">
                                <v-card class="h-100 event-card">
                                    <div class="event-image" :style="eventImageStyle(e)">
                                        <div class="event-datebadge">
                                            <div class="event-day">{{ formatDay(e.startsAtUtc) }}</div>
                                            <div class="event-month">{{ formatMonth(e.startsAtUtc) }}</div>
                                        </div>
                                    </div>
                                    <v-card-text class="pa-3">
                                        <div class="text-subtitle-1 font-weight-bold mb-1 text-truncate">{{ e.title }}</div>
                                        <!-- Apex shows the track + location (useful across tracks). The event
                                             type shows on every card, replacing the old colored pill. -->
                                        <div v-if="isApex" class="text-caption text-medium-emphasis d-flex align-center ga-1">
                                            <v-icon icon="mdi-map-marker" size="14"></v-icon>
                                            <span class="text-truncate">{{ locationText(e) }}</span>
                                        </div>
                                        <div class="text-caption text-medium-emphasis d-flex align-center ga-1">
                                            <v-icon :icon="eventTypeIcon(e.eventTypeCode)" size="18"></v-icon>
                                            <span class="text-truncate">{{ e.eventTypeName }}</span>
                                        </div>
                                        <div v-if="e.distanceMiles !== null" class="text-caption text-primary mt-1">
                                            {{ Math.round(e.distanceMiles) }} mi away
                                        </div>
                                    </v-card-text>
                                </v-card>
                            </component>
                        </div>
                    </div>
                </div>
            </section>

            <!-- ── TOOLBAR: month nav + view toggle + filters ─────────────── -->
            <div class="d-flex align-center flex-wrap ga-2 mb-4">
                <template v-if="viewMode === 'month'">
                    <v-btn icon="mdi-chevron-left" variant="text" @click="prevMonth"></v-btn>
                    <h2 class="text-h6 mx-1" style="min-width: 170px; text-align: center">{{ monthLabel }}</h2>
                    <v-btn icon="mdi-chevron-right" variant="text" @click="nextMonth"></v-btn>
                    <v-btn variant="text" size="small" @click="goToday">Today</v-btn>
                </template>
                <h2 v-else class="text-h6">All Upcoming Events</h2>

                <v-spacer></v-spacer>
                <v-progress-circular v-if="loading" indeterminate size="20" width="2" class="mr-2"></v-progress-circular>

                <v-btn-toggle v-model="viewMode" density="comfortable" mandatory variant="outlined" divided>
                    <v-btn value="month" prepend-icon="mdi-calendar-month">Month</v-btn>
                    <v-btn value="list" prepend-icon="mdi-format-list-bulleted">List</v-btn>
                </v-btn-toggle>

                <v-btn color="primary" prepend-icon="mdi-filter-variant" @click="openFilters">
                    Filters
                    <v-badge v-if="activeFilterCount > 0" color="error" :content="activeFilterCount" inline></v-badge>
                </v-btn>
            </div>

            <!-- ── MONTH VIEW: calendar grid ───────────────────────────────
                 Grid markup + styling adapted from the per-tenant Calendar view.
                 Each event chip links out to that event. -->
            <v-card v-show="viewMode === 'month'" class="mb-6">
                <div class="calendar-grid">
                    <div v-for="d in weekdayLabels" :key="d" class="weekday-header">{{ d }}</div>
                    <div v-for="(day, i) in monthDays" :key="i" class="day-cell"
                        :class="{ 'other-month': !day.inMonth, 'is-today': day.isToday }">
                        <div class="day-top-row">
                            <span class="day-num">{{ day.dayNumber }}</span>
                        </div>
                        <component :is="linkTag(ev)" v-for="ev in day.events.slice(0, 3)" :key="ev.id"
                            v-bind="linkProps(ev)" class="event-chip-link">
                            <div class="event-chip" :style="{ background: ev.eventTypeColor || '#1976D2' }" :title="ev.title">
                                {{ ev.title }}
                            </div>
                        </component>
                        <div v-if="day.events.length > 3" class="text-caption text-medium-emphasis">
                            +{{ day.events.length - 3 }} more
                        </div>
                    </div>
                </div>
            </v-card>

            <!-- ── LIST VIEW: all upcoming events ──────────────────────────
                 Date badge, thumbnail, title, location, type chip, and a
                 View Details link per the mock. -->
            <div v-show="viewMode === 'list'" class="mb-6">
                <v-card v-for="e in upcomingEvents" :key="e.id" class="mb-3 list-row">
                    <div class="d-flex align-center pa-3 ga-3 flex-wrap">
                        <div class="list-datebadge text-center flex-shrink-0">
                            <div class="text-caption font-weight-bold text-primary">{{ formatMonth(e.startsAtUtc) }}</div>
                            <div class="text-h5 font-weight-bold">{{ formatDay(e.startsAtUtc) }}</div>
                        </div>
                        <div class="list-thumb flex-shrink-0" :style="eventImageStyle(e)"></div>
                        <div class="flex-grow-1" style="min-width: 200px">
                            <div class="text-subtitle-1 font-weight-bold">{{ e.title }}</div>
                            <div class="text-body-2 text-medium-emphasis d-flex align-center ga-1">
                                <v-icon icon="mdi-map-marker" size="14"></v-icon>
                                <span>{{ locationText(e) }}</span>
                                <span v-if="e.distanceMiles !== null" class="text-primary ml-1">
                                    · {{ Math.round(e.distanceMiles) }} mi
                                </span>
                            </div>
                            <div class="text-caption mt-1 d-flex align-center ga-1">
                                <v-icon icon="mdi-clock-outline" size="14"></v-icon>
                                {{ formatWhen(e.startsAtUtc) }}
                            </div>
                        </div>
                        <v-chip size="small" :style="{ backgroundColor: e.eventTypeColor, color: '#fff' }" class="flex-shrink-0">
                            {{ e.eventTypeName }}
                        </v-chip>
                        <component :is="linkTag(e)" v-bind="linkProps(e)" class="flex-shrink-0">
                            <v-btn color="primary" variant="tonal" size="small" append-icon="mdi-arrow-right">View Details</v-btn>
                        </component>
                    </div>
                </v-card>
                <v-card v-if="!loading && upcomingEvents.length === 0" variant="outlined">
                    <v-card-text class="text-center text-medium-emphasis py-12">
                        No upcoming events match your filters.
                    </v-card-text>
                </v-card>
            </div>
        </v-container>

        <!-- ── FILTERS MODAL ───────────────────────────────────────────────
             Event types (always), tracks + mile radius (apex only). -->
        <v-dialog v-model="filtersOpen" :max-width="520" scrollable>
            <v-card>
                <v-card-title class="d-flex align-center">
                    Filters
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="filtersOpen = false"></v-btn>
                </v-card-title>
                <v-divider></v-divider>
                <v-card-text>
                    <div class="text-subtitle-2 mb-2">Event types</div>
                    <v-checkbox v-for="opt in typeOptions" :key="opt.code" v-model="draftTypeCodes" :value="opt.code"
                        density="compact" hide-details>
                        <template #label>
                            <span class="d-inline-flex align-center ga-2">
                                <span class="type-swatch" :style="{ backgroundColor: opt.color }"></span>
                                {{ opt.name }}
                            </span>
                        </template>
                    </v-checkbox>
                    <p class="text-caption text-medium-emphasis mt-1">Leave all unchecked to show every type.</p>

                    <template v-if="isApex">
                        <v-divider class="my-4"></v-divider>
                        <div class="text-subtitle-2 mb-2">Tracks</div>
                        <v-autocomplete v-model="draftTenantIds" :items="trackOptions" item-title="displayName"
                            item-value="tenantId" label="All tracks" density="compact" hide-details multiple chips
                            closable-chips clearable></v-autocomplete>

                        <v-divider class="my-4"></v-divider>
                        <div class="d-flex align-center mb-1">
                            <div class="text-subtitle-2">Distance</div>
                            <v-spacer></v-spacer>
                            <span class="text-body-2 font-weight-medium">{{ draftRadiusMiles }} mi</span>
                        </div>
                        <v-slider v-model="draftRadiusMiles" :min="5" :max="500" :step="5" thumb-label
                            color="primary" hide-details></v-slider>
                        <div class="d-flex align-center ga-2 mt-3">
                            <v-text-field v-model="draftZip" label="Center on zip code" density="compact" hide-details
                                style="max-width: 200px" @keyup.enter="applyDraftZip"></v-text-field>
                            <v-btn variant="tonal" size="small" :loading="geolocating" @click="useMyLocation"
                                prepend-icon="mdi-crosshairs-gps">Use my location</v-btn>
                        </div>
                        <div v-if="centerLabel" class="text-caption text-medium-emphasis mt-2">
                            Centered on: <strong>{{ centerLabel }}</strong>
                            <v-btn variant="text" size="x-small" @click="clearCenter">Clear</v-btn>
                        </div>
                        <p v-else class="text-caption text-medium-emphasis mt-2">
                            Set a location to filter by distance.
                        </p>
                    </template>
                </v-card-text>
                <v-divider></v-divider>
                <v-card-actions>
                    <v-btn variant="text" @click="resetFilters">Reset</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn color="primary" variant="flat" @click="applyFilters">Apply</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3500">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, watch, nextTick } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import {
    DiscoverService, type DiscoverQuery, type EventDiscoverItem,
    type EventTypeOption,
} from '@/services/DiscoverService'
import { EventService, type EventDto } from '@/services/EventService'
import { branding } from '@/stores/branding'
import { platformBranding, platformImageUrl } from '@/stores/platformBranding'
import tenantHelper from '@/helpers/TenantHelper'
import { geocode, browserGeolocate } from '@/helpers/Geocode'

// Event types this apex page surfaces. Lesson / Private Booking / Other are
// intentionally excluded everywhere on the apex Events page (carousel, calendar,
// list, and filter modal).
const APEX_ALLOWED_CODES = ['open_ride', 'race', 'practice']
const MI_PER_KM = 0.621371
const KM_PER_MI = 1.609344
const DEFAULT_RADIUS_MILES = 75

const route = useRoute()
const isApex = !tenantHelper.getSubdomain()
const tz = isApex ? dayjs.tz.guess() : (branding.timezone || 'UTC')

const discoverService = new DiscoverService()
const eventService = new EventService()

// ── Normalized event view-model shared by both modes ───────────────────────
interface CalEvent {
    id: string
    title: string
    startsAtUtc: string
    endsAtUtc: string
    eventTypeCode: string
    eventTypeName: string
    eventTypeColor: string
    imageUrl: string | null
    eventTypeImageUrl: string | null
    tenantId: string | null
    tenantSubdomain: string | null
    tenantDisplayName: string | null
    city: string | null
    region: string | null
    locationLabel: string | null
    distanceMiles: number | null
}

const rawEvents = ref<CalEvent[]>([])
const loading = ref(false)

// ── View + month-cursor state ───────────────────────────────────────────────
const viewMode = ref<'month' | 'list'>('month')
const cursor = ref(dayjs().tz(tz).startOf('month'))
const weekdayLabels = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
const monthLabel = computed(() => cursor.value.format('MMMM YYYY'))

// ── Filter state (live) + draft (edited inside the modal) ───────────────────
const selectedTypeCodes = ref<string[]>([])
const selectedTenantIds = ref<string[]>([])
const radiusMiles = ref<number>(DEFAULT_RADIUS_MILES)
const center = ref<{ lat: number; lng: number } | null>(null)
const centerLabel = ref<string>('')
const outOfCountry = ref(false)

const apexTypeOptions = ref<EventTypeOption[]>([])
const trackOptions = ref<{ tenantId: string; displayName: string }[]>([])

// Modal drafts
const filtersOpen = ref(false)
const draftTypeCodes = ref<string[]>([])
const draftTenantIds = ref<string[]>([])
const draftRadiusMiles = ref<number>(DEFAULT_RADIUS_MILES)
const draftZip = ref('')

// Out-of-country zip (on-page)
const zipInput = ref('')
const geolocating = ref(false)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

// Event types offered in the modal. Apex pulls a stable allow-list from the API;
// a tenant derives them from its own loaded events.
const typeOptions = computed<EventTypeOption[]>(() => {
    if (isApex) return apexTypeOptions.value
    const map = new Map<string, EventTypeOption>()
    for (const e of rawEvents.value) {
        if (!map.has(e.eventTypeCode)) {
            map.set(e.eventTypeCode, { code: e.eventTypeCode, name: e.eventTypeName, color: e.eventTypeColor })
        }
    }
    return [...map.values()]
})

const activeFilterCount = computed(() => {
    let n = 0
    if (selectedTypeCodes.value.length > 0) n++
    if (isApex && selectedTenantIds.value.length > 0) n++
    if (isApex && center.value) n++
    return n
})

// ── Hero ────────────────────────────────────────────────────────────────────
const heroStyle = computed(() => {
    const url = isApex
        ? platformImageUrl(platformBranding.data?.heroImageUrl)
        : (branding.heroImageUrl || null)
    return url ? { backgroundImage: `url(${url})` } : {}
})
const heroSubtitle = computed(() =>
    isApex
        ? 'Find upcoming motocross events and ride days near you'
        : `Upcoming events at ${branding.displayName || 'the track'}`)

// ── Filtered + derived event sets ───────────────────────────────────────────
const filteredEvents = computed<CalEvent[]>(() => {
    let list = rawEvents.value
    if (selectedTypeCodes.value.length > 0) {
        list = list.filter(e => selectedTypeCodes.value.includes(e.eventTypeCode))
    }
    if (isApex && selectedTenantIds.value.length > 0) {
        list = list.filter(e => e.tenantId && selectedTenantIds.value.includes(e.tenantId))
    }
    return list
})

const upcomingEvents = computed<CalEvent[]>(() => {
    const startOfToday = dayjs().tz(tz).startOf('day')
    return filteredEvents.value
        .filter(e => dayjs.utc(e.endsAtUtc).tz(tz).isAfter(startOfToday))
        .sort((a, b) => a.startsAtUtc.localeCompare(b.startsAtUtc))
        .slice(0, 24)
})

interface DayCell {
    dayNumber: number
    inMonth: boolean
    isToday: boolean
    events: CalEvent[]
}
const monthDays = computed<DayCell[]>(() => {
    const monthStart = cursor.value.startOf('month')
    const gridStart = monthStart.startOf('week')
    const gridEnd = cursor.value.endOf('month').endOf('week')
    const todayKey = dayjs().tz(tz).format('YYYY-MM-DD')
    const cells: DayCell[] = []
    let d = gridStart
    while (d.isBefore(gridEnd) || d.isSame(gridEnd, 'day')) {
        const key = d.format('YYYY-MM-DD')
        cells.push({
            dayNumber: d.date(),
            inMonth: d.month() === monthStart.month(),
            isToday: key === todayKey,
            events: filteredEvents.value
                .filter(e => dayjs.utc(e.startsAtUtc).tz(tz).format('YYYY-MM-DD') === key)
                .sort((a, b) => a.startsAtUtc.localeCompare(b.startsAtUtc)),
        })
        d = d.add(1, 'day')
    }
    return cells
})

// ── Loading ─────────────────────────────────────────────────────────────────
function rangeUtc() {
    // Fetch from the earlier of today / the visible grid start, out to the later
    // of +6 months / the visible grid end — covers the carousel, list, and a few
    // months of calendar navigation in one request.
    const gridStart = cursor.value.startOf('month').startOf('week')
    const gridEnd = cursor.value.endOf('month').endOf('week')
    const today = dayjs().tz(tz).startOf('day')
    const from = gridStart.isBefore(today) ? gridStart : today
    const sixMonths = dayjs().tz(tz).add(6, 'month')
    const to = gridEnd.isAfter(sixMonths) ? gridEnd : sixMonths
    return { fromUtc: from.utc().toISOString(), toUtc: to.utc().toISOString() }
}

function mapDiscover(e: EventDiscoverItem): CalEvent {
    return {
        id: e.eventId,
        title: e.title,
        startsAtUtc: e.startsAtUtc,
        endsAtUtc: e.endsAtUtc,
        eventTypeCode: e.eventTypeCode,
        eventTypeName: e.eventTypeName,
        eventTypeColor: e.eventTypeColor,
        imageUrl: e.imageUrl,
        eventTypeImageUrl: e.eventTypeImageUrl,
        tenantId: e.tenantId,
        tenantSubdomain: e.tenantSubdomain,
        tenantDisplayName: e.tenantDisplayName,
        city: e.tenantCity,
        region: e.tenantRegion,
        locationLabel: e.locationLabel,
        distanceMiles: e.distanceKm !== null ? e.distanceKm * MI_PER_KM : null,
    }
}

function mapTenant(e: EventDto): CalEvent {
    return {
        id: e.id,
        title: e.title,
        startsAtUtc: e.startsAtUtc,
        endsAtUtc: e.endsAtUtc,
        eventTypeCode: e.eventTypeCode,
        eventTypeName: e.eventTypeName,
        eventTypeColor: e.eventTypeColor,
        imageUrl: e.imageUrl,
        eventTypeImageUrl: e.eventTypeImageUrl,
        tenantId: null,
        tenantSubdomain: null,
        tenantDisplayName: branding.displayName ?? null,
        city: null,
        region: null,
        locationLabel: e.locationLabel,
        distanceMiles: null,
    }
}

async function load() {
    loading.value = true
    const { fromUtc, toUtc } = rangeUtc()
    try {
        if (isApex) {
            const params: DiscoverQuery = {
                fromUtc,
                toUtc,
                // Always bound to the allowed codes server-side; the per-type
                // checkboxes narrow further client-side.
                eventTypeCodes: APEX_ALLOWED_CODES,
            }
            if (center.value) {
                params.lat = center.value.lat
                params.lng = center.value.lng
                params.radiusKm = radiusMiles.value * KM_PER_MI
            }
            const r = await discoverService.searchEvents(params)
            rawEvents.value = (r.data as any).data.map(mapDiscover)
        } else {
            const r = await eventService.list(fromUtc, toUtc)
            rawEvents.value = ((r.data as any).data as EventDto[])
                .filter(e => e.status === 'scheduled')
                .map(mapTenant)
        }
    } catch (err) {
        console.error('Failed to load events', err)
        flash('Could not load events.', 'error')
    } finally {
        loading.value = false
    }
}

// Refetch when the month window moves outside what we last loaded, or when the
// distance center/radius changes (distance is computed server-side).
watch(cursor, (next, prev) => {
    // Only refetch if navigating to a month we likely didn't cover. Cheap guard:
    // refetch whenever the month actually changes.
    if (!next.isSame(prev, 'month')) load()
})

// ── Init ─────────────────────────────────────────────────────────────────────
async function initApex() {
    // Modal options.
    try {
        const [types, tracks] = await Promise.all([
            discoverService.listEventTypes(APEX_ALLOWED_CODES),
            discoverService.searchTracks({}),
        ])
        apexTypeOptions.value = (types.data as any).data
        trackOptions.value = ((tracks.data as any).data as any[])
            .map(t => ({ tenantId: t.tenantId, displayName: t.displayName }))
    } catch (err) {
        console.error('Failed to load filter options', err)
    }

    // Geolocate to decide the US vs out-of-country branch and seed the center.
    try {
        const debug = (route.query.debugCountry as string | undefined) || undefined
        const geo = (await discoverService.geoLocate(debug)).data.data
        if (geo.countryCode && geo.countryCode !== 'US') {
            outOfCountry.value = true
            // Out of country: default to race events only; no radius until a zip
            // is entered.
            selectedTypeCodes.value = ['race']
        } else if (geo.latitude !== null && geo.longitude !== null) {
            center.value = { lat: geo.latitude, lng: geo.longitude }
            centerLabel.value = 'your area'
        }
    } catch (err) {
        // Geolocation is best-effort; fall back to a no-radius, all-types view.
        console.error('Geolocate failed', err)
    }

    await load()
}

onMounted(async () => {
    refreshScrollSoon()
    window.addEventListener('resize', updateScrollState)
    if (isApex) {
        await initApex()
    } else {
        await load()
    }
})
onBeforeUnmount(() => window.removeEventListener('resize', updateScrollState))

// ── Filters modal actions ────────────────────────────────────────────────────
function openFilters() {
    draftTypeCodes.value = [...selectedTypeCodes.value]
    draftTenantIds.value = [...selectedTenantIds.value]
    draftRadiusMiles.value = radiusMiles.value
    draftZip.value = ''
    filtersOpen.value = true
}

async function applyDraftZip() {
    if (!draftZip.value.trim()) return
    geolocating.value = true
    try {
        const g = await geocode(draftZip.value.trim())
        if (g) {
            center.value = { lat: g.lat, lng: g.lng }
            centerLabel.value = g.displayName
        } else {
            flash(`Couldn't find "${draftZip.value}".`, 'error')
        }
    } finally {
        geolocating.value = false
    }
}

async function useMyLocation() {
    geolocating.value = true
    try {
        const pos = await browserGeolocate()
        if (!pos) {
            flash('Could not access your location. Try a zip code instead.', 'error')
            return
        }
        center.value = pos
        centerLabel.value = 'your current location'
    } finally {
        geolocating.value = false
    }
}

function clearCenter() {
    center.value = null
    centerLabel.value = ''
}

async function applyFilters() {
    selectedTypeCodes.value = [...draftTypeCodes.value]
    selectedTenantIds.value = [...draftTenantIds.value]
    radiusMiles.value = draftRadiusMiles.value
    filtersOpen.value = false
    // Apex distance is computed server-side from the center + radius, both of
    // which can change in the modal (zip / use-my-location / clear / slider), so
    // always refetch. Type + track filtering is client-side and instant, but the
    // extra fetch keeps the data correct regardless of what changed.
    if (isApex) await load()
}

function resetFilters() {
    draftTypeCodes.value = []
    draftTenantIds.value = []
    draftRadiusMiles.value = DEFAULT_RADIUS_MILES
    draftZip.value = ''
}

// Out-of-country on-page zip entry.
async function applyZip() {
    if (!zipInput.value.trim()) return
    geolocating.value = true
    try {
        const g = await geocode(zipInput.value.trim())
        if (g) {
            center.value = { lat: g.lat, lng: g.lng }
            centerLabel.value = g.displayName
            radiusMiles.value = DEFAULT_RADIUS_MILES
            await load()
        } else {
            flash(`Couldn't find "${zipInput.value}".`, 'error')
        }
    } finally {
        geolocating.value = false
    }
}

// ── Month nav ─────────────────────────────────────────────────────────────────
function prevMonth() { cursor.value = cursor.value.subtract(1, 'month') }
function nextMonth() { cursor.value = cursor.value.add(1, 'month') }
function goToday() { cursor.value = dayjs().tz(tz).startOf('month') }

// ── Carousel scroll (mirrors Home.vue) ─────────────────────────────────────────
const carouselTrack = ref<HTMLElement | null>(null)
const canScrollLeft = ref(false)
const canScrollRight = ref(false)
function updateScrollState() {
    const el = carouselTrack.value
    if (!el) return
    canScrollLeft.value = el.scrollLeft > 4
    canScrollRight.value = el.scrollLeft + el.clientWidth < el.scrollWidth - 4
}
function scrollCarousel(direction: -1 | 1) {
    const el = carouselTrack.value
    if (!el) return
    const card = el.querySelector('.carousel-card') as HTMLElement | null
    const step = card ? card.offsetWidth + 16 : el.clientWidth * 0.8
    el.scrollBy({ left: direction * step, behavior: 'smooth' })
}
function refreshScrollSoon() { nextTick(() => updateScrollState()) }
watch(() => upcomingEvents.value.length, refreshScrollSoon)

// ── Links + formatting ─────────────────────────────────────────────────────────
// Apex events live on a tenant subdomain, so their cards are plain anchors to
// that subdomain (mirrors the home page). Tenant events use the in-app router.
function tenantUrl(subdomain: string): string {
    const proto = window.location.protocol
    const port = window.location.port ? `:${window.location.port}` : ''
    return `${proto}//${subdomain}.${tenantHelper.rootDomain()}${port}/`
}
function linkTag(e: CalEvent): string {
    return isApex ? 'a' : 'router-link'
}
function linkProps(e: CalEvent): Record<string, unknown> {
    if (isApex && e.tenantSubdomain) {
        return { href: `${tenantUrl(e.tenantSubdomain)}Calendar?eventId=${e.id}`, rel: 'noopener' }
    }
    return { to: `/Event/${e.id}` }
}

function locationText(e: CalEvent): string {
    if (isApex) {
        const place = [e.city, e.region].filter(Boolean).join(', ')
        return [e.tenantDisplayName, place].filter(Boolean).join(' · ') || 'Location not set'
    }
    return e.locationLabel || e.tenantDisplayName || ''
}

function eventImageStyle(e: CalEvent) {
    const img = e.imageUrl ?? e.eventTypeImageUrl ?? null
    const url = isApex ? platformImageUrl(img) : absoluteTenantUrl(img)
    if (url) {
        return { backgroundImage: `url(${url})`, backgroundColor: e.eventTypeColor || '#1976D2' }
    }
    return { backgroundColor: e.eventTypeColor || '#1976D2' }
}

// Tenant image URLs are relative /uploads paths; join to the API origin.
const apiUrl: string = (import.meta as any).env?.VITE_API_ENDPOINT ?? ''
function absoluteTenantUrl(url: string | null | undefined): string | null {
    if (!url) return null
    if (/^https?:\/\//i.test(url)) return url
    try { return `${new URL(apiUrl, window.location.origin).origin}${url}` } catch { return url }
}

// Per-type icon for the event-type line (replaces the old colored type pill).
function eventTypeIcon(code: string): string {
    switch (code) {
        case 'open_ride': return 'mdi-motorbike'
        case 'race': return 'mdi-trophy'
        case 'practice': return 'mdi-timer-outline'
        case 'lesson': return 'mdi-school-outline'
        case 'private_booking': return 'mdi-calendar-lock'
        default: return 'mdi-calendar-star'
    }
}

function formatDay(utc: string): string { return dayjs.utc(utc).tz(tz).format('D') }
function formatMonth(utc: string): string { return dayjs.utc(utc).tz(tz).format('MMM').toUpperCase() }
function formatWhen(utc: string): string { return dayjs.utc(utc).tz(tz).format('ddd, MMM D · h:mm A') }

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>

<style scoped>
.events-page {
    background-color: #f5f5f5;
    min-height: 100vh;
}

/* ── Hero band ─────────────────────────────────────────────────────────────── */
.events-hero {
    position: relative;
    min-height: 280px;
    background-size: cover;
    background-position: center;
    background-color: rgb(var(--v-theme-secondary));
}
.events-hero-overlay {
    background: linear-gradient(90deg, rgba(20, 24, 32, 0.92) 0%, rgba(20, 24, 32, 0.55) 60%, rgba(20, 24, 32, 0.2) 100%);
    min-height: 280px;
    display: flex;
    align-items: center;
    padding: 3rem 0;
}

/* ── Carousel (mirrors Home.vue next-up) ───────────────────────────────────── */
.carousel-wrap { position: relative; }
.carousel-arrow {
    position: absolute;
    top: 50%;
    transform: translateY(-50%);
    z-index: 2;
    width: 44px;
    height: 44px;
    border-radius: 50%;
    background: rgba(255, 255, 255, 0.95);
    color: rgba(0, 0, 0, 0.87);
    border: 1px solid rgba(0, 0, 0, 0.08);
    box-shadow: 0 2px 10px rgba(0, 0, 0, 0.18);
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
}
.carousel-arrow:hover { background: #fff; }
.carousel-arrow--left { left: -8px; }
.carousel-arrow--right { right: -8px; }
@media (max-width: 600px) {
    .carousel-arrow--left { left: 4px; }
    .carousel-arrow--right { right: 4px; }
}
.carousel-track {
    display: flex;
    gap: 16px;
    overflow-x: auto;
    overflow-y: hidden;
    scroll-snap-type: x mandatory;
    scroll-behavior: smooth;
    /* Top room so the date badge (which sits at top: -6px on each card) isn't
       clipped by the track's overflow-y: hidden. */
    padding-top: 12px;
    padding-bottom: 4px;
    scrollbar-width: none;
    -ms-overflow-style: none;
}
.carousel-track::-webkit-scrollbar { display: none; }
.carousel-card {
    flex: 0 0 auto;
    scroll-snap-align: start;
    width: calc((100% - 48px) / 4);
}
@media (max-width: 1264px) { .carousel-card { width: calc((100% - 32px) / 3); } }
@media (max-width: 960px) { .carousel-card { width: calc((100% - 16px) / 2); } }
@media (max-width: 600px) { .carousel-card { width: 88%; } }

.event-card-link { text-decoration: none; color: inherit; display: block; height: 100%; }
.event-card { overflow: visible; transition: transform 0.15s ease; }
.event-card:hover { transform: translateY(-2px); }
.event-image {
    position: relative;
    height: 150px;
    background-size: cover;
    background-position: center;
    border-top-left-radius: inherit;
    border-top-right-radius: inherit;
}
.event-datebadge {
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
.event-datebadge .event-day { font-size: 1.4rem; font-weight: 700; }
.event-datebadge .event-month { font-size: 0.65rem; letter-spacing: 0.08em; opacity: 0.85; margin-top: 2px; }

/* ── List rows ─────────────────────────────────────────────────────────────── */
.list-row { transition: box-shadow 0.15s ease; }
.list-row:hover { box-shadow: 0 4px 14px rgba(0, 0, 0, 0.1); }
.list-datebadge {
    width: 56px;
    border-right: 2px solid rgba(0, 0, 0, 0.08);
    padding-right: 12px;
}
.list-thumb {
    width: 84px;
    height: 64px;
    border-radius: 6px;
    background-size: cover;
    background-position: center;
}
@media (max-width: 600px) { .list-thumb { display: none; } }

/* ── Filter modal bits ─────────────────────────────────────────────────────── */
.type-swatch {
    display: inline-block;
    width: 12px;
    height: 12px;
    border-radius: 3px;
}

/* ── Calendar grid (adapted from Calendar.vue) ─────────────────────────────── */
.calendar-grid {
    display: grid;
    grid-template-columns: repeat(7, 1fr);
    gap: 1px;
    background: rgba(0, 0, 0, 0.08);
}
.weekday-header {
    background: rgb(var(--v-theme-surface));
    padding: 8px;
    text-align: center;
    font-weight: 600;
    font-size: 0.85rem;
    color: rgba(var(--v-theme-on-surface), 0.7);
}
.day-cell {
    background: rgb(var(--v-theme-surface));
    min-height: 110px;
    padding: 6px;
    overflow: hidden;
}
.day-cell.other-month { background: rgba(var(--v-theme-on-surface), 0.03); }
.day-cell.other-month .day-num { color: rgba(var(--v-theme-on-surface), 0.35); }
.day-cell.is-today .day-num {
    background: rgb(var(--v-theme-primary));
    color: rgb(var(--v-theme-on-primary));
    border-radius: 50%;
    width: 24px;
    height: 24px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
}
.day-num { font-size: 0.85rem; font-weight: 500; }
.day-top-row { display: flex; align-items: center; justify-content: space-between; gap: 4px; margin-bottom: 4px; }
.event-chip-link { text-decoration: none; display: block; }
.event-chip {
    color: white;
    font-size: 0.7rem;
    padding: 2px 6px;
    border-radius: 3px;
    margin-bottom: 2px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    cursor: pointer;
}
@media (max-width: 600px) {
    .day-cell { min-height: 70px; padding: 3px; font-size: 0.7rem; }
    .event-chip { font-size: 0.6rem; padding: 1px 3px; }
}
</style>
