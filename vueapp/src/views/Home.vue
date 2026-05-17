<template>
    <div>
        <!-- Apex (no tenant resolved): keep the existing "find a track" landing -->
        <v-container v-if="isApex">
            <v-row class="my-12" align="center" justify="center">
                <v-col cols="12" md="8" class="text-center">
                    <h1 class="text-h2 font-weight-bold mb-4">{{ branding.displayName }}</h1>
                    <p v-if="branding.tagline" class="text-h6 text-medium-emphasis mb-8">{{ branding.tagline }}</p>
                    <v-btn color="primary" size="x-large" to="/Discover" class="mr-3"
                        prepend-icon="mdi-map-search">Find Tracks Near You</v-btn>
                    <v-btn variant="outlined" size="x-large" to="/Login">Super Admin Login</v-btn>
                </v-col>
            </v-row>
        </v-container>

        <template v-else>
            <!-- ── 1. Hero ──────────────────────────────────────────────────────── -->
            <section v-if="branding.heroImageUrl" class="hero" :style="{ backgroundImage: `url(${branding.heroImageUrl})` }">
                <div class="hero-overlay text-center">
                    <h1 class="text-h2 font-weight-bold text-white mb-2">{{ branding.displayName }}</h1>
                    <p v-if="branding.tagline" class="text-h6 text-white mb-4">{{ branding.tagline }}</p>
                    <div v-if="effectiveStatus" class="status-badge" :class="`status-${effectiveStatus.tone}`">
                        <v-icon size="small">{{ effectiveStatus.icon }}</v-icon>
                        <strong>{{ effectiveStatus.label }}</strong>
                        <span v-if="effectiveStatus.message" class="ml-2 text-body-2">— {{ effectiveStatus.message }}</span>
                    </div>
                    <div class="mt-6">
                        <v-btn color="primary" size="x-large" to="/Calendar" class="mr-3">See Calendar</v-btn>
                        <v-btn v-if="hasSeasonPasses" variant="outlined" size="x-large" color="white"
                            to="/SeasonPasses">Season Passes</v-btn>
                    </div>
                </div>
            </section>
            <v-container v-else class="text-center my-8">
                <h1 class="text-h2 font-weight-bold mb-2">{{ branding.displayName }}</h1>
                <p v-if="branding.tagline" class="text-h6 text-medium-emphasis mb-4">{{ branding.tagline }}</p>
                <v-chip v-if="effectiveStatus" :color="effectiveStatus.color" class="mb-4">
                    {{ effectiveStatus.label }}<span v-if="effectiveStatus.message"> — {{ effectiveStatus.message }}</span>
                </v-chip>
                <div>
                    <v-btn color="primary" size="x-large" to="/Calendar" class="mr-3">See Calendar</v-btn>
                    <v-btn v-if="hasSeasonPasses" variant="outlined" size="x-large"
                        to="/SeasonPasses">Season Passes</v-btn>
                </div>
            </v-container>

            <v-container>
                <!-- ── 2. Next events row ──────────────────────────────────────── -->
                <!-- Horizontal scroll-snap track. Chevrons overlay the track edges
                     like a typical carousel — only visible while scrolling is possible
                     in that direction. Native scroll keeps touch + keyboard
                     accessibility free; we just script the buttons. -->
                <section v-if="nextEvents.length > 0" class="mb-12">
                    <div class="d-flex align-center mb-4 ga-2">
                        <h2 class="text-h4">{{ nextUpTitle }}</h2>
                        <v-spacer></v-spacer>
                        <v-btn variant="text" to="/Calendar" append-icon="mdi-arrow-right">Full calendar</v-btn>
                    </div>
                    <div class="next-up-wrap">
                        <button v-show="canScrollLeft" type="button" class="next-up-arrow next-up-arrow--left"
                            aria-label="Previous events" @click="scrollNextUp(-1)">
                            <v-icon size="32">mdi-chevron-left</v-icon>
                        </button>
                        <button v-show="canScrollRight" type="button" class="next-up-arrow next-up-arrow--right"
                            aria-label="Next events" @click="scrollNextUp(1)">
                            <v-icon size="32">mdi-chevron-right</v-icon>
                        </button>
                        <div ref="nextUpTrack" class="next-up-track"
                            @scroll.passive="updateNextUpScrollState">
                            <div v-for="e in nextEvents" :key="e.id" class="next-up-card">
                                <v-card class="event-card d-flex flex-column" style="height: 100%">
                                <div class="event-cover" :class="{ 'event-cover--text': !hasCoverImage(e) }"
                                    :style="eventCoverStyle(e)">
                                    <v-chip size="small" class="event-chip">{{ e.eventTypeName }}</v-chip>
                                    <div v-if="!hasCoverImage(e)" class="event-cover-title">{{ e.title }}</div>
                                </div>
                                <v-card-text class="pa-3 flex-grow-1 d-flex flex-column">
                                    <div class="font-weight-bold">{{ e.title }}</div>
                                    <div class="text-caption text-medium-emphasis">{{ formatEventDate(e) }}</div>
                                    <div v-if="e.minTicketPriceCents" class="text-body-2 mt-2">
                                        From <strong>${{ (e.minTicketPriceCents / 100).toFixed(2) }}</strong>
                                    </div>
                                    <div class="d-flex flex-wrap ga-2 mt-auto pt-3">
                                        <v-btn v-if="e.hasRaceEntryTiers" size="small" color="deep-orange"
                                            @click="openBuy(e, 'race_entry')">
                                            Race Entry
                                        </v-btn>
                                        <v-btn v-if="!e.hasRaceEntryTiers"
                                            size="small" variant="text"
                                            :to="{ path: '/Calendar', query: { eventId: e.id } }">
                                            View on calendar
                                        </v-btn>
                                    </div>
                                </v-card-text>
                            </v-card>
                        </div>
                        </div>
                    </div>
                </section>

                <!-- In-page purchase modal — reuses the BuyTicket route's flow component
                     so admins can update the route page once and both surfaces stay in sync. -->
                <v-dialog v-model="buyDialog" :max-width="720" scrollable>
                    <v-card>
                        <v-card-title class="d-flex align-center">
                            {{ buyTitle }}
                            <span v-if="buyEvent" class="text-body-2 text-medium-emphasis ml-2">
                                — {{ buyEvent.title }}
                            </span>
                            <v-spacer></v-spacer>
                            <v-btn icon="mdi-close" variant="text" size="small" @click="buyDialog = false"></v-btn>
                        </v-card-title>
                        <v-card-text class="pa-4">
                            <BuyAdmissionFlow v-if="buyEvent" :event-id="buyEvent.id"
                                :event="buyEvent" :kind-filter="buyKindFilter" @completed="onBuyCompleted" />
                        </v-card-text>
                    </v-card>
                </v-dialog>

                <!-- ── 3. Pricing snapshot ─────────────────────────────────────── -->
                <section v-if="passFromCents !== null || (seasonPassFromCents !== null)" class="mb-12">
                    <h2 class="text-h4 mb-4">Passes</h2>
                    <v-row>
                        <v-col v-if="passFromCents !== null" cols="12" md="6">
                            <v-card class="pa-6" variant="outlined">
                                <h3 class="text-h5 mb-2">Pass</h3>
                                <p class="text-body-2 text-medium-emphasis mb-4">Single-day access to ride.</p>
                                <div class="text-h4 font-weight-bold mb-4">
                                    From ${{ (passFromCents / 100).toFixed(2) }}
                                </div>
                                <v-btn color="primary" to="/Calendar">Pick an Event</v-btn>
                            </v-card>
                        </v-col>
                        <v-col v-if="seasonPassFromCents !== null && branding.seasonPassesEnabled" cols="12" md="6">
                            <v-card class="pa-6" variant="outlined">
                                <h3 class="text-h5 mb-2">Season Pass</h3>
                                <p class="text-body-2 text-medium-emphasis mb-4">Ride the whole season — best value.</p>
                                <div class="text-h4 font-weight-bold mb-4">
                                    From ${{ (seasonPassFromCents / 100).toFixed(2) }}
                                </div>
                                <v-btn color="primary" to="/SeasonPasses">See Season Passes</v-btn>
                            </v-card>
                        </v-col>
                    </v-row>
                </section>

                <!-- ── 4. About ──────────────────────────────────────────────── -->
                <section v-if="branding.aboutHtml" class="mb-12">
                    <h2 class="text-h4 mb-4">About</h2>
                    <div class="rich-text-body" v-html="branding.aboutHtml"></div>
                    <v-img v-if="branding.secondaryHeroUrl" :src="branding.secondaryHeroUrl"
                        max-height="400" cover class="mt-6 rounded"></v-img>
                </section>

                <!-- ── 5. Photo gallery ──────────────────────────────────────── -->
                <section v-if="gallery.length > 0" class="mb-12">
                    <h2 class="text-h4 mb-4">Photos</h2>
                    <v-row>
                        <v-col v-for="(img, idx) in gallery" :key="img.id" cols="6" md="4" lg="3">
                            <!-- Thumbnail: caption hidden here; only shown inside the slideshow. -->
                            <v-img :src="absoluteUrl(img.imageUrl)" aspect-ratio="1" cover
                                class="rounded gallery-thumb" @click="openGallery(idx)"></v-img>
                        </v-col>
                    </v-row>
                </section>

                <!-- ── 6. Track Layout ──────────────────────────────────────── -->
                <!-- Park location info (map + address) was removed — riders find that
                     in the footer now. Track-layout images are unique content, kept here. -->
                <section v-if="trackGraphics.length > 0" class="mb-12">
                    <h2 class="text-h4 mb-4">Track Layout</h2>
                    <div>
                        <v-card v-for="g in trackGraphics" :key="g.id" variant="outlined" class="mb-3">
                            <v-row no-gutters>
                                <v-col cols="12" md="5">
                                    <v-img :src="absoluteUrl(g.imageUrl)" aspect-ratio="1.5" cover></v-img>
                                </v-col>
                                <v-col cols="12" md="7">
                                    <v-card-text>
                                        <h4 v-if="g.title" class="text-h6 mb-2">{{ g.title }}</h4>
                                        <p v-if="g.description" class="text-body-1 mb-0" style="white-space: pre-wrap">{{ g.description }}</p>
                                    </v-card-text>
                                </v-col>
                            </v-row>
                        </v-card>
                    </div>
                </section>

                <!-- ── 7. Hours of operation ─────────────────────────────────── -->
                <section v-if="weekHours.length > 0" class="mb-12">
                    <h2 class="text-h4 mb-4">Hours</h2>
                    <v-card variant="outlined" class="pa-4 hours-card">
                        <div v-for="day in weekHours" :key="day.key" class="hours-row">
                            <span class="hours-day">{{ day.label }}</span>
                            <span v-if="day.closed" class="text-medium-emphasis">Closed</span>
                            <span v-else>{{ day.open }} – {{ day.close }}</span>
                        </div>
                    </v-card>
                </section>

                <!-- ── 9. Sign up / log in strip ─────────────────────────────── -->
                <section v-if="!isAuthenticated" class="mb-12">
                    <v-card variant="tonal" color="primary" class="pa-6 text-center">
                        <h3 class="text-h5 mb-2">Have a season pass?</h3>
                        <p class="mb-4">Log in to check in at the gate, see your purchases, or buy more passes.</p>
                        <v-btn to="/Login" class="mr-2">Log In</v-btn>
                        <v-btn variant="outlined" to="/CreateAccount">Sign Up</v-btn>
                    </v-card>
                </section>

                <!-- Full-screen photo gallery slideshow. Triggered by clicking any
                     gallery thumbnail; arrows cycle through. Caption + counter overlay
                     at the bottom so the image owns the whole viewport. -->
                <v-dialog v-model="galleryDialog" fullscreen
                    transition="dialog-bottom-transition" :scrim="false">
                    <v-card class="gallery-fullscreen" color="black">
                        <v-toolbar density="compact" color="transparent" theme="dark" flat>
                            <v-toolbar-title v-if="gallery.length > 1" class="text-caption text-grey-lighten-2">
                                {{ galleryIndex + 1 }} / {{ gallery.length }}
                            </v-toolbar-title>
                            <v-spacer></v-spacer>
                            <v-btn icon="mdi-close" variant="text" color="white"
                                @click="galleryDialog = false"></v-btn>
                        </v-toolbar>
                        <v-carousel v-model="galleryIndex" hide-delimiters
                            :show-arrows="gallery.length > 1" continuous
                            class="flex-grow-1 gallery-carousel">
                            <v-carousel-item v-for="img in gallery" :key="img.id">
                                <div class="gallery-slide">
                                    <v-img :src="absoluteUrl(img.imageUrl)"
                                        height="100%" contain class="gallery-img"></v-img>
                                    <div v-if="img.caption" class="gallery-caption">
                                        <span class="gallery-caption-text">{{ img.caption }}</span>
                                    </div>
                                </div>
                            </v-carousel-item>
                        </v-carousel>
                    </v-card>
                </v-dialog>

                <!-- Footer is rendered globally in App.vue (see components/Footer.vue) so
                     the same address / contact / social / newsletter block shows on every page. -->
            </v-container>
        </template>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch, onMounted, onBeforeUnmount, nextTick } from 'vue'
