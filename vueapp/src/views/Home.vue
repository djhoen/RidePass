<template>
    <div>
        <!-- Apex (ridepass.io, no tenant subdomain): platform landing page.
             Purpose: promote the platform's tracks and their events to riders,
             spotlight a rider, and promote RidePass to prospective track
             operators (bottom CTA -> /ForTracks). Copy, hero image, featured
             tracks, benefits, testimonials, and the bottom CTA banner are all
             pulled from platform_branding so super admins can edit without a
             deploy. The map section is a stub for now; a real map ships later. -->
        <div v-if="isApex" class="apex-page">
            <!-- ── HERO ────────────────────────────────────────────────────── -->
            <section class="apex-hero" :style="heroStyle">
                <div class="apex-hero-overlay">
                    <v-container class="text-left">
                        <v-row justify="start">
                            <v-col cols="12" md="8" lg="7" class="text-left">
                                <h1 class="text-h1 mb-3 font-display apex-hero-headline text-left">
                                    <span v-for="(line, i) in apexHeroLines" :key="i"
                                        class="apex-hero-line"
                                        :class="i === 0 ? 'text-white' : 'text-primary'">
                                        {{ line }}
                                    </span>
                                </h1>
                                <p class="text-h6 text-white mb-8 text-left" style="max-width: 600px">
                                    {{ apexHeroSubhead }}
                                </p>
                                <div class="d-flex flex-wrap ga-3 mb-10 justify-start">
                                    <v-btn v-if="apexHeroPrimary"
                                        color="primary" size="x-large"
                                        :to="apexHeroPrimary.url">{{ apexHeroPrimary.label }}</v-btn>
                                    <v-btn v-if="apexHeroSecondary"
                                        variant="outlined" size="x-large" color="white"
                                        :to="apexHeroSecondary.url">{{ apexHeroSecondary.label }}</v-btn>
                                </div>
                                <div class="apex-stats">
                                    <div v-if="apexStatsShowTracks" class="apex-stat">
                                        <v-icon icon="mdi-map-marker-multiple" size="28" class="apex-stat-icon"></v-icon>
                                        <div>
                                            <div class="apex-stat-num">{{ apexTrackCount }}+</div>
                                            <div class="apex-stat-label">Tracks</div>
                                        </div>
                                    </div>
                                    <div v-if="apexStatsShowEventDays" class="apex-stat">
                                        <v-icon icon="mdi-calendar-check" size="28" class="apex-stat-icon"></v-icon>
                                        <div>
                                            <div class="apex-stat-num">{{ apexEventDayCount }}+</div>
                                            <div class="apex-stat-label">Event days</div>
                                        </div>
                                    </div>
                                </div>
                            </v-col>
                        </v-row>
                    </v-container>
                </div>
            </section>

            <v-container>
                <!-- ── UPCOMING EVENTS (promoted to full width, first) ──────
                     Events are the most time-sensitive content, so they lead
                     the page. Cards deep-link to the event on its tenant. -->
                <section class="my-12">
                    <div class="d-flex align-center mb-4 ga-2">
                        <h2 class="text-h4 font-weight-bold font-display">{{ apexSectionTitle('events', 'Upcoming events') }}</h2>
                        <v-spacer></v-spacer>
                        <v-btn variant="text" color="primary" to="/Events" append-icon="mdi-arrow-right">View all events</v-btn>
                    </div>
                    <v-row v-if="apexEvents.length > 0" dense>
                        <v-col v-for="e in apexEvents" :key="e.eventId" cols="12" sm="6" md="3">
                            <v-card class="h-100 apex-event-card"
                                :href="apexEventUrl(e)" rel="noopener">
                                <!-- Cover image with the date badge floated in the top-left.
                                     Falls back to event type color when no image is set. -->
                                <div class="apex-event-image"
                                    :style="apexEventImageStyle(e)">
                                    <div class="apex-event-datebadge">
                                        <div class="apex-event-day">{{ formatApexEventDay(e.startsAtUtc) }}</div>
                                        <div class="apex-event-month">{{ formatApexEventMonth(e.startsAtUtc) }}</div>
                                    </div>
                                    <img v-if="e.tenantLogoUrl" class="apex-event-logo"
                                        :src="absoluteUrl(e.tenantLogoUrl)" :alt="e.tenantDisplayName" />
                                </div>
                                <v-card-text class="pa-4">
                                    <div class="text-h6 font-display-upright mb-1">{{ e.title }}</div>
                                    <div class="text-caption text-medium-emphasis d-flex align-center ga-1">
                                        <v-icon icon="mdi-map-marker" size="14"></v-icon>
                                        <span>
                                            {{ e.tenantDisplayName }}<span v-if="e.tenantCity">, {{ e.tenantCity }}<span v-if="e.tenantRegion">, {{ e.tenantRegion }}</span></span>
                                        </span>
                                    </div>
                                </v-card-text>
                            </v-card>
                        </v-col>
                    </v-row>
                    <v-card v-else variant="outlined">
                        <v-card-text class="text-center text-medium-emphasis py-8">
                            No upcoming events scheduled.
                        </v-card-text>
                    </v-card>
                </section>

                <!-- ── RIDE THE BEST TRACKS ────────────────────────────────── -->
                <section class="my-12">
                    <div class="d-flex align-center mb-4 ga-2">
                        <h2 class="text-h4 font-weight-bold font-display">{{ apexSectionTitle('tracks', 'Ride the best tracks') }}</h2>
                        <v-spacer></v-spacer>
                        <v-btn variant="text" color="primary" to="/Discover" append-icon="mdi-arrow-right">See all tracks</v-btn>
                    </div>
                    <TrackCardGrid :tracks="apexFeaturedTracks" empty-text="No tracks on the platform yet." />
                </section>

                <!-- The "Why Tracks love RidePass" benefits band moved to the For
                     Tracks page (/ForTracks), edited under Super Admin -> For Tracks. -->

                <!-- ── TRACKS NEAR YOU (lower-48 map, auto-pinned from lat/lng) ── -->
                <section class="my-12">
                    <div class="d-flex align-center mb-4 ga-2">
                        <h2 class="text-h4 font-weight-bold font-display">{{ apexSectionTitle('tracksNearYou', 'Tracks near you') }}</h2>
                        <v-spacer></v-spacer>
                        <v-btn variant="text" color="primary" to="/Discover" append-icon="mdi-arrow-right">Browse all tracks</v-btn>
                    </div>
                    <!-- overflow visible so the hover preview card can extend past the
                         map's border instead of being clipped. -->
                    <v-card variant="outlined" class="pa-4" style="overflow: visible">
                        <TracksMap :tracks="apexTracks" @select="openTrack" />
                    </v-card>
                </section>

                <!-- ── TESTIMONIALS (kept last on the apex home) ───────────────── -->
                <section v-if="apexTestimonials.length > 0" class="my-12">
                    <h2 class="text-h4 font-weight-bold text-center mb-6 font-display">
                        {{ apexSectionTitle('testimonials', 'What riders are saying') }}
                    </h2>
                    <v-row dense>
                        <v-col v-for="t in apexTestimonials" :key="t.id" cols="12" md="6">
                            <v-card class="h-100">
                                <v-card-text>
                                    <div class="d-flex align-center mb-3 ga-3">
                                        <v-avatar size="48" color="grey-lighten-3">
                                            <v-img v-if="t.riderPhotoUrl" :src="testimonialPhoto(t.riderPhotoUrl)"></v-img>
                                            <v-icon v-else>mdi-account</v-icon>
                                        </v-avatar>
                                        <div>
                                            <div class="font-weight-medium">{{ t.riderName }}</div>
                                            <div class="text-caption text-warning">
                                                <span v-for="n in t.rating" :key="n">★</span>
                                            </div>
                                        </div>
                                    </div>
                                    <p class="text-body-1 font-italic">"{{ t.quote }}"</p>
                                </v-card-text>
                            </v-card>
                        </v-col>
                    </v-row>
                </section>
            </v-container>

        </div>

        <template v-else>
            <!-- ── 1. Hero (apex-style: full-bleed photo, left-aligned, keeps the
                 live track-status badge). Always shown. ──────────────────────── -->
            <section class="apex-hero" :style="tenantHeroStyle">
                <div class="apex-hero-overlay">
                    <v-container>
                        <v-row>
                            <v-col cols="12" md="8" lg="7">
                                <h1 class="text-h2 text-md-h1 mb-3 font-display apex-hero-headline text-left text-white">
                                    {{ branding.displayName }}
                                </h1>
                                <p v-if="branding.tagline" class="text-h6 text-white mb-5 hero-subhead">
                                    {{ branding.tagline }}
                                </p>
                                <div v-if="effectiveStatus" class="status-badge mb-6" :class="`status-${effectiveStatus.tone}`">
                                    <v-icon size="small">{{ effectiveStatus.icon }}</v-icon>
                                    <strong>{{ effectiveStatus.label }}</strong>
                                    <span v-if="effectiveStatus.message" class="ml-2 text-body-2">— {{ effectiveStatus.message }}</span>
                                </div>
                                <div class="d-flex flex-wrap ga-3">
                                    <v-btn color="primary" size="x-large" to="/Events">See Events</v-btn>
                                    <v-btn v-if="hasSeasonPasses" variant="outlined" size="x-large" color="white"
                                        to="/SeasonPasses">Season Passes</v-btn>
                                </div>
                            </v-col>
                        </v-row>
                    </v-container>
                </div>
            </section>

            <v-container>
                <!-- ── 2. Next events row ──────────────────────────────────────── -->
                <!-- Horizontal scroll-snap track. Chevrons overlay the track edges
                     like a typical carousel — only visible while scrolling is possible
                     in that direction. Native scroll keeps touch + keyboard
                     accessibility free; we just script the buttons. -->
                <section v-if="sectionVisible('nextEvents') && nextEvents.length > 0" class="mb-12">
                    <div class="d-flex align-center mb-4 ga-2">
                        <h2 class="text-h4 font-weight-bold font-display">{{ nextUpTitle }}</h2>
                        <v-spacer></v-spacer>
                        <v-btn variant="text" color="primary" to="/Events" append-icon="mdi-arrow-right">All events</v-btn>
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
                                <!-- Whole card links to the event landing page. The Race Entry button
                                     stops propagation so it opens the buy dialog instead of navigating. -->
                                <v-card class="apex-event-card d-flex flex-column" style="height: 100%"
                                    :to="`/Event/${e.id}`">
                                <!-- Cover image with the black day/month date badge floated over the
                                     top-left corner (matches the apex home cards); chip moves right. -->
                                <div class="apex-event-image" :style="eventCoverStyle(e)">
                                    <div class="apex-event-datebadge">
                                        <div class="apex-event-day">{{ eventDayBadge(e) }}</div>
                                        <div class="apex-event-month">{{ eventMonthBadge(e) }}</div>
                                    </div>
                                    <v-chip size="small" class="event-chip event-chip--right">{{ e.eventTypeName }}</v-chip>
                                </div>
                                <v-card-text class="pa-4 flex-grow-1 d-flex flex-column">
                                    <div class="text-h6 font-display-upright mb-1">{{ e.title }}</div>
                                    <div class="text-caption text-medium-emphasis">{{ formatEventDate(e) }}</div>
                                    <div v-if="e.minTicketPriceCents" class="text-body-2 mt-2">
                                        From <strong>${{ (e.minTicketPriceCents / 100).toFixed(2) }}</strong>
                                    </div>
                                    <div v-if="e.hasRaceEntryTiers" class="d-flex flex-wrap ga-2 mt-auto pt-3">
                                        <v-btn size="small" color="deep-orange" :to="`/Event/${e.id}`">
                                            Race Entry
                                        </v-btn>
                                    </div>
                                </v-card-text>
                            </v-card>
                        </div>
                        </div>
                    </div>
                </section>

                <!-- Featured blog post: contained card under the events row, with a Read
                     more CTA. Shown only when the blog is on and a post is featured. -->
                <section v-if="branding.blogEnabled && featuredPost" class="featured-blog my-12">
                    <div class="featured-blog-grid">
                        <div class="featured-blog-media"
                            :style="featuredPost.mainImageUrl ? { backgroundImage: `url(${absoluteUrl(featuredPost.mainImageUrl)})` } : {}">
                        </div>
                        <div class="featured-blog-content">
                            <div class="text-overline text-primary mb-1">From the blog</div>
                            <h2 class="text-h4 text-md-h3 font-display mb-3">{{ featuredPost.title }}</h2>
                            <p v-if="featuredPost.excerpt" class="text-body-1 mb-6 featured-blog-excerpt">
                                {{ featuredPost.excerpt }}
                            </p>
                            <div>
                                <v-btn color="primary" size="large" :to="`/Blog/${featuredPost.slug}`"
                                    append-icon="mdi-arrow-right">Read more</v-btn>
                            </div>
                        </div>
                    </div>
                </section>

                <!-- ── 3. Pricing snapshot ─────────────────────────────────────── -->
                <section v-if="sectionVisible('passes') && seasonPassFromCents !== null" class="my-12">
                    <h2 class="text-h4 font-weight-bold font-display mb-4">Passes</h2>
                    <v-row>
                        <v-col v-if="seasonPassFromCents !== null && branding.seasonPassesEnabled" cols="12" md="6">
                            <v-card class="pa-6" variant="outlined">
                                <h3 class="text-h5 mb-2">Season Pass</h3>
                                <p class="text-body-2 text-medium-emphasis mb-4">Ride the whole season — best value.</p>
                                <div class="text-h4 font-weight-bold mb-4 font-display">
                                    From ${{ (seasonPassFromCents / 100).toFixed(2) }}
                                </div>
                                <v-btn color="primary" to="/SeasonPasses">See Season Passes</v-btn>
                            </v-card>
                        </v-col>
                    </v-row>
                </section>

                <!-- ── Benefits (apex dark band: photo left, checkmark perks right) ── -->
                <section v-if="sectionVisible('benefits') && (branding.benefitsHtml || branding.benefitsImageUrl)"
                    class="apex-benefits my-12">
                    <v-row no-gutters>
                        <v-col cols="12" md="4">
                            <div v-if="branding.benefitsImageUrl" class="apex-benefits-photo"
                                :style="{ backgroundImage: `url(${branding.benefitsImageUrl})` }"></div>
                            <div v-else class="apex-benefits-photo apex-benefits-photo--placeholder"></div>
                        </v-col>
                        <v-col cols="12" md="8" class="apex-benefits-content">
                            <h2 class="text-h4 mb-4 font-display">Why ride with {{ branding.displayName }}</h2>
                            <div v-if="branding.benefitsHtml" v-html="benefitsHtmlSafe"></div>
                        </v-col>
                    </v-row>
                </section>

                <!-- ── 4. About ──────────────────────────────────────────────── -->
                <section v-if="sectionVisible('about') && branding.aboutHtml" class="my-12">
                    <h2 class="text-h4 font-weight-bold font-display mb-4">About</h2>
                    <div class="rich-text-body" v-html="aboutHtmlSafe"></div>
                    <v-img v-if="branding.secondaryHeroUrl" :src="branding.secondaryHeroUrl"
                        max-height="400" cover class="mt-6 rounded"></v-img>
                </section>

                <!-- ── 5. Photo gallery ──────────────────────────────────────── -->
                <section v-if="sectionVisible('gallery') && gallery.length > 0" class="my-12">
                    <h2 class="text-h4 font-weight-bold font-display mb-4">Photos</h2>
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
                <section v-if="sectionVisible('trackLayout') && trackGraphics.length > 0" class="my-12">
                    <h2 class="text-h4 font-weight-bold font-display mb-4">Track Layout</h2>
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
                <section v-if="sectionVisible('hours') && weekHours.length > 0" class="my-12">
                    <h2 class="text-h4 font-weight-bold font-display mb-4">Hours</h2>
                    <v-card variant="outlined" class="pa-4 hours-card">
                        <div v-for="day in weekHours" :key="day.key" class="hours-row">
                            <span class="hours-day">{{ day.label }}</span>
                            <span v-if="day.closed" class="text-medium-emphasis">Closed</span>
                            <span v-else>{{ day.open }} – {{ day.close }}</span>
                        </div>
                    </v-card>
                </section>

                <!-- ── 9. Sign up / log in strip ─────────────────────────────── -->
                <section v-if="sectionVisible('signup') && !isAuthenticated" class="mb-12">
                    <v-card variant="tonal" color="primary" class="pa-6 text-center">
                        <h3 class="text-h5 mb-2">Already have an account?</h3>
                        <p class="mb-4">Log in to check in at the gate, see your purchases, or buy more passes. New here? Your account is created automatically at your first purchase.</p>
                        <v-btn to="/Login">Log In</v-btn>
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

        <v-snackbar :model-value="!!loadError" @update:model-value="loadError = ''"
            color="error" location="top" :timeout="6000">{{ loadError }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch, onMounted, onBeforeUnmount, nextTick } from 'vue'
