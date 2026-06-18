<template>
    <!-- Lower-48 US map with one auto-placed pin per track. The state shapes and
         the pins share a single d3 Albers projection, so a track's lat/lng lands
         exactly on the right spot. d3-zoom adds drag-to-pan + zoom (buttons /
         double-click / pinch; mouse wheel is intentionally disabled so the map
         doesn't hijack page scrolling). Hovering a pin shows an interactive
         preview card with a caret pointing back at the pin. -->
    <div ref="mapEl" class="tracks-map">
        <svg ref="svgRef" :viewBox="`0 0 ${WIDTH} ${HEIGHT}`" class="tracks-map-svg"
            preserveAspectRatio="xMidYMid meet" role="img"
            aria-label="Map of tracks across the lower 48 states"
            @click="hovered = null">
            <defs>
                <!-- Soft "ocean" backdrop and a subtle top-to-bottom gradient on the
                     land so the map reads with some depth instead of flat white. -->
                <linearGradient id="tm-ocean" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stop-color="#eef4fb" />
                    <stop offset="100%" stop-color="#d6e2f0" />
                </linearGradient>
                <linearGradient id="tm-land" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="0" :y2="HEIGHT">
                    <stop offset="0%" stop-color="#ffffff" />
                    <stop offset="100%" stop-color="#e7edf4" />
                </linearGradient>
            </defs>
            <!-- Background sits outside the zoom layer so it stays put while the land pans/zooms. -->
            <rect x="0" y="0" :width="WIDTH" :height="HEIGHT" fill="url(#tm-ocean)" />
            <g :transform="zoomTransform">
                <path v-for="(d, i) in statePaths" :key="i" :d="d" class="state-path" />
                <g v-for="p in pins" :key="p.track.tenantId" class="track-pin"
                    :transform="`translate(${p.x}, ${p.y})`" tabindex="0"
                    :aria-label="p.track.displayName"
                    @click.stop="onPinClick(p)"
                    @keydown.enter="emit('select', p.track)"
                    @mouseenter="show(p)" @mouseleave="scheduleHide()"
                    @focus="show(p)" @blur="scheduleHide()">
                    <circle :r="emphasized(p) ? haloR * 1.7 : haloR" class="pin-halo" />
                    <circle :r="emphasized(p) ? dotR * 1.8 : dotR" class="pin-dot" />
                </g>
            </g>
        </svg>

        <!-- Zoom controls -->
        <div class="map-zoom-controls">
            <v-btn icon="mdi-plus" size="small" variant="elevated" aria-label="Zoom in"
                @click="zoomBy(1.6)"></v-btn>
            <v-btn icon="mdi-minus" size="small" variant="elevated" aria-label="Zoom out"
                @click="zoomBy(1 / 1.6)"></v-btn>
            <v-btn icon="mdi-restore" size="small" variant="elevated" aria-label="Reset view"
                @click="resetZoom()"></v-btn>
        </div>

        <!-- Hover preview card. Positioned over the pin in screen space (so it
             tracks pan/zoom), flips below the pin when the pin is near the top.
             Interactive: a short hide-delay lets the pointer cross the gap from
             the pin into the card to click the button. -->
        <div v-if="hovered" class="track-hover-card" :class="{ 'is-below': hoverBelow }"
            :style="hoverStyle" @mouseenter="cancelHide()" @mouseleave="scheduleHide()">
            <div v-if="hovered.track.heroImageUrl" class="thc-img"
                :style="{ backgroundImage: `url(${absUrl(hovered.track.heroImageUrl)})` }"></div>
            <div v-else class="thc-img thc-img--placeholder">
                <v-icon icon="mdi-motorbike" size="28"></v-icon>
            </div>
            <div class="thc-body">
                <div class="thc-name">{{ hovered.track.displayName }}</div>
                <div v-if="hovered.track.city || hovered.track.region" class="thc-loc">
                    <v-icon icon="mdi-map-marker" size="13"></v-icon>
                    <span>{{ [hovered.track.city, hovered.track.region].filter(Boolean).join(', ') }}</span>
                </div>
                <div v-if="hovered.track.upcomingEventsCount > 0" class="thc-meta">
                    <v-chip size="x-small" color="primary" variant="flat">
                        {{ hovered.track.upcomingEventsCount }} upcoming
                    </v-chip>
                </div>
                <v-btn class="mt-3" block size="small" color="primary"
                    @click="emit('select', hovered.track)">Visit track</v-btn>
            </div>
            <div class="thc-caret" :style="{ left: `calc(50% + ${caretOffsetPx}px)` }"></div>
        </div>

        <div v-if="pins.length === 0" class="text-center text-medium-emphasis py-4">
            No tracks have a map location yet.
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted, onBeforeUnmount } from 'vue'
import { useDisplay } from 'vuetify'
import { geoAlbers, geoPath } from 'd3-geo'
import { feature } from 'topojson-client'
import { select } from 'd3-selection'
import { zoom as d3zoom, zoomIdentity, type ZoomBehavior, type D3ZoomEvent } from 'd3-zoom'
import 'd3-transition' // augments d3 selections with .transition() for smooth zoom
// us-atlas ships standard US TopoJSON; no types, so treat as any.
import statesTopo from 'us-atlas/states-10m.json'
import type { TrackDiscoverItem } from '@/services/DiscoverService'

