<template>
    <!-- Reusable grid of track "cover" cards (used on the apex Home and Discover).
         Each card links to the tenant's own subdomain. -->
    <v-row v-if="tracks.length > 0" dense>
        <v-col v-for="t in tracks" :key="t.tenantId" cols="12" sm="6" md="4">
            <v-card class="h-100 track-card" :class="{ 'track-card--active': t.tenantId === highlightedId }"
                :href="tenantHomeUrl(t)" rel="noopener"
                @mouseenter="emit('hover', t.tenantId)" @mouseleave="emit('hover', null)">
                <div class="track-card-image rp-on-photo" :style="imageStyle(t)">
                    <v-chip v-if="featuredIds.includes(t.tenantId)" size="x-small" color="primary"
                        variant="flat" class="track-card-featured">Featured</v-chip>
                </div>
                <v-card-text class="pa-4">
                    <div class="text-h5 font-display-upright mb-1">{{ t.displayName }}</div>
                    <div class="text-body-2 text-medium-emphasis d-flex align-center ga-1">
                        <v-icon icon="mdi-map-marker" size="14"></v-icon>
                        <span>
                            <template v-if="t.city || t.region">
                                <span v-if="t.city">{{ t.city }}</span><span v-if="t.city && t.region">, </span><span v-if="t.region">{{ t.region }}</span>
                            </template>
                            <template v-else>Location not set</template>
                        </span>
                    </div>
                    <div v-if="showChips" class="mt-2 d-flex ga-2">
                        <v-chip v-if="t.distanceKm !== null" size="x-small" color="primary">
                            {{ formatDistance(t.distanceKm) }}
                        </v-chip>
                        <v-chip v-if="t.upcomingEventsCount > 0" size="x-small" variant="tonal">
                            {{ t.upcomingEventsCount }} upcoming
                        </v-chip>
                    </div>
                </v-card-text>
            </v-card>
        </v-col>
    </v-row>
    <v-card v-else-if="emptyText" variant="outlined">
        <v-card-text class="text-center text-medium-emphasis py-8">{{ emptyText }}</v-card-text>
    </v-card>
</template>

<script setup lang="ts">
import { type TrackDiscoverItem } from '@/services/DiscoverService'
import { tenantHomeUrl } from '@/helpers/tenantLinks'

withDefaults(defineProps<{
    tracks: TrackDiscoverItem[]
    emptyText?: string
    // When true, show distance + upcoming-event chips (Discover); off on the apex Home.
    showChips?: boolean
    // tenantId of the track to emphasize (synced with a hovered map pin).
    highlightedId?: string | null
    // tenantIds to badge as "Featured" (the admin-curated list).
    featuredIds?: string[]
}>(), { emptyText: '', showChips: false, highlightedId: null, featuredIds: () => [] })

const emit = defineEmits<{ (e: 'hover', tenantId: string | null): void }>()

const apiUrl: string = (import.meta as any).env?.VITE_API_ENDPOINT ?? ''
function apiOrigin(): string {
    try { return new URL(apiUrl, window.location.origin).origin } catch { return '' }
}
function absoluteUrl(url: string | null | undefined): string | null {
    if (!url) return null
    if (/^https?:\/\//i.test(url)) return url
    return `${apiOrigin()}${url}`
}
// Cover image is the tenant's hero photo; falls back to a flat themed background
// so a card never renders an empty white block.
function imageStyle(t: TrackDiscoverItem) {
    const u = absoluteUrl(t.heroImageUrl)
    return u ? { backgroundImage: `url(${u})` } : { backgroundColor: 'rgb(var(--v-theme-secondary))' }
}
function formatDistance(km: number): string {
    const mi = km * 0.621371
    if (km < 10) return `${km.toFixed(1)} km · ${mi.toFixed(1)} mi`
    return `${Math.round(km)} km · ${Math.round(mi)} mi`
}
</script>

<style scoped>
.track-card {
    text-decoration: none;
    transition: transform 0.15s ease, border-color 0.15s ease;
}
.track-card:hover {
    transform: translateY(-3px);
    border-color: var(--rp-line3);
}
/* Emphasized when the matching map pin is hovered (and vice versa). */
.track-card--active {
    border-color: var(--rp-ink);
    transform: translateY(-3px);
}
.track-card-image {
    position: relative;
    height: 160px;
    background-size: cover;
    background-position: center;
}
/* Bottom fade so the card body reads as one piece with the photo (mockup .tcard .ph). */
.track-card-image::after {
    content: '';
    position: absolute;
    inset: 0;
    background: linear-gradient(180deg, transparent 40%, rgba(var(--rp-scrim), 0.75));
    pointer-events: none;
}
.track-card-featured {
    position: absolute;
    top: 8px;
    left: 8px;
}
</style>