import dayjs from 'dayjs'
import { branding } from '../stores/branding'
import { EventService, type EventDto } from '../services/EventService'
import { BlackoutService, type BlackoutDto } from '../services/BlackoutService'
import { SeasonPassService, type SeasonPassProduct } from '../services/SeasonPassService'
import { TenantService, type GalleryImage, type TrackGraphic } from '../services/TenantService'
import { DiscoverService, type TrackDiscoverItem, type EventDiscoverItem } from '../services/DiscoverService'
import { BlogService, type BlogPostDetail } from '../services/BlogService'
import { platformBranding, platformImageUrl } from '../stores/platformBranding'
import tenantHelper from '../helpers/TenantHelper'
import authHelper from '../helpers/AuthHelper'
import TracksMap from '@/components/TracksMap.vue'
import TrackCardGrid from '@/components/TrackCardGrid.vue'
import { tenantHomeUrl, tenantEventUrl } from '@/helpers/tenantLinks'
import DOMPurify from 'dompurify'

const isApex = computed(() => !tenantHelper.getSubdomain())
const isAuthenticated = computed(() => authHelper.isAuthenticated())

// Admin-authored "About" copy is rendered as HTML, so sanitize it before it
// hits v-html. The wrapper keeps the rich-text-body class for its layout rules.
const aboutHtmlSafe = computed(() => DOMPurify.sanitize(branding.aboutHtml ?? ''))
const benefitsHtmlSafe = computed(() => DOMPurify.sanitize(branding.benefitsHtml ?? ''))

