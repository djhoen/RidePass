<template>
    <v-container>
        <h1 class="text-h4 mb-2">Find tracks near you</h1>
        <p class="text-body-2 text-medium-emphasis mb-6">
            Search MX and MTB tracks by city, state, or use your current location.
        </p>

        <v-card class="mb-6 pa-4">
            <v-row dense>
                <v-col cols="12" md="5">
                    <v-text-field v-model="locationInput" label="City, state, or zip" density="compact" hide-details
                        placeholder="e.g. Denver, CO" @keyup.enter="runSearch"></v-text-field>
                </v-col>
                <v-col cols="12" md="3">
                    <v-select v-model="radiusKm" :items="radiusOptions" label="Radius" density="compact" hide-details
                        :disabled="!coords"></v-select>
                </v-col>
                <v-col cols="12" md="4" class="d-flex ga-2">
                    <v-btn variant="tonal" :loading="geolocating" @click="useMyLocation" prepend-icon="mdi-crosshairs-gps">
                        Use my location
                    </v-btn>
                    <v-btn color="primary" :loading="loading" @click="runSearch">Search</v-btn>
                </v-col>
            </v-row>
            <div v-if="resolvedLocationLabel" class="text-caption text-medium-emphasis mt-2">
                Searching near: <strong>{{ resolvedLocationLabel }}</strong>
                <v-btn v-if="coords" variant="text" size="x-small" class="ml-2" @click="clearLocation">Clear</v-btn>
            </div>
        </v-card>

        <v-row>
            <v-col cols="12" md="6">
                <h2 class="text-h6 mb-3">Tracks ({{ tracks.length }})</h2>
                <v-card v-for="t in tracks" :key="t.tenantId" class="mb-3">
                    <v-card-text>
                        <div class="d-flex align-start">
                            <div class="flex-grow-1">
                                <div class="text-h6">{{ t.displayName }}</div>
                                <div class="text-body-2 text-medium-emphasis">
                                    <span v-if="t.city">{{ t.city }}</span><span v-if="t.city && t.region">, </span>
                                    <span v-if="t.region">{{ t.region }}</span>
                                    <span v-if="!t.city && !t.region" class="text-medium-emphasis">Location not set</span>
                                </div>
                                <div class="text-caption mt-1">
                                    <v-chip v-if="t.distanceKm !== null" size="x-small" color="primary" class="mr-2">
                                        {{ formatDistance(t.distanceKm) }}
                                    </v-chip>
                                    <v-chip v-if="t.upcomingEventsCount > 0" size="x-small" color="secondary">
                                        {{ t.upcomingEventsCount }} upcoming
                                    </v-chip>
                                </div>
                            </div>
                            <v-btn variant="tonal" size="small" :href="tenantUrl(t.subdomain)" target="_blank" rel="noopener">
                                Visit
                            </v-btn>
                        </div>
                    </v-card-text>
                </v-card>
                <v-card v-if="!loading && tracks.length === 0" variant="outlined">
                    <v-card-text class="text-center text-medium-emphasis py-8">
                        No tracks match your search.
                    </v-card-text>
                </v-card>
            </v-col>

            <v-col cols="12" md="6">
                <h2 class="text-h6 mb-3">Upcoming Events ({{ events.length }})</h2>
                <v-card v-for="e in events" :key="e.eventId" class="mb-3">
                    <v-card-text>
                        <div class="d-flex align-start">
                            <div class="flex-grow-1">
                                <div class="d-flex align-center ga-2 mb-1">
                                    <v-chip size="x-small" :style="{ backgroundColor: e.eventTypeColor, color: '#fff' }">
                                        {{ e.eventTypeName }}
                                    </v-chip>
                                    <span class="text-caption">{{ formatWhen(e.startsAtUtc) }}</span>
                                </div>
                                <div class="text-subtitle-1">{{ e.title }}</div>
                                <div class="text-body-2 text-medium-emphasis">
                                    {{ e.tenantDisplayName }}
                                    <span v-if="e.tenantCity">— {{ e.tenantCity }}<span v-if="e.tenantRegion">, {{ e.tenantRegion }}</span></span>
                                </div>
                                <div v-if="e.distanceKm !== null" class="mt-1">
                                    <v-chip size="x-small" color="primary">{{ formatDistance(e.distanceKm) }}</v-chip>
                                </div>
                            </div>
                            <v-btn variant="tonal" size="small" :href="tenantUrl(e.tenantSubdomain)" target="_blank" rel="noopener">
                                Visit Track
                            </v-btn>
                        </div>
                    </v-card-text>
                </v-card>
                <v-card v-if="!loading && events.length === 0" variant="outlined">
                    <v-card-text class="text-center text-medium-emphasis py-8">
                        No events match your search.
                    </v-card-text>
                </v-card>
            </v-col>
        </v-row>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3500">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import dayjs from 'dayjs'