const props = defineProps<{ tracks: TrackDiscoverItem[]; highlightedId?: string | null }>()
const emit = defineEmits<{
    (e: 'select', track: TrackDiscoverItem): void
    // Emitted as the pointer enters/leaves a pin so a parent can sync a hovered
    // track card. Null when nothing is hovered.
    (e: 'hover', tenantId: string | null): void
}>()

const { mobile } = useDisplay()

// Albers CONUS is roughly 1.6:1; the SVG scales to its container via viewBox.
const WIDTH = 975
const HEIGHT = 610
const CARD_W = 230

// FIPS ids to drop so only the contiguous 48 (+ DC) render: Alaska (02),
// Hawaii (15), and the territories (AS 60, GU 66, MP 69, PR 72, VI 78).
const EXCLUDE = new Set(['02', '15', '60', '66', '69', '72', '78'])

const topo = statesTopo as any
const allStates = feature(topo, topo.objects.states) as any
const lower48 = {
    type: 'FeatureCollection',
    features: allStates.features.filter((f: any) => !EXCLUDE.has(String(f.id))),
}

// One projection fit to the lower-48 set, reused for shapes and pins so they
// stay aligned.
const projection = geoAlbers().fitSize([WIDTH, HEIGHT], lower48 as any)
const pathGen = geoPath(projection)

const statePaths = computed<string[]>(() =>
    lower48.features.map((f: any) => pathGen(f) || '').filter(Boolean))

interface PlacedPin { track: TrackDiscoverItem; x: number; y: number }
const pins = computed<PlacedPin[]>(() => {
    const out: PlacedPin[] = []
    for (const t of props.tracks) {
        if (t.latitude == null || t.longitude == null) continue
        const xy = projection([t.longitude, t.latitude])
        if (!xy) continue
        out.push({ track: t, x: xy[0], y: xy[1] })
    }
    return out
})

// ── Zoom / pan ──────────────────────────────────────────────────────────────
const svgRef = ref<SVGSVGElement | null>(null)
const transform = ref({ k: 1, x: 0, y: 0 })
const zoomTransform = computed(() =>
    `translate(${transform.value.x},${transform.value.y}) scale(${transform.value.k})`)
// Base pin size: larger on mobile, where the whole map (and each pin with it)
// scales down a lot and small dots get hard to see and tap. Counter-scaled by
// the zoom factor so pins stay a constant screen size as you zoom.
const baseDot = computed(() => (mobile.value ? 9 : 4))
const baseHalo = computed(() => (mobile.value ? 14 : 7))
const dotR = computed(() => baseDot.value / transform.value.k)
const haloR = computed(() => baseHalo.value / transform.value.k)

let zoomBehavior: ZoomBehavior<SVGSVGElement, unknown> | null = null