// Tenant hero background (reuses the apex hero styling). With no hero photo we
// paint a brand-colored diagonal gradient instead of a flat block so the band
// still looks intentional.
const tenantHeroStyle = computed(() =>
    branding.heroImageUrl
        ? { backgroundImage: `url(${branding.heroImageUrl})` }
        : { backgroundImage: 'linear-gradient(135deg, rgb(var(--v-theme-primary)), rgb(var(--v-theme-secondary)))' })

// A non-hero section is visible unless its key is explicitly toggled off.
function sectionVisible(key: string): boolean {
    return branding.homeSections[key] !== false
}

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
const seasonPassService = new SeasonPassService()
const tenantService = new TenantService()
const discoverService = new DiscoverService()
const blogService = new BlogService()

const featuredPost = ref<BlogPostDetail | null>(null)
const events = ref<EventDto[]>([])
const blackouts = ref<BlackoutDto[]>([])
const seasonPassProducts = ref<SeasonPassProduct[]>([])
const gallery = ref<GalleryImage[]>([])
const trackGraphics = ref<TrackGraphic[]>([])

// Apex landing data: tracks and upcoming events across the whole platform.
// Loaded only when there's no tenant subdomain.
const apexTracks = ref<TrackDiscoverItem[]>([])
const apexEvents = ref<EventDiscoverItem[]>([])

