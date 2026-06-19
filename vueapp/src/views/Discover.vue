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

        <!-- Track cards: same look as the home page's featured tracks. Featured
             tracks are ordered first when not searching by location. -->
        <div class="d-flex align-center mb-3 ga-2">
            <h2 class="text-h5 font-weight-bold">Tracks ({{ tracks.length }})</h2>
            <v-progress-circular v-if="loading" indeterminate size="20" width="2" color="primary"></v-progress-circular>
        </div>
        <TrackCardGrid :tracks="orderedTracks" show-chips :featured-ids="featuredIds"
            :highlighted-id="hoveredId" @hover="hoveredId = $event"
            empty-text="No tracks match your search." />

        <!-- Map: reuses the same component as the home page. -->
        <h2 class="text-h5 font-weight-bold mt-10 mb-3">Tracks map</h2>
        <v-card variant="outlined" class="pa-4" style="overflow: visible">
            <TracksMap :tracks="tracks" :highlighted-id="hoveredId" @hover="hoveredId = $event"
                @select="openTrack" />
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3500">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { DiscoverService, type TrackDiscoverItem } from '@/services/DiscoverService'
import { geocode, browserGeolocate } from '@/helpers/Geocode'
import { platformBranding } from '@/stores/platformBranding'
import tenantHelper from '@/helpers/TenantHelper'
import TrackCardGrid from '@/components/TrackCardGrid.vue'
import TracksMap from '@/components/TracksMap.vue'
import { tenantHomeUrl } from '@/helpers/tenantLinks'

const service = new DiscoverService()
const route = useRoute()
const router = useRouter()

// tenantId of the track currently emphasized, synced both ways between the
// track cards and the map pins.
const hoveredId = ref<string | null>(null)
// Admin-curated featured tracks (same list the home page uses) — badged on the cards.
const featuredIds = computed<string[]>(() => platformBranding.data?.featuredTrackIds ?? [])

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
const loading = ref(false)
const geolocating = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

// When not searching by location, surface the admin-curated featured tracks first
// (same list the home page uses). With a location active, keep the backend's
// distance ordering instead.
const orderedTracks = computed(() => {
    if (coords.value) return tracks.value
    const featured = platformBranding.data?.featuredTrackIds ?? []
    if (featured.length === 0) return tracks.value
    const rank = new Map(featured.map((id, i) => [id, i]))
    return [...tracks.value].sort(
        (a, b) => (rank.get(a.tenantId) ?? Number.MAX_SAFE_INTEGER) - (rank.get(b.tenantId) ?? Number.MAX_SAFE_INTEGER))
})

// Restore a shared/bookmarked search from the URL (?near=...&radius=...).
onMounted(() => {
    // RidePass sets page titles via document.title (no vue-meta); the branding
    // store has already run by the time this route mounts.
    document.title = 'Find tracks — RidePass'
    const near = typeof route.query.near === 'string' ? route.query.near : ''
    const radiusQ = typeof route.query.radius === 'string' ? Number(route.query.radius) : NaN
    if (near) locationInput.value = near
    if (!Number.isNaN(radiusQ) && radiusOptions.some(o => o.value === radiusQ)) radiusKm.value = radiusQ
    runSearch()
})

// Reflect the active search in the URL so it can be bookmarked or shared.
function syncUrl() {
    const query: Record<string, string> = {}
    if (locationInput.value.trim()) query.near = locationInput.value.trim()
    if (coords.value && radiusKm.value > 0) query.radius = String(radiusKm.value)
    router.replace({ query }).catch(() => { /* ignore redundant navigation */ })
}

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

        const tr = await service.searchTracks({ lat, lng, radiusKm: radius, q })
        tracks.value = (tr.data as any).data
        syncUrl()
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
    const proto = window.location.protocol
    const port = window.location.port ? `:${window.location.port}` : ''
    return `${proto}//${subdomain}.${tenantHelper.rootDomain()}${port}/`
}

// Map pin click -> open that track's public home (client-type-aware).
function openTrack(t: TrackDiscoverItem) {
    window.location.href = tenantHomeUrl(t)
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