onMounted(() => {
    if (!svgRef.value) return
    zoomBehavior = d3zoom<SVGSVGElement, unknown>()
        .scaleExtent([1, 8])
        .translateExtent([[0, 0], [WIDTH, HEIGHT]])
        // Allow drag-pan / double-click / touch, but NOT mouse wheel (so the
        // page can still scroll past the map). Ignore non-primary buttons.
        .filter((event: any) => event.type !== 'wheel' && !event.button)
        .on('zoom', (e: D3ZoomEvent<SVGSVGElement, unknown>) => {
            transform.value = { k: e.transform.k, x: e.transform.x, y: e.transform.y }
        })
    select(svgRef.value).call(zoomBehavior)
})
onBeforeUnmount(() => {
    if (svgRef.value) select(svgRef.value).on('.zoom', null)
    cancelHide()
})

function zoomBy(factor: number) {
    if (!svgRef.value || !zoomBehavior) return
    select(svgRef.value).transition().duration(200).call(zoomBehavior.scaleBy as any, factor)
}
function resetZoom() {
    if (!svgRef.value || !zoomBehavior) return
    select(svgRef.value).transition().duration(250).call(zoomBehavior.transform as any, zoomIdentity)
}

// ── Hover card ──────────────────────────────────────────────────────────────
const mapEl = ref<HTMLElement | null>(null)
const wrapperW = ref(0)
const hovered = ref<PlacedPin | null>(null)
let hideTimer: number | undefined

function isHover(p: PlacedPin): boolean {
    return hovered.value?.track.tenantId === p.track.tenantId
}
// A pin is emphasized when hovered locally OR when the parent flags it as the
// active track (e.g. the matching track card is hovered).
function emphasized(p: PlacedPin): boolean {
    return isHover(p) || p.track.tenantId === props.highlightedId
}
// Mobile (no hover): a tap opens the card. Desktop: do nothing on click — the
// card already shows on hover, and navigation is via its "Visit track" button.
function onPinClick(p: PlacedPin) {
    if (mobile.value) show(p)
}
function show(p: PlacedPin) {
    cancelHide()
    hovered.value = p
    wrapperW.value = mapEl.value?.clientWidth ?? 0
    emit('hover', p.track.tenantId)
}
// Short delay so the pointer can cross the gap from the pin into the card.
function scheduleHide() {
    cancelHide()
    hideTimer = window.setTimeout(() => { hovered.value = null; emit('hover', null) }, 160)
}
function cancelHide() {
    if (hideTimer) { clearTimeout(hideTimer); hideTimer = undefined }
}

// Pin position in screen space (% of the wrapper), after the zoom transform.
const hoverPct = computed(() => {
    const p = hovered.value
    if (!p) return { x: 0, y: 0 }
    return {
        x: ((transform.value.x + transform.value.k * p.x) / WIDTH) * 100,
        y: ((transform.value.y + transform.value.k * p.y) / HEIGHT) * 100,
    }
})
const hoverBelow = computed(() => hoverPct.value.y < 30)
// Card horizontal center, clamped so it doesn't clip the map edges.
const cardLeftPct = computed(() => Math.min(86, Math.max(14, hoverPct.value.x)))
const hoverStyle = computed(() => ({
    left: `${cardLeftPct.value}%`,
    top: `${hoverPct.value.y}%`,
}))
// Caret horizontal offset (px) so it keeps pointing at the pin even when the
// card was clamped away from it. Bounded to stay within the card.
const caretOffsetPx = computed(() => {
    if (!wrapperW.value) return 0
    const pinPx = (hoverPct.value.x / 100) * wrapperW.value
    const centerPx = (cardLeftPct.value / 100) * wrapperW.value
    const half = CARD_W / 2 - 18
    return Math.max(-half, Math.min(half, pinPx - centerPx))
})

// ── Image URL helper ────────────────────────────────────────────────────────
const apiUrl: string = (import.meta as any).env?.VITE_API_ENDPOINT ?? ''
function apiOrigin(): string {
    try { return new URL(apiUrl, window.location.origin).origin } catch { return '' }
}
function absUrl(u: string | null): string {
    if (!u) return ''
    return /^https?:\/\//i.test(u) ? u : `${apiOrigin()}${u}`
}
</script>