// Featured tracks render 3; upcoming events lead the page full-width as a
// single row of up to 4 (four across on desktop).
const APEX_TRACK_LIMIT = 3
const APEX_EVENT_LIMIT = 4

// Surfaced to the rider via a top snackbar when a page load fails, so a network
// error shows a real message instead of a blank home / apex page.
const loadError = ref('')

// Photo-gallery slideshow modal state. Set when a thumbnail is clicked.
const galleryDialog = ref(false)
const galleryIndex = ref(0)
function openGallery(index: number) {
    galleryIndex.value = index
    galleryDialog.value = true
}

async function load() {
    if (!branding.loaded || isApex.value) return
    const tz = branding.timezone || 'UTC'
    const fromUtc = dayjs().tz(tz).startOf('day').utc().toISOString()
    const toUtc = dayjs().tz(tz).startOf('day').add(60, 'day').utc().toISOString()
    try {
        const [e, b, sp, gal, tg] = await Promise.all([
            eventService.list(fromUtc, toUtc),
            blackoutService.list(fromUtc, toUtc),
            seasonPassService.listActive().catch(() => ({ data: { data: [] as SeasonPassProduct[] } })),
            tenantService.listGallery().catch(() => ({ data: { data: [] as GalleryImage[] } })),
            tenantService.listTrackGraphics().catch(() => ({ data: { data: [] as TrackGraphic[] } })),
        ])
        events.value = (e.data as any).data
        blackouts.value = (b.data as any).data
        seasonPassProducts.value = (sp.data as any).data
        gallery.value = (gal.data as any).data
        trackGraphics.value = (tg.data as any).data
        // Featured blog post for the home-page band (only when the blog is on).
        if (branding.blogEnabled) {
            try {
                const f = await blogService.getFeatured()
                featuredPost.value = (f.data as any).data
            } catch { featuredPost.value = null }
        } else {
            featuredPost.value = null
        }
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            || 'Could not load this page. Refresh to try again, or check your connection.'
    }
}