import { DiscoverService, type TrackDiscoverItem, type EventDiscoverItem } from '@/services/DiscoverService'
import { geocode, browserGeolocate } from '@/helpers/Geocode'

const service = new DiscoverService()

const locationInput = ref('')
const coords = ref<{ lat: number; lng: number } | null>(null)
const resolvedLocationLabel = ref<string>('')
const radiusKm = ref<number>(80)
const radiusOptions = [
    { title: '25 km (~15 mi)', value: 25 },
    { title: '50 km (~30 mi)', value: 50 },
    { title: '80 km (~50 mi)', value: 80 },
    { title: '160 km (~100 mi)', value: 160 },
    { title: '400 km (~250 mi)', value: 400 },
    { title: 'Any distance', value: 0 },
]

const tracks = ref<TrackDiscoverItem[]>([])
const events = ref<EventDiscoverItem[]>([])
const loading = ref(false)
const geolocating = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(runSearch)

async function useMyLocation() {
    geolocating.value = true
    try {
        const pos = await browserGeolocate()
        if (!pos) {
            flash('Could not access your location. You can type a city instead.', 'error')
            return
        }
        coords.value = pos
        locationInput.value = ''
        resolvedLocationLabel.value = `your current location (${pos.lat.toFixed(3)}, ${pos.lng.toFixed(3)})`
        await runSearch()
    } finally {
        geolocating.value = false
    }
}

async function resolveTypedLocation(): Promise<boolean> {
    if (!locationInput.value.trim()) return true // unchanged — just a text filter
    try {
        const g = await geocode(locationInput.value)
        if (!g) {
            flash(`Couldn't find "${locationInput.value}". Showing text-only results.`, 'error')
            coords.value = null
            resolvedLocationLabel.value = ''
            return false
        }
        coords.value = { lat: g.lat, lng: g.lng }
        resolvedLocationLabel.value = g.displayName
        return true
    } catch {
        flash('Geocoding failed. Showing text-only results.', 'error')
        coords.value = null
        return false
    }
}

async function runSearch() {
    loading.value = true
    try {
        // If the user typed something and we don't yet have coords for it, geocode once.
        if (locationInput.value.trim() && !coords.value) {
            await resolveTypedLocation()
        }
        const q = locationInput.value.trim() || undefined
        const lat = coords.value?.lat
        const lng = coords.value?.lng
        const radius = coords.value && radiusKm.value > 0 ? radiusKm.value : undefined

        const [tr, ev] = await Promise.all([
            service.searchTracks({ lat, lng, radiusKm: radius, q }),
            service.searchEvents({ lat, lng, radiusKm: radius, q }),
        ])
        tracks.value = (tr.data as any).data
        events.value = (ev.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Search failed.', 'error')
    } finally {
        loading.value = false
    }
}

function clearLocation() {
    coords.value = null
    resolvedLocationLabel.value = ''
    runSearch()
}

function tenantUrl(subdomain: string): string {
    const root = import.meta.env.VITE_ROOT_DOMAIN ?? window.location.hostname.replace(/^[^.]+\./, '')
    const port = window.location.port ? `:${window.location.port}` : ''
    return `${window.location.protocol}//${subdomain}.${root}${port}/`
}

function formatDistance(km: number): string {
    const mi = km * 0.621371
    if (km < 10) return `${km.toFixed(1)} km · ${mi.toFixed(1)} mi`
    return `${Math.round(km)} km · ${Math.round(mi)} mi`
}

function formatWhen(utc: string): string {
    return dayjs.utc(utc).local().format('ddd, MMM D · h:mm A')
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