<style scoped>
.tracks-map {
    position: relative;
}
.tracks-map-svg {
    width: 100%;
    height: auto;
    display: block;
    cursor: grab;
    touch-action: none;
}
.tracks-map-svg:active {
    cursor: grabbing;
}
.state-path {
    fill: url(#tm-land);
    stroke: #c6d2e0;
    stroke-width: 1;
    vector-effect: non-scaling-stroke;
}
.track-pin {
    cursor: pointer;
    outline: none;
}
.pin-halo {
    fill: rgb(var(--v-theme-primary));
    opacity: 0.22;
    transition: opacity 0.15s ease, r 0.12s ease;
}
.pin-dot {
    fill: rgb(var(--v-theme-primary));
    stroke: #ffffff;
    stroke-width: 1.5;
    vector-effect: non-scaling-stroke;
    transition: fill 0.15s ease, r 0.12s ease;
}
.track-pin:hover .pin-halo,
.track-pin:focus .pin-halo {
    opacity: 0.42;
}
.track-pin:hover .pin-dot,
.track-pin:focus .pin-dot {
    fill: rgb(var(--v-theme-secondary));
}

/* Zoom controls, stacked top-right over the map. */
.map-zoom-controls {
    position: absolute;
    top: 10px;
    right: 10px;
    display: flex;
    flex-direction: column;
    gap: 6px;
    z-index: 2;
}

/* Hover preview card. Anchored at the pin (left/top set inline) and shifted so
   it floats above the pin, or below it when the pin is near the top edge. */
.track-hover-card {
    position: absolute;
    z-index: 20;
    width: 230px;
    transform: translate(-50%, calc(-100% - 18px));
    /* Grow out of the pin: scale up anchored at the caret (the side nearest
       the pin). */
    transform-origin: bottom center;
    background: #ffffff;
    border-radius: 12px;
    box-shadow: 0 8px 28px rgba(0, 0, 0, 0.22);
    pointer-events: auto;
    animation: thc-grow 0.17s cubic-bezier(0.2, 0.9, 0.3, 1.25);
}
.track-hover-card.is-below {
    transform: translate(-50%, 18px);
    transform-origin: top center;
    animation: thc-grow-below 0.17s cubic-bezier(0.2, 0.9, 0.3, 1.25);
}
/* Keyframes carry the full positioning transform so the card doesn't jump;
   only the scale animates (anchored at transform-origin). */
@keyframes thc-grow {
    from { opacity: 0; transform: translate(-50%, calc(-100% - 18px)) scale(0.4); }
    to   { opacity: 1; transform: translate(-50%, calc(-100% - 18px)) scale(1); }
}
@keyframes thc-grow-below {
    from { opacity: 0; transform: translate(-50%, 18px) scale(0.4); }
    to   { opacity: 1; transform: translate(-50%, 18px) scale(1); }
}

/* Caret: a small triangle that bridges the gap to the pin. Points down from
   the card bottom by default, up from the top when the card is below the pin.
   Horizontal position is offset inline so it stays aimed at the pin. */
.thc-caret {
    position: absolute;
    width: 0;
    height: 0;
    transform: translateX(-50%);
    filter: drop-shadow(0 2px 1px rgba(0, 0, 0, 0.06));
}
.track-hover-card:not(.is-below) .thc-caret {
    bottom: -10px;
    border-left: 11px solid transparent;
    border-right: 11px solid transparent;
    border-top: 11px solid #ffffff;
}
.track-hover-card.is-below .thc-caret {
    top: -10px;
    border-left: 11px solid transparent;
    border-right: 11px solid transparent;
    border-bottom: 11px solid #ffffff;
}

.thc-img {
    height: 110px;
    background-size: cover;
    background-position: center;
    background-color: rgb(var(--v-theme-secondary));
    border-radius: 12px 12px 0 0;
}
.thc-img--placeholder {
    display: flex;
    align-items: center;
    justify-content: center;
    color: rgba(255, 255, 255, 0.7);
}
.thc-body {
    padding: 10px 12px 12px;
}
.thc-name {
    font-weight: 700;
    font-size: 1rem;
    line-height: 1.2;
}
.thc-loc {
    display: flex;
    align-items: center;
    gap: 3px;
    color: rgba(0, 0, 0, 0.6);
    font-size: 0.8rem;
    margin-top: 3px;
}
.thc-meta {
    margin-top: 8px;
}
</style>