onMounted(load)
watch(() => branding.loaded, load)
watch(() => branding.timezone, load)

// ── Apex (no tenant) loader ─────────────────────────────────────────────────
async function loadApex() {
    if (!isApex.value) return
    try {
        const [trackResp, eventResp] = await Promise.all([
            discoverService.searchTracks({}),
            discoverService.searchEvents({ fromUtc: new Date().toISOString() }),
        ])
        apexTracks.value = (trackResp.data as any).data
        apexEvents.value = (eventResp.data as any).data.slice(0, APEX_EVENT_LIMIT)
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            || 'Could not load tracks and events. Refresh to try again, or check your connection.'
    }
}
onMounted(loadApex)

// ── Apex computed (all driven by platform_branding) ─────────────────────────
const apexHeroHeadline = computed(() =>
    platformBranding.data?.heroHeadline?.trim() || 'Find your track. Ride this weekend.')
const apexHeroSubhead = computed(() =>
    platformBranding.data?.heroSubhead?.trim()
    || 'Discover motocross tracks near you, see what is on the schedule, and grab your gate pass before you load the van.')

// Split the headline on sentence boundaries so each clause renders on its
// own line and gets its own color (mockup: "RIDE MORE." white,
// "PAY LESS." orange). When the admin enters a single sentence, only one
// line renders. Periods dropped by the split are re-added so the rendered
// text matches what the admin typed.
const apexHeroLines = computed<string[]>(() => {
    const raw = apexHeroHeadline.value.trim()
    if (!raw) return []
    const parts = raw.split(/\.\s+/).map(p => p.trim()).filter(Boolean)
    return parts.map((p, i) => {
        if (i === parts.length - 1) return p
        return p.endsWith('.') ? p : p + '.'
    })
})
const apexHeroPrimary = computed(() => buttonFrom(
    platformBranding.data?.heroCtaPrimaryLabel,
    platformBranding.data?.heroCtaPrimaryUrl))