import dayjs from 'dayjs'
import { branding } from '../stores/branding'
import { EventService, type EventDto } from '../services/EventService'
import { BlackoutService, type BlackoutDto } from '../services/BlackoutService'
import { PassService, type PassProduct } from '../services/PassService'
import { SeasonPassService, type SeasonPassProduct } from '../services/SeasonPassService'
import { TenantService, type GalleryImage, type TrackGraphic } from '../services/TenantService'
import tenantHelper from '../helpers/TenantHelper'
import authHelper from '../helpers/AuthHelper'
import BuyAdmissionFlow from '@/components/BuyAdmissionFlow.vue'

const isApex = computed(() => !tenantHelper.getSubdomain())
const isAuthenticated = computed(() => authHelper.isAuthenticated())

const apiUrl: string = (import.meta as any).env?.VITE_API_ENDPOINT ?? ''
function apiOrigin(): string {
    try { return new URL(apiUrl, window.location.origin).origin } catch { return '' }
}
function absoluteUrl(url: string | null | undefined): string {
    if (!url) return ''
    if (/^https?:\/\//i.test(url)) return url
    return `${apiOrigin()}${url}`
}

const eventService = new EventService()
const blackoutService = new BlackoutService()
const passService = new PassService()
const seasonPassService = new SeasonPassService()
const tenantService = new TenantService()

const events = ref<EventDto[]>([])
const blackouts = ref<BlackoutDto[]>([])
const passProducts = ref<PassProduct[]>([])
const seasonPassProducts = ref<SeasonPassProduct[]>([])
const gallery = ref<GalleryImage[]>([])
const trackGraphics = ref<TrackGraphic[]>([])

// Photo-gallery slideshow modal state. Set when a thumbnail is clicked.
const galleryDialog = ref(false)
const galleryIndex = ref(0)
function openGallery(index: number) {
    galleryIndex.value = index
    galleryDialog.value = true
}

// In-page Buy Admission dialog state
const buyDialog = ref(false)
const buyEvent = ref<EventDto | null>(null)
const buyKindFilter = ref<'spectator_pass' | 'race_entry' | null>(null)
const buyTitle = computed(() => {
    if (buyKindFilter.value === 'race_entry') return 'Buy Race Entry'
    if (buyKindFilter.value === 'spectator_pass') return 'Buy Ticket'
    return 'Buy Admission'
})

function openBuy(e: EventDto, kind: 'spectator_pass' | 'race_entry') {
    buyEvent.value = e
    buyKindFilter.value = kind
    buyDialog.value = true
}

function onBuyCompleted() {
    // Reload the event list so spots-left counters and sold-out states refresh.
    // Leave the dialog open — the user is looking at their QR code.
    void load()
}

async function load() {
    if (!branding.loaded || isApex.value) return
    const tz = branding.timezone || 'UTC'
    const fromUtc = dayjs().tz(tz).startOf('day').utc().toISOString()
    const toUtc = dayjs().tz(tz).startOf('day').add(60, 'day').utc().toISOString()
    try {
        const [e, b, dp, sp, gal, tg] = await Promise.all([
            eventService.list(fromUtc, toUtc),
            blackoutService.list(fromUtc, toUtc),
            passService.listActive().catch(() => ({ data: { data: [] as PassProduct[] } })),
            seasonPassService.listActive().catch(() => ({ data: { data: [] as SeasonPassProduct[] } })),
            tenantService.listGallery().catch(() => ({ data: { data: [] as GalleryImage[] } })),
            tenantService.listTrackGraphics().catch(() => ({ data: { data: [] as TrackGraphic[] } })),
        ])
        events.value = (e.data as any).data
        blackouts.value = (b.data as any).data
        passProducts.value = (dp.data as any).data
        seasonPassProducts.value = (sp.data as any).data
        gallery.value = (gal.data as any).data
        trackGraphics.value = (tg.data as any).data
    } catch (err) {
        console.error('Failed to load home page data', err)
    }
}

onMounted(load)
watch(() => branding.loaded, load)
watch(() => branding.timezone, load)

// ── Pricing snapshots ────────────────────────────────────────────────────────
const passFromCents = computed(() => {
    const prices = passProducts.value.map(p => p.priceCents).filter(p => p > 0)
    return prices.length > 0 ? Math.min(...prices) : null
})
const seasonPassFromCents = computed(() => {
    const prices = seasonPassProducts.value.map(p => p.priceCents).filter(p => p > 0)
    return prices.length > 0 ? Math.min(...prices) : null
})
// Drives the hero-area "Season Passes" CTA — hidden when the feature is off
// or when the tenant has none configured.
const hasSeasonPasses = computed(() =>
    branding.seasonPassesEnabled && seasonPassProducts.value.length > 0)

// ── Next-up events: closest 4 in time, filtered by configured event types ──
const nextUpTitle = computed(() => branding.homeNextUpTitle?.trim() || 'Next Up')
const nextEvents = computed(() => {
    const allowedTypeIds = branding.homeNextUpEventTypeIds
    const hasFilter = Array.isArray(allowedTypeIds) && allowedTypeIds.length > 0
    // Wider window — the carousel scrolls horizontally, so we want enough rows
    // for that to feel useful. Three viewport-fulls at 4-up = 12 covers most
    // tracks for a couple of months out without bloating the home page.
    return [...events.value]
        .filter(e => e.status !== 'cancelled')
        .filter(e => !hasFilter || allowedTypeIds!.includes(e.eventTypeId))
        .sort((a, b) => a.startsAtUtc.localeCompare(b.startsAtUtc))
        .slice(0, 12)
})

function hasCoverImage(e: EventDto): boolean {
    return !!(e.imageUrl ?? e.eventTypeImageUrl)
}

// ── Next-Up carousel ────────────────────────────────────────────────────────
// Native horizontal scroll-snap with chevron buttons. Buttons disable at the
// boundaries — no wrap-around — so a click never loops the rider back to event #1.
const nextUpTrack = ref<HTMLElement | null>(null)
const canScrollLeft = ref(false)
const canScrollRight = ref(false)

function updateNextUpScrollState() {
    const el = nextUpTrack.value
    if (!el) return
    // Tiny epsilon so subpixel rounding at exact boundaries doesn't flicker the buttons.
    canScrollLeft.value = el.scrollLeft > 4
    canScrollRight.value = el.scrollLeft + el.clientWidth < el.scrollWidth - 4
}

function scrollNextUp(direction: -1 | 1) {
    const el = nextUpTrack.value
    if (!el) return
    // Step by one card width (including the gap) so each click moves a clean unit.
    // Falls back to ~80% of the viewport width when no card has rendered yet.
    const card = el.querySelector('.next-up-card') as HTMLElement | null
    const step = card ? card.offsetWidth + 16 : el.clientWidth * 0.8
    el.scrollBy({ left: direction * step, behavior: 'smooth' })
}

// Refresh button state whenever the track size changes — initial mount, the
// nextEvents list updating, and viewport resize all matter.
function refreshNextUpStateSoon() {
    nextTick(() => updateNextUpScrollState())
}
onMounted(() => {
    refreshNextUpStateSoon()
    window.addEventListener('resize', updateNextUpScrollState)
})
onBeforeUnmount(() => {
    window.removeEventListener('resize', updateNextUpScrollState)
})
watch(() => nextEvents.value.length, refreshNextUpStateSoon)
function eventCoverStyle(e: EventDto) {
    // Fallback chain: per-event image → event-type default image → stylized title
    // over a subtle gradient on the event-type color.
    const img = e.imageUrl ?? e.eventTypeImageUrl ?? null
    if (img) {
        return {
            backgroundImage: `url(${absoluteUrl(img)})`,
            backgroundColor: e.eventTypeColor || '#1976D2',
        }
    }
    const base = e.eventTypeColor || '#1976D2'
    // Diagonal gradient: full color → 25% darker overlay (rgba black) so the typography
    // pops without us having to compute a second color from the hex.
    return {
        backgroundColor: base,
        backgroundImage: `linear-gradient(135deg, rgba(255,255,255,0.06) 0%, rgba(0,0,0,0.32) 100%)`,
    }
}

function formatEventDate(e: EventDto): string {
    const tz = branding.timezone || 'UTC'
    const start = dayjs.utc(e.startsAtUtc).tz(tz)
    return e.allDay
        ? start.format('ddd, MMM D')
        : `${start.format('ddd, MMM D')} · ${start.format('h:mm A')}`
}

// ── Daily status (manual + blackout override) ───────────────────────────────
type StatusInfo = { label: string; message: string; tone: 'open' | 'closed' | 'caution'; icon: string; color: string }

const effectiveStatus = computed<StatusInfo | null>(() => {
    const tz = branding.timezone || 'UTC'
    const todayKey = dayjs().tz(tz).format('YYYY-MM-DD')
    // Blackout always wins.
    const todayBlackout = blackouts.value.find(b =>
        dayjs.utc(b.startsAtUtc).tz(tz).format('YYYY-MM-DD') === todayKey
    )
    if (todayBlackout) {
        return {
            label: 'Closed today',
            message: todayBlackout.reason || '',
            tone: 'closed', icon: 'mdi-close-circle', color: 'error',
        }
    }
    // Manual status: only honored if posted within the last 24h.
    if (branding.dailyStatusOpen !== null && branding.dailyStatusUpdatedAt) {
        const ageHours = dayjs().diff(dayjs(branding.dailyStatusUpdatedAt), 'hour')
        if (ageHours <= 24) {
            return branding.dailyStatusOpen
                ? { label: 'Open today', message: branding.dailyStatusMessage ?? '', tone: 'open', icon: 'mdi-check-circle', color: 'success' }
                : { label: 'Closed today', message: branding.dailyStatusMessage ?? '', tone: 'closed', icon: 'mdi-close-circle', color: 'error' }
        }
    }
    return null
})

// ── Hours of operation ──────────────────────────────────────────────────────
type DayHours = { closed: boolean; open: string; close: string }
const dayLabels: { key: string; label: string }[] = [
    { key: 'mon', label: 'Monday' }, { key: 'tue', label: 'Tuesday' },
    { key: 'wed', label: 'Wednesday' }, { key: 'thu', label: 'Thursday' },
    { key: 'fri', label: 'Friday' }, { key: 'sat', label: 'Saturday' },
    { key: 'sun', label: 'Sunday' },
]
const weekHours = computed(() => {
    if (!branding.hoursJson) return []
    try {
        const parsed = JSON.parse(branding.hoursJson) as Record<string, DayHours>
        const entries = dayLabels.map(d => {
            const v = parsed[d.key]
            return {
                key: d.key, label: d.label,
                closed: v?.closed ?? false,
                open: formatTime12(v?.open ?? '09:00'),
                close: formatTime12(v?.close ?? '17:00'),
            }
        })
        // Hide entirely if hours_json was never configured (all defaults & not closed)
        const looksConfigured = Object.keys(parsed).length > 0
        return looksConfigured ? entries : []
    } catch {
        return []
    }
})

function formatTime12(hhmm: string): string {
    const [h, m] = hhmm.split(':').map(Number)
    if (Number.isNaN(h) || Number.isNaN(m)) return hhmm
    const period = h >= 12 ? 'PM' : 'AM'
    const h12 = h % 12 === 0 ? 12 : h % 12
    return `${h12}:${m.toString().padStart(2, '0')} ${period}`
}

</script>

<style scoped>
.hero {
    position: relative;
    height: 60vh;
    min-height: 360px;
    background-size: cover;
    background-position: center;
    display: flex;
    align-items: center;
    justify-content: center;
}
.hero-overlay {
    background: rgba(0, 0, 0, 0.45);
    padding: 2rem 3rem;
    border-radius: 8px;
    max-width: 90%;
}
.status-badge {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 6px 14px;
    border-radius: 999px;
    font-size: 14px;
    color: white;
}
.status-open { background: rgba(76, 175, 80, 0.9); }
.status-closed { background: rgba(244, 67, 54, 0.9); }
.status-caution { background: rgba(255, 152, 0, 0.9); }

.event-card {
    text-decoration: none;
    transition: transform 0.15s ease;
    height: 100%;
}
.event-card:hover { transform: translateY(-2px); }

/* Carousel wrapper hosts the absolutely-positioned chevrons. The track itself
   handles the actual scroll; the arrows sit over the left + right edges,
   vertically centered, and only render when scroll is actually possible
   that direction (v-show on the buttons). */
.next-up-wrap {
    position: relative;
}
.next-up-arrow {
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
    transition: background-color 0.15s ease, transform 0.15s ease;
}
.next-up-arrow:hover {
    background: #fff;
    transform: translateY(-50%) scale(1.05);
}
.next-up-arrow:active {
    transform: translateY(-50%) scale(0.97);
}
.next-up-arrow--left  { left: -8px; }
.next-up-arrow--right { right: -8px; }
@media (max-width: 600px) {
    /* Tuck the arrows just inside the viewport on phones so they don't push
       past the container's content padding. */
    .next-up-arrow--left  { left: 4px; }
    .next-up-arrow--right { right: 4px; }
}

/* Horizontal carousel track. Native overflow-x scroll keeps touch + keyboard
   accessibility free; the chevron buttons just call scrollBy() with smooth
   behavior. Scroll-snap-x makes each click land on a card boundary. The
   per-card width breaks at the same breakpoints the old v-row used (xs/sm/md/lg)
   so the visual density stays consistent with the rest of the page. */
.next-up-track {
    display: flex;
    gap: 16px;
    overflow-x: auto;
    overflow-y: hidden;
    scroll-snap-type: x mandatory;
    scroll-behavior: smooth;
    padding-bottom: 4px;          /* room for hover translate without clipping */
    /* Hide the native scrollbar — chevrons are the affordance. */
    scrollbar-width: none;
    -ms-overflow-style: none;
}
.next-up-track::-webkit-scrollbar {
    display: none;
}
.next-up-card {
    flex: 0 0 auto;
    scroll-snap-align: start;
    /* lg and up: 4 cards, 3 gaps of 16px = 48px total gap. */
    width: calc((100% - 48px) / 4);
}
@media (max-width: 1264px) {  /* < lg → 3 up */
    .next-up-card { width: calc((100% - 32px) / 3); }
}
@media (max-width: 960px) {   /* < md → 2 up */
    .next-up-card { width: calc((100% - 16px) / 2); }
}
@media (max-width: 600px) {   /* < sm → 1 up, lean a bit so the next card peeks */
    .next-up-card { width: 88%; }
}
.event-cover {
    height: 140px;
    background-size: cover;
    background-position: center;
    position: relative;
    flex-shrink: 0;
}
/* Stylized-title cover (no image fallback). The chip stays pinned top-left;
   the title takes the rest of the card with a tracked-out, drop-shadowed look
   so a flat background reads as intentional design rather than missing media. */
.event-cover--text {
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 16px 18px;
    overflow: hidden;
}
.event-cover-title {
    color: #fff;
    font-weight: 800;
    font-size: 22px;
    line-height: 1.15;
    letter-spacing: 0.01em;
    text-align: center;
    text-shadow: 0 2px 6px rgba(0, 0, 0, 0.35);
    /* Clamp to 3 lines so a marathon-long title doesn't blow past the chip. */
    display: -webkit-box;
    -webkit-line-clamp: 3;
    -webkit-box-orient: vertical;
    overflow: hidden;
    width: 100%;
}
/* The chip uses a white background regardless of theme so it pops against the event
   cover image; pin the foreground color too so dark-mode (where chip text would
   otherwise be white) doesn't render white-on-white. */
.event-chip {
    position: absolute;
    top: 8px;
    left: 8px;
    background: rgba(255, 255, 255, 0.92) !important;
    color: rgba(0, 0, 0, 0.87) !important;
}
:deep(.event-chip .v-chip__content) {
    color: rgba(0, 0, 0, 0.87) !important;
}

.hours-card .hours-row {
    display: flex;
    justify-content: space-between;
    padding: 6px 0;
    border-bottom: 1px solid rgba(0, 0, 0, 0.06);
}
.hours-card .hours-row:last-child { border-bottom: none; }
.hours-card .hours-day { font-weight: 500; }

.footer a {
    color: inherit;
    text-decoration: none;
}
.footer a:hover { text-decoration: underline; }

.rich-text-body :deep(*) { max-width: 100%; }

/* Gallery thumbnails — click cue + subtle hover lift. */
.gallery-thumb {
    cursor: pointer;
    transition: transform 150ms ease, box-shadow 150ms ease;
}
.gallery-thumb:hover {
    transform: translateY(-2px);
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

/* Full-screen slideshow — black background, carousel + slide expand to fill the
   viewport (toolbar is the only fixed-height element). The image is `contain`
   so portrait/landscape both fit without cropping. */
.gallery-fullscreen {
    display: flex;
    flex-direction: column;
    height: 100vh;
}
.gallery-carousel,
.gallery-carousel :deep(.v-window),
.gallery-carousel :deep(.v-window__container),
.gallery-carousel :deep(.v-carousel-item) {
    height: 100% !important;
}
.gallery-slide {
    position: relative;
    background: #000;
    width: 100%;
    height: 100%;
}
.gallery-img,
.gallery-img :deep(.v-img__img) {
    height: 100%;
    width: 100%;
}
/* Caption sits in a flex container so the inner pill stays centered + only as
   wide as the text. The pill itself carries the dark backdrop so light images
   don't wash the words out. */
.gallery-caption {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    padding: 0 24px 32px;
    display: flex;
    justify-content: center;
    pointer-events: none;
}
.gallery-caption-text {
    display: inline-block;
    max-width: min(900px, 90%);
    padding: 12px 20px;
    background: rgba(0, 0, 0, 0.65);
    color: #fff;
    border-radius: 8px;
    font-size: 15px;
    line-height: 1.5;
    text-align: center;
}
</style>