const apexHeroSecondary = computed(() => buttonFrom(
    platformBranding.data?.heroCtaSecondaryLabel,
    platformBranding.data?.heroCtaSecondaryUrl))

const apexStatsShowTracks = computed(() => platformBranding.data?.statsShowTracks !== false)
const apexStatsShowEventDays = computed(() => platformBranding.data?.statsShowEventDays !== false)

// Stats counts use real numbers from /Discover. Pad the displayed value so
// admins don't see a misleading "0+" before tracks load.
const apexTrackCount = computed(() => apexTracks.value.length)
const apexEventDayCount = computed(() =>
    apexTracks.value.reduce((sum, t) => sum + (t.upcomingEventsCount || 0), 0))

const heroStyle = computed(() => {
    const url = platformImageUrl(platformBranding.data?.heroImageUrl)
    return url ? { backgroundImage: `url(${url})` } : {}
})

// Featured tracks: use the admin's curated list when set, otherwise auto-pick
// by upcoming event count. Limit display to 3 (mockup).
const apexFeaturedTracks = computed(() => {
    const all = apexTracks.value
    const featured = platformBranding.data?.featuredTrackIds ?? []
    if (featured.length > 0) {
        const byId = new Map(all.map(t => [t.tenantId, t]))
        const picks: TrackDiscoverItem[] = []
        for (const id of featured) {
            const t = byId.get(id)
            if (t) picks.push(t)
            if (picks.length >= APEX_TRACK_LIMIT) break
        }
        return picks
    }
    return [...all]
        .sort((a, b) => (b.upcomingEventsCount ?? 0) - (a.upcomingEventsCount ?? 0))
        .slice(0, APEX_TRACK_LIMIT)
})

const apexTestimonials = computed(() => platformBranding.data?.testimonials ?? [])

function apexSectionTitle(key: 'tracks' | 'events' | 'benefits' | 'testimonials' | 'tracksNearYou', fallback: string): string {
    const b = platformBranding.data
    if (!b) return fallback
    const map: Record<typeof key, string | null | undefined> = {
        tracks: b.sectionTracksTitle,
        events: b.sectionEventsTitle,
        benefits: b.sectionBenefitsTitle,
        testimonials: b.sectionTestimonialsTitle,
        tracksNearYou: b.sectionTracksNearYouTitle,
    }
    return map[key]?.trim() || fallback
}

function buttonFrom(label: string | null | undefined, url: string | null | undefined): { label: string; url: string } | null {
    const l = label?.trim()
    const u = url?.trim()
    if (!l || !u) return null
    return { label: l, url: u }
}

function testimonialPhoto(url: string | null | undefined): string {
    return platformImageUrl(url) ?? ''
}

// Build the absolute URL to a tenant subdomain. tenantHelper.rootDomain()
// returns the configured root (ridepass.io in prod, ridepass.local in dev),
// so this works the same in both environments.
function tenantUrl(subdomain: string): string {
    const proto = window.location.protocol
    const port = window.location.port ? `:${window.location.port}` : ''
    return `${proto}//${subdomain}.${tenantHelper.rootDomain()}${port}/`
}

// Map pin click -> open that track's public home (client-type-aware).
function openTrack(t: TrackDiscoverItem) {
    window.location.href = tenantHomeUrl(t)
}

// Date-badge helpers for the upcoming-events list (day number + month abbrev,
// to match the mockup's "12 / OCT" stacked format).
function formatApexEventDay(utc: string): string {
    return dayjs.utc(utc).local().format('D')
}
function formatApexEventMonth(utc: string): string {
    return dayjs.utc(utc).local().format('MMM').toUpperCase()
}
// Tenant event-card date badge (day / month) in the tenant's timezone, matching
// the rest of the tenant page's date display.
function eventDayBadge(e: EventDto): string {
    return dayjs.utc(e.startsAtUtc).tz(branding.timezone || 'UTC').format('D')
}
function eventMonthBadge(e: EventDto): string {
    return dayjs.utc(e.startsAtUtc).tz(branding.timezone || 'UTC').format('MMM').toUpperCase()
}

// Background style for the apex track card cover. Reuses the tenant's own
// hero image; falls back to a flat themed background so the card never
// renders an empty white block when a tenant has no hero photo yet.
function apexTrackImageStyle(t: TrackDiscoverItem) {
    if (t.heroImageUrl) {
        return { backgroundImage: `url(${absoluteUrl(t.heroImageUrl)})` }
    }
    return { backgroundColor: 'rgb(var(--v-theme-secondary))' }
}

// Deep link from an apex event card straight to that event's public page on its
// tenant subdomain (the new standalone event page, not the old calendar modal).
function apexEventUrl(e: EventDiscoverItem): string {
    return tenantEventUrl({
        subdomain: e.tenantSubdomain,
        clientType: e.tenantClientType,
        customDomain: e.tenantCustomDomain,
        customDomainVerified: e.tenantCustomDomainVerified,
        externalHomeUrl: e.tenantExternalHomeUrl,
        externalEventsUrl: e.tenantExternalEventsUrl,
        embedEventTarget: e.tenantEmbedEventTarget,
    }, e.eventId)
}

// Apex event card image: per-event image wins, fall back to the event type's
// image, then to a flat fill in the event-type color. Both URLs come back as
// relative `/uploads/...` paths so we need to absolutize against the API host.
function apexEventImageStyle(e: EventDiscoverItem) {
    const img = e.imageUrl ?? e.eventTypeImageUrl ?? null
    if (img) {
        return {
            backgroundImage: `url(${absoluteUrl(img)})`,
            backgroundColor: e.eventTypeColor || '#1976D2',
        }
    }
    return { backgroundColor: e.eventTypeColor || '#1976D2' }
}

// ── Pricing snapshots ────────────────────────────────────────────────────────
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
/* ── Featured blog band (full-bleed; image left, copy right on desktop) ── */
.featured-blog {
    width: 100%;
    background: rgb(var(--v-theme-surface));
    /* Contained card within the page container (no longer a full-bleed band). */
    border: 1px solid rgba(0, 0, 0, 0.08);
    border-radius: 16px;
    overflow: hidden;
}
.featured-blog-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    min-height: 300px;
}
.featured-blog-media {
    background-size: cover;
    background-position: center;
    background-color: rgba(0, 0, 0, 0.06);
    min-height: 240px;
}
.featured-blog-content {
    display: flex;
    flex-direction: column;
    justify-content: center;
    padding: 48px;
}
.featured-blog-excerpt {
    max-width: 52ch;
    color: rgba(var(--v-theme-on-surface), 0.8);
}
@media (max-width: 960px) {
    .featured-blog-grid { grid-template-columns: 1fr; }
    .featured-blog-content { padding: 28px; }
}

/* ── Apex landing page ───────────────────────────────────────────────────
   Hero is full-bleed with a configurable background image; falls back to a
   dark gradient when the super admin hasn't uploaded one. The overlay sits
   on top of the image and dims it so white text always reads.

   Stats badges sit in a row at the bottom of the hero with a subtle dark
   pill background. Other sections (how-it-works, benefits, testimonials,
   CTA banner) inherit normal page width from v-container.
*/
/* Slight warm gray for the apex page background. Cards stay white (their
   default surface color) so they visually pop against this. Hero and CTA
   banner have their own background colors and override this. */
.apex-page {
    background-color: #f5f5f5;
}

.apex-hero {
    position: relative;
    min-height: 540px;
    background-size: cover;
    /* Anchor the crop to the top of the photo so heads stay in frame.
       `cover` zooms to fill and the default `center` drops the top first;
       `top` pins the top edge no matter the viewport ratio. */
    background-position: center top;
    background-color: rgb(var(--v-theme-secondary));
}
.apex-hero-overlay {
    position: relative;
    /* Horizontal darken: heavy on the left (where the headline, subhead,
       CTAs and stats sit) fading to fully transparent on the right (where
       the photo subject reads clean). Uniform top-to-bottom on each side
       so the text always has the same backstop regardless of viewport
       height, and so the right side of the photo never gets a mask. */
    background: linear-gradient(90deg,
        rgba(20, 24, 32, 0.95) 0%,
        rgba(20, 24, 32, 0.85) 25%,
        rgba(20, 24, 32, 0.50) 50%,
        rgba(20, 24, 32, 0.0) 75%);
    padding: 5rem 0 4rem;
    min-height: 540px;
    display: flex;
    align-items: center;
}
/* Hero headline: two-clause split renders one clause per line, alternating
   colors. line-height tight so the two clauses sit close together. */
.apex-hero-headline {
    line-height: 0.95;
}
.apex-hero-line {
    display: block;
}

.hero-subhead {
    max-width: 640px;
    opacity: 0.92;
}

.apex-stats {
    display: inline-flex;
    flex-wrap: wrap;
    gap: 1rem 4rem;
    padding: 1rem 2rem;
    background: rgba(0, 0, 0, 0.45);
    border-radius: 14px;
    color: #fff;
}
.apex-stat {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    text-align: left;
}
.apex-stat-icon {
    color: #ffffff;
    opacity: 0.95;
}
.apex-stat-num {
    font-family: 'Inter', sans-serif;
    font-weight: 700;
    font-style: normal;
    font-size: 1.75rem;
    line-height: 1.05;
}
.apex-stat-label {
    font-size: 0.8rem;
    opacity: 0.85;
    text-transform: uppercase;
    letter-spacing: 0.08em;
    margin-top: 2px;
}

/* Section heading: title centered between two horizontal rules that flex to
   fill the remaining space on each side. An optional trailing CTA (used on
   the "Upcoming events" header for the "View All Events" link) sits to the
   right of the second rule. Mirrors the "---  Title  ---" pattern from the
   mock for both column headers. */
.apex-section-head {
    display: flex;
    align-items: center;
    gap: 16px;
    width: 100%;
}
.apex-section-rule {
    flex: 1 1 auto;
    border: 0;
    height: 1px;
    margin: 0;
    background-color: rgba(0, 0, 0, 0.22);
}
.apex-section-title {
    flex: 0 0 auto;
    margin: 0;
    white-space: nowrap;
}
.apex-section-cta {
    flex: 0 0 auto;
}

/* Ride the best tracks: each card pairs the tenant's hero photo at the top
   with the track name + location below. Whole card is a link to the tenant
   subdomain. Hover gives a subtle lift to signal interactivity. */
.apex-track-card {
    overflow: hidden;
    text-decoration: none;
    transition: transform 0.15s ease, box-shadow 0.15s ease;
}
.apex-track-card:hover {
    transform: translateY(-2px);
    box-shadow: 0 6px 18px rgba(0, 0, 0, 0.12);
}
.apex-track-image {
    height: 180px;
    background-size: cover;
    background-position: center;
}

/* Upcoming events: card has a cover image at the top with a black date badge
   floated in the top-left, slightly inset from the corners so it reads as
   pinned-over-the-image rather than crammed into it. Falls back to a flat
   event-type color fill when no image is set. */
.apex-event-card {
    overflow: visible;
}
.apex-event-image {
    position: relative;
    height: 160px;
    background-size: cover;
    background-position: center;
}
/* Tenant white logo overlaid bottom-right on the event photo. */
.apex-event-logo {
    position: absolute;
    bottom: 8px;
    right: 8px;
    max-height: 28px;
    max-width: 96px;
    object-fit: contain;
    filter: drop-shadow(0 1px 2px rgba(0, 0, 0, 0.5));
}
.apex-event-datebadge {
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
    box-shadow: 1px 2px 5px 0px rgba(0,0,0,0.34); 
}
.apex-event-datebadge .apex-event-day {
    font-size: 1.4rem;
    font-weight: 700;
    color: #fff;
    line-height: 1;
}
.apex-event-datebadge .apex-event-month {
    font-size: 0.65rem;
    letter-spacing: 0.08em;
    color: #fff;
    opacity: 0.85;
    margin-top: 2px;
}
.apex-event-day {
    font-size: 1.75rem;
    font-weight: 700;
    line-height: 1;
}
.apex-event-month {
    font-size: 0.75rem;
    letter-spacing: 0.08em;
    opacity: 0.7;
    margin-top: 2px;
}

/* Benefits section: dark navy band that pairs a photo on the left with the
   bullet list of member perks on the right. 50/50 split on desktop. No
   gutter between the columns so the photo butts up against the dark panel
   for an editorial layout (mirrors the mock). The whole section gets a
   border radius so it reads as a contained card against the light page bg. */
.apex-benefits {
    background-color: rgb(var(--v-theme-secondary));
    color: #ffffff;
    border-radius: 16px;
    overflow: hidden;
}
.apex-benefits-photo {
    width: 100%;
    height: 100%;
    min-height: 360px;
    background-size: cover;
    background-position: center;
    background-color: rgba(255, 255, 255, 0.04);
}
.apex-benefits-photo--placeholder {
    background: linear-gradient(135deg, rgba(255, 255, 255, 0.08), rgba(255, 255, 255, 0.02));
}
.apex-benefits-content {
    padding: 3rem 3rem !important;
    display: flex;
    flex-direction: column;
    justify-content: center;
}
.apex-benefits-content :deep(ul) {
    list-style: none;
    padding-left: 0;
    margin: 0;
}
.apex-benefits-content :deep(li) {
    padding: 0.5rem 0 0.5rem 1.75rem;
    position: relative;
    font-size: 1.05rem;
}
.apex-benefits-content :deep(li::before) {
    content: '✓';
    position: absolute;
    left: 0;
    color: rgb(var(--v-theme-primary));
    font-weight: 700;
}
@media (max-width: 960px) {
    .apex-benefits-photo { min-height: 240px; }
    .apex-benefits-content { padding: 2rem 1.5rem; }
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
    /* Top room so the date badge, floated -6px above each card, isn't clipped by
       overflow-y: hidden (which overflow-x: auto forces on this axis). */
    padding-top: 10px;
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
/* Event-type chip moves to the top-right on the apex-style cards so it clears
   the date badge pinned top-left. */
.event-chip--right {
    left: auto;
    right: 8px;
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
