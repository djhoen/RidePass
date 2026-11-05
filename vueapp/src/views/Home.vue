<template>
    <div>
        <!-- Apex (ridepass.io, no tenant subdomain): platform landing page.
             Purpose: promote the platform's tracks and their events to riders,
             sell the season-pass idea, and promote RidePass to prospective track
             operators (owners band -> /ForTracks). Copy, hero image, featured
             tracks, testimonials and the bottom CTA band are pulled from
             platform_branding so super admins can edit without a deploy; every
             field falls back to the landing-mockup copy when it is empty.
             Pass tiers and the "how it works" steps are placeholder marketing
             content for now (see apexPassTiers / apexSteps in the script). -->
        <div v-if="isApex" class="apex-page">
            <!-- ── 1. HERO ─────────────────────────────────────────────────── -->
            <header class="lp-hero">
                <!-- Photo layer: CMS hero when set (inline style), else the bundled
                     landing photo from the scoped CSS. The ::after gradient dims the
                     left side for the copy and fades the bottom into the page. -->
                <div class="lp-hero-bg" :style="heroStyle"></div>
                <v-container class="wrap lp-hero-inner rp-on-photo">
                    <h1 class="font-display lp-hero-h1">
                        <span v-for="(line, i) in apexHeroLines" :key="i"
                            class="lp-hero-line" :class="{ 'font-display-outline': i > 0 }">{{ line }}</span>
                    </h1>
                    <p class="lp-hero-sub">{{ apexHeroSubhead }}</p>
                    <div class="lp-ctas">
                        <v-btn color="primary" variant="flat" size="x-large"
                            :to="apexHeroPrimary.url">{{ apexHeroPrimary.label }}</v-btn>
                        <v-btn variant="outlined" size="x-large"
                            :to="apexHeroSecondary.url">{{ apexHeroSecondary.label }}</v-btn>
                    </div>
                    <div class="stats">
                        <div v-if="apexStatsShowTracks"><b>{{ apexTrackCountLabel }}</b><span>Tracks</span></div>
                        <div v-if="apexStatsShowEventDays"><b>{{ apexEventDayCountLabel }}</b><span>Event days</span></div>
                        <div><b>1</b><span>Waiver, whole network</span></div>
                    </div>
                </v-container>
            </header>

            <!-- ── 2. UPCOMING EVENTS ──────────────────────────────────────
                 Compact date-block rows; each deep-links to the event on its
                 tenant. The discover DTO has no capacity, so no "few left" pill. -->
            <section id="events" class="lp-sec">
                <v-container class="wrap">
                    <div class="rp-sec-h">
                        <div>
                            <span class="rp-kicker">On the schedule</span>
                            <h2 class="font-display">{{ apexSectionTitle('events', 'Upcoming events') }}</h2>
                        </div>
                        <router-link to="/SignUp">Create a free account to enter →</router-link>
                    </div>
                    <div v-if="apexEvents.length > 0" class="evgrid">
                        <a v-for="e in apexEvents" :key="e.eventId" class="evcard"
                            :href="apexEventUrl(e)" rel="noopener">
                            <div class="evdate">
                                <div class="m">{{ formatApexEventMonth(e.startsAtUtc) }}</div>
                                <div class="d font-display">{{ formatApexEventDay(e.startsAtUtc) }}</div>
                            </div>
                            <div class="evbody">
                                <div class="n">{{ e.title }}</div>
                                <div class="t">{{ apexEventLocation(e) }}</div>
                                <span class="rp-pill">{{ e.eventTypeName }}</span>
                            </div>
                        </a>
                    </div>
                    <p v-else class="lp-empty">No upcoming events scheduled — check back soon.</p>
                </v-container>
            </section>

            <!-- ── 3. RIDE THE BEST TRACKS ─────────────────────────────────── -->
            <section id="tracks" class="lp-sec lp-sec--tight">
                <v-container class="wrap">
                    <div class="rp-sec-h">
                        <div>
                            <span class="rp-kicker">The network</span>
                            <h2 class="font-display">{{ apexSectionTitle('tracks', 'Ride the best tracks') }}</h2>
                        </div>
                        <router-link to="/Discover">See them in the app →</router-link>
                    </div>
                    <TrackCardGrid :tracks="apexFeaturedTracks" empty-text="No tracks on the platform yet." />
                </v-container>
            </section>

            <!-- ── TRACKS NEAR YOU (lower-48 map, auto-pinned from lat/lng) ── -->
            <section class="lp-sec lp-sec--tight">
                <v-container class="wrap">
                    <div class="rp-sec-h">
                        <div>
                            <span class="rp-kicker">Near you</span>
                            <h2 class="font-display">{{ apexSectionTitle('tracksNearYou', 'Tracks near you') }}</h2>
                        </div>
                        <router-link to="/Discover">Browse all tracks →</router-link>
                    </div>
                    <!-- overflow visible so the hover preview card can extend past the
                         map's border instead of being clipped. -->
                    <v-card class="pa-4 lp-map-card" style="overflow: visible">
                        <TracksMap :tracks="apexTracks" @select="openTrack" />
                    </v-card>
                </v-container>
            </section>

            <!-- ── 4. SEASON PASSES (nav links to /#passes) ─────────────────── -->
            <section v-if="apexPassTiers.length > 0" id="passes" class="lp-sec lp-passes">
                <v-container class="wrap">
                    <div class="rp-sec-h">
                        <div>
                            <span class="rp-kicker">Season passes</span>
                            <h2 class="font-display">One pass. Every gate.</h2>
                        </div>
                    </div>
                    <div class="pgrid">
                        <div v-for="tier in apexPassTiers" :key="tier.key"
                            class="pcard" :class="{ 'pcard--pop': tier.popular }">
                            <div class="tn">{{ tier.name }}</div>
                            <div class="pr font-display">{{ tier.price }}</div>
                            <div class="per">per season</div>
                            <ul>
                                <li v-for="b in tier.bullets" :key="b">{{ b }}</li>
                            </ul>
                            <v-btn v-if="tier.popular" color="primary" variant="flat" block to="/SignUp">{{ tier.cta }}</v-btn>
                            <v-btn v-else variant="outlined" block to="/SignUp">{{ tier.cta }}</v-btn>
                        </div>
                    </div>
                </v-container>
            </section>

            <!-- ── 5. HOW IT WORKS ─────────────────────────────────────────── -->
            <section class="lp-sec">
                <v-container class="wrap">
                    <div class="rp-sec-h">
                        <div>
                            <span class="rp-kicker">How it works</span>
                            <h2 class="font-display">Gate in three steps</h2>
                        </div>
                    </div>
                    <div class="steps">
                        <div v-for="s in apexSteps" :key="s.num" class="step">
                            <div class="num font-display">{{ s.num }}</div>
                            <h3>{{ s.title }}</h3>
                            <p>{{ s.body }}</p>
                        </div>
                    </div>
                </v-container>
            </section>

            <!-- ── 6. FOR TRACK OWNERS ─────────────────────────────────────── -->
            <section id="owners" class="lp-sec">
                <v-container class="wrap">
                    <div class="owners">
                        <div class="txt">
                            <span class="rp-kicker">For track owners</span>
                            <h2 class="font-display">Your gate, upgraded.</h2>
                            <p>Join the network and get a full track business suite: live revenue dashboard, QR gate check-in, rider CRM, events with race classes, POS, payouts through Stripe — and a new stream of riders carrying the pass.</p>
                            <div class="ownstats">
                                <div><b class="font-display">85%</b><span>of pass revenue pooled to tracks, paid by visits</span></div>
                                <div><b class="font-display">90%</b><span>of every day-pass sale goes to your track</span></div>
                                <div><b class="font-display">$0</b><span>software cost — we only earn when you do</span></div>
                            </div>
                            <v-btn color="primary" variant="flat" to="/ForTracks">Operator sign in</v-btn>
                        </div>
                        <!-- CMS photo when set, else the bundled track photo from the scoped CSS. -->
                        <div class="ph" :style="ownersPhotoStyle"></div>
                    </div>
                </v-container>
            </section>

            <!-- ── 7. TESTIMONIALS ─────────────────────────────────────────── -->
            <section v-if="apexQuotes.length > 0" class="lp-sec">
                <v-container class="wrap">
                    <div class="rp-sec-h">
                        <div>
                            <span class="rp-kicker">Riders on RidePass</span>
                            <h2 class="font-display">{{ apexSectionTitle('testimonials', 'What riders are saying') }}</h2>
                        </div>
                    </div>
                    <div class="quotes">
                        <div v-for="q in apexQuotes" :key="q.key" class="quote">
                            <div class="stars">{{ q.stars }}</div>
                            <p>"{{ q.quote }}"</p>
                            <div class="who">
                                <v-avatar v-if="q.photo" size="26"><v-img :src="q.photo" cover></v-img></v-avatar>
                                <span>{{ q.who }}</span>
                            </div>
                        </div>
                    </div>
                </v-container>
            </section>

            <!-- ── 8. CTA BAND ─────────────────────────────────────────────── -->
            <div class="band">
                <h2 class="font-display">{{ apexCta.headline }}</h2>
                <p>{{ apexCta.subhead }}</p>
                <v-btn color="primary" variant="flat" size="x-large" :to="apexCta.url">{{ apexCta.label }}</v-btn>
            </div>
        </div>

        <template v-else>
            <!-- ── 1. Hero (same chrome as the landing hero: full-bleed photo,
                 left-aligned, keeps the live track-status pill). Always shown. ── -->
            <header class="lp-hero lp-hero--tenant">
                <div class="lp-hero-bg" :style="tenantHeroStyle"></div>
                <v-container class="wrap lp-hero-inner rp-on-photo">
                    <h1 class="font-display lp-hero-h1 lp-hero-h1--tenant">{{ branding.displayName }}</h1>
                    <p v-if="branding.tagline" class="lp-hero-sub">{{ branding.tagline }}</p>
                    <div v-if="effectiveStatus" class="lp-status-row">
                        <span class="rp-pill lp-status status-badge"
                            :class="{ 'rp-pill--green': effectiveStatus.tone === 'open', 'rp-pill--red': effectiveStatus.tone === 'closed' }">
                            <v-icon size="14">{{ effectiveStatus.icon }}</v-icon>
                            {{ effectiveStatus.label }}
                        </span>
                        <span v-if="effectiveStatus.message" class="lp-status-msg">{{ effectiveStatus.message }}</span>
                    </div>
                    <div class="lp-ctas">
                        <v-btn color="primary" variant="flat" size="x-large" to="/Events">See Events</v-btn>
                        <v-btn v-if="hasSeasonPasses" variant="outlined" size="x-large"
                            to="/SeasonPasses">Season Passes</v-btn>
                    </div>
                </v-container>
            </header>

            <v-container class="wrap lp-tenant-body">
                <!-- ── 2. Next events row ──────────────────────────────────────── -->
                <!-- Horizontal scroll-snap track. Chevrons overlay the track edges
                     like a typical carousel — only visible while scrolling is possible
                     in that direction. Native scroll keeps touch + keyboard
                     accessibility free; we just script the buttons. -->
                <section v-if="sectionVisible('nextEvents') && nextEvents.length > 0" class="mb-12">
                    <div class="rp-sec-h">
                        <h2 class="font-display">{{ nextUpTitle }}</h2>
                        <router-link to="/Events">See events →</router-link>
                    </div>
                    <div class="next-up-wrap">
                        <button v-show="canScrollLeft" type="button" class="next-up-arrow next-up-arrow--left"
                            aria-label="Previous events" @click="scrollNextUp(-1)">
                            <v-icon size="28">mdi-chevron-left</v-icon>
                        </button>
                        <button v-show="canScrollRight" type="button" class="next-up-arrow next-up-arrow--right"
                            aria-label="Next events" @click="scrollNextUp(1)">
                            <v-icon size="28">mdi-chevron-right</v-icon>
                        </button>
                        <div ref="nextUpTrack" class="next-up-track"
                            @scroll.passive="updateNextUpScrollState">
                            <div v-for="e in nextEvents" :key="e.id" class="next-up-card">
                                <!-- Whole card links to the event landing page. -->
                                <v-card class="apex-event-card d-flex flex-column" style="height: 100%"
                                    :to="`/Event/${e.id}`">
                                <!-- Cover image with the day/month date block pinned top-left
                                     and the event-type pill top-right. -->
                                <div class="apex-event-image rp-on-photo" :style="eventCoverStyle(e)">
                                    <div class="apex-event-datebadge">
                                        <div class="apex-event-month">{{ eventMonthBadge(e) }}</div>
                                        <div class="apex-event-day font-display">{{ eventDayBadge(e) }}</div>
                                    </div>
                                    <span class="rp-pill event-chip event-chip--right">{{ e.eventTypeName }}</span>
                                </div>
                                <v-card-text class="pa-4 flex-grow-1 d-flex flex-column">
                                    <div class="lp-card-title font-display-upright">{{ e.title }}</div>
                                    <div class="lp-card-sub">{{ formatEventDate(e) }}</div>
                                    <div v-if="e.minTicketPriceCents" class="lp-card-price">
                                        From <strong>${{ (e.minTicketPriceCents / 100).toFixed(2) }}</strong>
                                    </div>
                                    <!-- Every card gets a CTA so non-race events (practice, lesson,
                                         open ride) don't look unbookable. -->
                                    <div class="d-flex flex-wrap ga-2 mt-auto pt-3">
                                        <v-btn v-if="e.hasRaceEntryTiers" size="small" color="primary" variant="flat" :to="`/Event/${e.id}`">
                                            Race Entry
                                        </v-btn>
                                        <v-btn v-else size="small" color="primary" variant="flat" :to="`/Event/${e.id}`">
                                            {{ e.minTicketPriceCents ? 'Get tickets' : 'View event' }}
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
                            <span class="rp-kicker">From the blog</span>
                            <h2 class="font-display featured-blog-title">{{ featuredPost.title }}</h2>
                            <p v-if="featuredPost.excerpt" class="featured-blog-excerpt mb-6">
                                {{ featuredPost.excerpt }}
                            </p>
                            <div>
                                <v-btn color="primary" variant="flat" size="large" :to="`/Blog/${featuredPost.slug}`"
                                    append-icon="mdi-arrow-right">Read more</v-btn>
                            </div>
                        </div>
                    </div>
                </section>

                <!-- ── 3. Pricing snapshot ─────────────────────────────────────── -->
                <!-- Rendered (with a skeleton placeholder) from first paint whenever the
                     season-pass feature is on, so the section reserves its slot during load
                     instead of popping in above About and shifting the page down. Once data
                     resolves it shows the real card, or collapses if there are no priced passes. -->
                <section v-if="sectionVisible('passes') && branding.seasonPassesEnabled
                             && (initialLoading || seasonPassFromCents !== null)" class="my-12">
                    <div class="rp-sec-h">
                        <h2 class="font-display">Passes</h2>
                    </div>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-card class="pcard pcard--tenant" style="min-height: 236px">
                                <template v-if="seasonPassFromCents !== null">
                                    <div class="tn">Season Pass</div>
                                    <div class="pr font-display">From ${{ (seasonPassFromCents / 100).toFixed(2) }}</div>
                                    <div class="per">Ride the whole season — best value.</div>
                                    <div class="mt-auto">
                                        <v-btn color="primary" variant="flat" to="/SeasonPasses">See Season Passes</v-btn>
                                    </div>
                                </template>
                                <v-skeleton-loader v-else type="heading, text, text, button"
                                    class="bg-transparent px-0" />
                            </v-card>
                        </v-col>
                    </v-row>
                </section>

                <!-- ── Benefits (bordered band: photo left, checkmark perks right) ── -->
                <section v-if="sectionVisible('benefits') && (branding.benefitsHtml || branding.benefitsImageUrl)"
                    class="apex-benefits my-12">
                    <v-row no-gutters>
                        <v-col cols="12" md="4">
                            <div v-if="branding.benefitsImageUrl" class="apex-benefits-photo"
                                :style="{ backgroundImage: `url(${branding.benefitsImageUrl})` }"></div>
                            <div v-else class="apex-benefits-photo apex-benefits-photo--placeholder"></div>
                        </v-col>
                        <v-col cols="12" md="8" class="apex-benefits-content">
                            <span class="rp-kicker">Membership</span>
                            <h2 class="font-display apex-benefits-title">Why ride with {{ branding.displayName }}</h2>
                            <div v-if="branding.benefitsHtml" v-html="benefitsHtmlSafe"></div>
                        </v-col>
                    </v-row>
                </section>

                <!-- ── 4. About ──────────────────────────────────────────────── -->
                <section v-if="sectionVisible('about') && branding.aboutHtml" class="my-12">
                    <div class="rp-sec-h">
                        <h2 class="font-display">About</h2>
                    </div>
                    <div class="rich-text-body" v-html="aboutHtmlSafe"></div>
                    <v-img v-if="branding.secondaryHeroUrl" :src="branding.secondaryHeroUrl"
                        max-height="400" cover class="mt-6 lp-rounded-img"></v-img>
                </section>

                <!-- ── 5. Photo gallery ──────────────────────────────────────── -->
                <section v-if="sectionVisible('gallery') && gallery.length > 0" class="my-12">
                    <div class="rp-sec-h">
                        <h2 class="font-display">Photos</h2>
                    </div>
                    <v-row>
                        <v-col v-for="(img, idx) in gallery" :key="img.id" cols="6" md="4" lg="3">
                            <!-- Thumbnail: caption hidden here; only shown inside the slideshow. -->
                            <v-img :src="absoluteUrl(img.imageUrl)" aspect-ratio="1" cover
                                class="gallery-thumb" @click="openGallery(idx)"></v-img>
                        </v-col>
                    </v-row>
                </section>

                <!-- ── 6. Track Layout ──────────────────────────────────────── -->
                <!-- Park location info (map + address) was removed — riders find that
                     in the footer now. Track-layout images are unique content, kept here. -->
                <section v-if="sectionVisible('trackLayout') && trackGraphics.length > 0" class="my-12">
                    <div class="rp-sec-h">
                        <h2 class="font-display">Track Layout</h2>
                    </div>
                    <div>
                        <v-card v-for="g in trackGraphics" :key="g.id" class="mb-3">
                            <v-row no-gutters>
                                <v-col cols="12" md="5">
                                    <v-img :src="absoluteUrl(g.imageUrl)" aspect-ratio="1.5" cover></v-img>
                                </v-col>
                                <v-col cols="12" md="7">
                                    <v-card-text class="pa-6">
                                        <h4 v-if="g.title" class="lp-card-title font-display-upright mb-2">{{ g.title }}</h4>
                                        <p v-if="g.description" class="lp-body-copy mb-0" style="white-space: pre-wrap">{{ g.description }}</p>
                                    </v-card-text>
                                </v-col>
                            </v-row>
                        </v-card>
                    </div>
                </section>

                <!-- ── 7. Hours of operation ─────────────────────────────────── -->
                <section v-if="sectionVisible('hours') && weekHours.length > 0" class="my-12">
                    <div class="rp-sec-h">
                        <h2 class="font-display">Hours</h2>
                    </div>
                    <v-card class="pa-5 hours-card">
                        <div v-for="day in weekHours" :key="day.key" class="hours-row">
                            <span class="hours-day">{{ day.label }}</span>
                            <span v-if="day.closed" class="rp-pill rp-pill--red">Closed</span>
                            <span v-else class="tabular">{{ day.open }} – {{ day.close }}</span>
                        </div>
                    </v-card>
                </section>

                <!-- ── 9. Sign up / log in strip ─────────────────────────────── -->
                <section v-if="sectionVisible('signup') && !isAuthenticated" class="mb-12">
                    <v-card class="pa-8 text-center lp-signup">
                        <h3 class="font-display lp-signup-title">Already have an account?</h3>
                        <p class="lp-signup-copy">Log in to check in at the gate, see your purchases, or buy more passes. New here? Your account is created automatically at your first purchase.</p>
                        <v-btn color="primary" variant="flat" to="/Login">Log In</v-btn>
                    </v-card>
                </section>

                <!-- Full-screen photo gallery slideshow. Triggered by clicking any
                     gallery thumbnail; arrows cycle through. Caption + counter overlay
                     at the bottom so the image owns the whole viewport. -->
                <v-dialog v-model="galleryDialog" fullscreen
                    transition="dialog-bottom-transition" :scrim="false">
                    <v-card class="gallery-fullscreen">
                        <v-toolbar density="compact" color="transparent" theme="dark" flat>
                            <v-toolbar-title v-if="gallery.length > 1" class="text-caption text-muted">
                                {{ galleryIndex + 1 }} / {{ gallery.length }}
                            </v-toolbar-title>
                            <v-spacer></v-spacer>
                            <v-btn icon="mdi-close" variant="text"
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

// Tenant hero background (reuses the landing hero chrome). With no hero photo
// the scoped CSS paints a dark diagonal gradient instead of a stock photo.
const tenantHeroStyle = computed(() =>
    branding.heroImageUrl
        ? { backgroundImage: `url(${branding.heroImageUrl})` }
        : {})

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

// Featured tracks render 3; upcoming events fill a three-column grid of
// compact rows, two rows deep at most.
const APEX_TRACK_LIMIT = 3
const APEX_EVENT_LIMIT = 6

// Surfaced to the rider via a top snackbar when a page load fails, so a network
// error shows a real message instead of a blank home / apex page.
const loadError = ref('')

// True until the tenant home's data fetch (events, season passes, gallery...) first
// resolves. Lets data-dependent sections reserve their slot with a skeleton during
// load instead of popping into flow afterward and shifting already-painted content.
const initialLoading = ref(true)

// Photo-gallery slideshow modal state. Set when a thumbnail is clicked.
const galleryDialog = ref(false)
const galleryIndex = ref(0)
function openGallery(index: number) {
    galleryIndex.value = index
    galleryDialog.value = true
}

async function load() {
    if (!branding.loaded || isApex.value) return
    initialLoading.value = true
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
    } finally {
        initialLoading.value = false
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
            // Start of today, not this instant: an event in progress (or one that ended earlier
            // today) must stay listed until the day after it ends.
            discoverService.searchEvents({ fromUtc: dayjs().startOf('day').utc().toISOString() }),
        ])
        apexTracks.value = (trackResp.data as any).data
        apexEvents.value = (eventResp.data as any).data.slice(0, APEX_EVENT_LIMIT)
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            || 'Could not load tracks and events. Refresh to try again, or check your connection.'
    }
}
onMounted(loadApex)

// ── Apex computed (all driven by platform_branding, mockup copy as fallback) ──
const apexHeroHeadline = computed(() =>
    platformBranding.data?.heroHeadline?.trim() || 'Find your track. Ride this weekend.')
const apexHeroSubhead = computed(() =>
    platformBranding.data?.heroSubhead?.trim()
    || 'Discover motocross tracks near you, see what\'s on the schedule, and grab your gate pass before you load the van. One membership, one waiver, one QR code at every member gate.')

// Split the headline on sentence boundaries so each clause renders on its
// own line: the first line solid, the rest outlined (mockup: "FIND YOUR
// TRACK." solid, "RIDE THIS WEEKEND." stroke-only). When the admin enters a
// single sentence, only one line renders. Periods dropped by the split are
// re-added so the rendered text matches what the admin typed.
const apexHeroLines = computed<string[]>(() => {
    const raw = apexHeroHeadline.value.trim()
    if (!raw) return []
    const parts = raw.split(/\.\s+/).map(p => p.trim()).filter(Boolean)
    return parts.map((p, i) => {
        if (i === parts.length - 1) return p
        return p.endsWith('.') ? p : p + '.'
    })
})
// Hero CTA pair: CMS label + URL when both are set, else the mockup pair.
const apexHeroPrimary = computed(() => buttonFrom(
    platformBranding.data?.heroCtaPrimaryLabel,
    platformBranding.data?.heroCtaPrimaryUrl) ?? { label: 'Browse tracks', url: '/Discover' })
const apexHeroSecondary = computed(() => buttonFrom(
    platformBranding.data?.heroCtaSecondaryLabel,
    platformBranding.data?.heroCtaSecondaryUrl) ?? { label: 'Upcoming events', url: '/Events' })

const apexStatsShowTracks = computed(() => platformBranding.data?.statsShowTracks !== false)
const apexStatsShowEventDays = computed(() => platformBranding.data?.statsShowEventDays !== false)

// Stats counts use real numbers from /Discover. Show a dash until tracks load
// so admins never see a misleading "0" or "0+".
const apexTrackCount = computed(() => apexTracks.value.length)
const apexEventDayCount = computed(() =>
    apexTracks.value.reduce((sum, t) => sum + (t.upcomingEventsCount || 0), 0))
const apexTrackCountLabel = computed(() => apexTrackCount.value > 0 ? String(apexTrackCount.value) : '–')
const apexEventDayCountLabel = computed(() => apexEventDayCount.value > 0 ? `${apexEventDayCount.value}+` : '–')

// CMS hero photo as an inline background; empty when unset so the bundled
// landing photo from the scoped CSS shows instead.
const heroStyle = computed(() => {
    const url = platformImageUrl(platformBranding.data?.heroImageUrl)
    return url ? { backgroundImage: `url(${url})` } : {}
})

// Owners band photo: the platform's "benefits" photo when a super admin has
// uploaded one, else the bundled track photo from the scoped CSS.
const ownersPhotoStyle = computed(() => {
    const url = platformImageUrl(platformBranding.data?.benefitsImageUrl)
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

// Quote cards come only from CMS testimonials (Super Admin → Home page). The section
// is hidden when there are none: invented reviews must never ship on the public site.
type ApexQuote = { key: string; stars: string; quote: string; who: string; photo: string }
const apexQuotes = computed<ApexQuote[]>(() => {
    return apexTestimonials.value.map(t => ({
        key: t.id,
        stars: '★'.repeat(Math.min(5, Math.max(1, Math.round(t.rating || 5)))),
        quote: t.quote,
        who: t.riderName,
        photo: testimonialPhoto(t.riderPhotoUrl),
    }))
})

// Bottom CTA band: CMS banner copy when set, else the mockup copy.
const apexCta = computed(() => {
    const b = platformBranding.data
    const btn = buttonFrom(b?.ctaBannerCtaLabel, b?.ctaBannerCtaUrl)
        ?? { label: 'Get started — it\'s free', url: '/SignUp' }
    return {
        headline: b?.ctaBannerHeadline?.trim() || 'Ready to ride?',
        subhead: b?.ctaBannerSubhead?.trim() || 'Create your free account, sign once, and your pass works at every member gate.',
        label: btn.label,
        url: btn.url,
    }
})

// ── Apex marketing content (placeholder) ────────────────────────────────────
// The landing-page mockup shows four priced season-pass tiers. RidePass has no
// platform-wide pass product yet, so this list is intentionally EMPTY and the section
// (and the NavBar "Season passes" link) stay hidden: advertising prices for products
// that do not exist is not acceptable on the public site. Populate it from
// platform_branding once it grows pass-tier fields (name, price, bullets, "most
// popular" flag, CTA label + URL); the template below the fold is ready for it.
// The "how it works" steps (apexSteps) remain mockup copy and describe the product truthfully.
type ApexPassTier = { key: string; name: string; price: string; popular: boolean; cta: string; bullets: string[] }
const apexPassTiers: ApexPassTier[] = []
const apexSteps = [
    { num: '01', title: 'Create your account', body: 'Sign up free and e-sign the network waiver once — it covers you at every member track, all season.' },
    { num: '02', title: 'Pick your pass', body: 'Buy a season pass in the app with secure in-app checkout — or just grab a one-tap day pass when you show up.' },
    { num: '03', title: 'Scan in and ride', body: 'Your QR pass lives on your phone. Flash it at the gate, the track gets paid automatically, and you\'re on the track.' },
]

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

// Date-block helpers for the upcoming-events rows (month abbrev over day
// number, to match the mockup's "OCT / 12" stacked format).
function formatApexEventDay(utc: string): string {
    return dayjs.utc(utc).local().format('D')
}
function formatApexEventMonth(utc: string): string {
    return dayjs.utc(utc).local().format('MMM').toUpperCase()
}
// "Track · City, Region" subline for an apex event row.
function apexEventLocation(e: EventDiscoverItem): string {
    const place = [e.tenantCity, e.tenantRegion].filter(Boolean).join(', ')
    return place ? `${e.tenantDisplayName} · ${place}` : e.tenantDisplayName
}
// Tenant event-card date badge (day / month) in the tenant's timezone, matching
// the rest of the tenant page's date display.
function eventDayBadge(e: EventDto): string {
    return dayjs.utc(e.startsAtUtc).tz(branding.timezone || 'UTC').format('D')
}
function eventMonthBadge(e: EventDto): string {
    return dayjs.utc(e.startsAtUtc).tz(branding.timezone || 'UTC').format('MMM').toUpperCase()
}

// Deep link from an apex event row straight to that event's public page on its
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
            backgroundColor: e.eventTypeColor || 'var(--rp-raise)',
        }
    }
    const base = e.eventTypeColor || 'var(--rp-raise)'
    // Diagonal gradient: full color → 25% darker overlay (rgba black) so the typography
    // pops without us having to compute a second color from the hex.
    return {
        backgroundColor: base,
        backgroundImage: `linear-gradient(135deg, transparent 0%, rgba(0,0,0,0.32) 100%)`,
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
/* ═══════════════════════════════════════════════════════════════════════════
   Landing page (apex) + tenant home — dark monochrome system.
   Mirrors ridepassNewUi/public/index.html: 1140px column, Bebas display
   type, hairline-bordered cards on --rp-card, white as the only accent.
   ═══════════════════════════════════════════════════════════════════════════ */

/* ── Layout ─────────────────────────────────────────────────────────────── */
.apex-page {
    background: var(--rp-bg);
    color: var(--rp-ink);
}
/* Mockup .wrap: 1140px column with 22px gutters. Scoped class + attribute
   outranks Vuetify's breakpoint max-widths on .v-container. */
.wrap {
    max-width: 1140px;
    padding-left: 22px;
    padding-right: 22px;
}
.lp-sec { padding: 74px 0; }
.lp-sec--tight { padding-top: 20px; }
.lp-tenant-body { padding-top: 48px; }
.lp-empty {
    color: var(--rp-muted2);
    font-size: 13px;
    margin: 0;
}
.lp-body-copy {
    color: var(--rp-muted);
    font-size: 14.5px;
    line-height: 1.6;
}
.lp-rounded-img { border-radius: var(--rp-radius); }

/* ── Hero ───────────────────────────────────────────────────────────────── */
/* v-main already pads for the 64px app bar, so the hero starts flush under
   it; the mockup's 92vh is measured against the full viewport. */
.lp-hero {
    position: relative;
    min-height: calc(92vh - 64px);
    display: flex;
    align-items: center;
    padding: 56px 0 70px;
    overflow: hidden;
    background: var(--rp-bg);
}
.lp-hero-bg {
    position: absolute;
    inset: 0;
    background: url('@/assets/landing-hero.jpg') center / cover no-repeat;
}
.lp-hero-bg::after {
    content: '';
    position: absolute;
    inset: 0;
    background:
        linear-gradient(105deg, rgba(var(--rp-scrim), 0.9) 20%, rgba(var(--rp-scrim), 0.55) 55%, rgba(var(--rp-scrim), 0.35)),
        linear-gradient(180deg, transparent 55%, var(--rp-bg));
}
.lp-hero-inner {
    position: relative;
    z-index: 1;
}
.lp-hero-h1 {
    font-size: clamp(58px, 9vw, 108px);
    margin: 0;
    color: var(--rp-photo-ink);
}
.lp-hero-line { display: block; }
.lp-hero-sub {
    max-width: 520px;
    color: var(--rp-photo-muted);
    font-size: 16.5px;
    line-height: 1.6;
    margin: 18px 0 26px;
}
.lp-ctas {
    display: flex;
    gap: 12px;
    flex-wrap: wrap;
}
/* Stats strip: frosted pill group with display-type numbers. Sits on the hero
   photo, so it uses the photo tokens (dark glass, white type) in both modes. */
.stats {
    display: flex;
    margin-top: 38px;
    background: rgba(var(--rp-scrim), 0.72);
    border: 1px solid var(--rp-photo-line);
    border-radius: 14px;
    backdrop-filter: blur(8px);
    overflow: hidden;
    width: fit-content;
    max-width: 100%;
}
.stats > div {
    padding: 14px 26px;
    border-right: 1px solid var(--rp-photo-line);
}
.stats > div:last-child { border-right: 0; }
.stats b {
    display: block;
    font-family: var(--rp-font-display);
    font-style: italic;
    font-weight: 400;
    font-size: 30px;
    line-height: 1;
    color: var(--rp-photo-ink);
}
.stats span {
    font-size: 10.5px;
    font-weight: 800;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: var(--rp-photo-muted);
}

/* Tenant hero: same chrome, shorter, dark gradient instead of a stock photo
   when the track has no hero image, slightly smaller title for long names.
   The photo-less gradient stays dark in both modes: the hero is a photo
   surface (white type over a scrim), and the bottom fade meets the page. */
.lp-hero--tenant { min-height: min(640px, calc(82vh - 64px)); }
.lp-hero--tenant .lp-hero-bg {
    background-image: linear-gradient(135deg, rgba(var(--rp-scrim), 0.78) 0%, rgb(var(--rp-scrim)) 70%);
}
.lp-hero-h1--tenant { font-size: clamp(44px, 7vw, 92px); }
.lp-status-row {
    display: flex;
    align-items: center;
    gap: 12px;
    flex-wrap: wrap;
    margin: 0 0 22px;
}
/* Status pill sits on the hero photo: frosted dark glass so it reads in both modes.
   The open/closed tints are the dark-mode semantic hues (the light-mode ones are
   darkened for white cards and would sink into the scrim). */
.lp-status {
    font-size: 12px;
    padding: 7px 14px;
    background: rgba(var(--rp-scrim), 0.72);
    border: 1px solid var(--rp-photo-line);
    color: var(--rp-photo-ink);
    backdrop-filter: blur(6px);
}
.lp-status.rp-pill--green { color: #5dd39e; border-color: rgba(93, 211, 158, 0.45); }
.lp-status.rp-pill--red { color: #ff9a8a; border-color: rgba(255, 107, 107, 0.45); }
.lp-status-msg {
    color: var(--rp-photo-muted);
    font-size: 13.5px;
}

/* ── Upcoming events (compact rows) ─────────────────────────────────────── */
.evgrid {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 14px;
}
.evcard {
    display: flex;
    gap: 14px;
    align-items: center;
    min-width: 0;
    background: var(--rp-card);
    border: 1px solid var(--rp-line);
    border-radius: 15px;
    padding: 16px;
    color: inherit;
    transition: border-color 0.15s;
}
.evcard:hover { border-color: var(--rp-line3); }
.evdate {
    flex: 0 0 auto;
    width: 58px;
    text-align: center;
    background: var(--rp-bg);
    border: 1px solid var(--rp-line2);
    border-radius: 11px;
    padding: 8px 0;
}
.evdate .m {
    font-size: 10px;
    font-weight: 800;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: var(--rp-muted);
}
.evdate .d {
    font-size: 26px;
    line-height: 1.05;
}
.evbody {
    flex: 1 1 auto;
    min-width: 0;
}
.evcard .n {
    font-weight: 800;
    font-size: 14.5px;
    margin-bottom: 2px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}
.evcard .t {
    font-size: 12px;
    color: var(--rp-muted);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}
.evcard .rp-pill { margin-top: 6px; }

/* ── Tracks near you ────────────────────────────────────────────────────── */
.lp-map-card { background: var(--rp-card); }

/* ── Season passes ──────────────────────────────────────────────────────── */
.lp-passes {
    background: var(--rp-panel);
    border-top: 1px solid var(--rp-line);
    border-bottom: 1px solid var(--rp-line);
}
/* On the light page the panel is white, same as the cards; drop the band a step
   so the pricing cards still read as cards. */
:root[data-rp-theme="light"] .lp-passes { background: var(--rp-panel2); }
.pgrid {
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    gap: 14px;
}
.pcard {
    position: relative;
    display: flex;
    flex-direction: column;
    background: var(--rp-card);
    border: 1px solid var(--rp-line2);
    border-radius: 18px;
    padding: 22px;
    transition: border-color 0.15s;
}
.pcard:hover { border-color: var(--rp-line3); }
.pcard--pop,
.pcard--pop:hover { border-color: var(--rp-ink); }
.pcard--pop::before {
    content: 'Most popular';
    position: absolute;
    top: -11px;
    left: 50%;
    transform: translateX(-50%);
    background: var(--rp-ink);
    color: var(--rp-on-ink);
    font-size: 10px;
    font-weight: 800;
    letter-spacing: 0.1em;
    text-transform: uppercase;
    padding: 4px 12px;
    border-radius: 99px;
    white-space: nowrap;
}
.pcard .tn {
    font-size: 13px;
    font-weight: 800;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: var(--rp-ink2);
}
.pcard .pr {
    font-size: 52px;
    margin: 8px 0 2px;
}
.pcard .per {
    font-size: 11.5px;
    color: var(--rp-muted2);
    margin-bottom: 16px;
}
.pcard ul {
    list-style: none;
    padding: 0;
    margin: 0 0 20px;
    flex: 1;
}
.pcard li {
    position: relative;
    font-size: 12.5px;
    color: var(--rp-ink2);
    line-height: 1.5;
    padding: 5px 0 5px 20px;
}
.pcard li::before {
    content: '✓';
    position: absolute;
    left: 0;
    font-weight: 800;
}
/* Vuetify's block button is flex: 1 0 auto, which would stretch it to fill
   the card column; pin it to its natural height. */
.pcard .v-btn {
    flex: 0 0 auto;
}
/* Tenant pricing snapshot reuses the tier card chrome on a v-card. */
.pcard--tenant {
    padding: 26px !important;
    border-radius: 18px !important;
}
.pcard--tenant .per { font-size: 13px; color: var(--rp-muted); margin-bottom: 22px; }
.pcard--tenant .pr { font-size: 44px; }

/* ── How it works ───────────────────────────────────────────────────────── */
.steps {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 14px;
}
.step {
    background: var(--rp-card);
    border: 1px solid var(--rp-line);
    border-radius: 16px;
    padding: 24px;
}
.step .num {
    font-size: 44px;
    color: transparent;
    -webkit-text-stroke: 1.5px var(--rp-muted2);
}
.step h3 {
    font-size: 16px;
    font-weight: 800;
    margin: 10px 0 6px;
}
.step p {
    font-size: 13px;
    color: var(--rp-muted);
    line-height: 1.55;
    margin: 0;
}

/* ── For track owners ───────────────────────────────────────────────────── */
.owners {
    display: grid;
    grid-template-columns: 1.1fr 0.9fr;
    align-items: stretch;
    background: var(--rp-card);
    border: 1px solid var(--rp-line2);
    border-radius: 22px;
    overflow: hidden;
}
.owners .txt { padding: 38px; }
.owners .txt h2 {
    font-size: clamp(32px, 4vw, 44px);
    margin: 0 0 12px;
}
.owners .txt p {
    font-size: 14px;
    color: var(--rp-muted);
    line-height: 1.65;
    margin: 0 0 6px;
}
.owners .ph {
    min-height: 320px;
    background: url('@/assets/landing-owners.jpg') center / cover no-repeat;
    background-color: var(--rp-card2);
}
.ownstats {
    display: flex;
    gap: 24px;
    margin: 20px 0 24px;
    flex-wrap: wrap;
}
.ownstats b {
    display: block;
    font-size: 34px;
}
.ownstats span {
    display: block;
    max-width: 130px;
    font-size: 11px;
    font-weight: 700;
    color: var(--rp-muted2);
    line-height: 1.35;
}

/* ── Testimonials ───────────────────────────────────────────────────────── */
.quotes {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 14px;
}
.quote {
    background: var(--rp-card);
    border: 1px solid var(--rp-line);
    border-radius: 16px;
    padding: 26px;
}
.quote .stars {
    letter-spacing: 3px;
    font-size: 13px;
    margin-bottom: 12px;
}
.quote p {
    font-size: 16px;
    line-height: 1.55;
    font-weight: 600;
    margin: 0;
}
.quote .who {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-top: 14px;
    font-size: 12px;
    font-weight: 800;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: var(--rp-muted2);
}

/* ── CTA band ───────────────────────────────────────────────────────────── */
.band {
    text-align: center;
    padding: 84px 22px;
}
.band h2 {
    font-size: clamp(44px, 6vw, 72px);
    margin: 0 0 14px;
}
.band p {
    color: var(--rp-muted);
    font-size: 15px;
    margin: 0 0 26px;
}

/* ── Responsive (mockup breakpoints) ────────────────────────────────────── */
@media (max-width: 920px) {
    .evgrid, .steps, .pgrid { grid-template-columns: 1fr 1fr; }
    .owners { grid-template-columns: 1fr; }
    .owners .ph { min-height: 220px; order: -1; }
}
@media (max-width: 560px) {
    .evgrid, .steps, .pgrid, .quotes { grid-template-columns: 1fr; }
    .stats > div { padding: 12px 16px; }
    .lp-hero { min-height: calc(86vh - 56px); padding: 40px 0 56px; }
    .lp-sec { padding: 56px 0; }
    .owners .txt { padding: 26px 22px; }
    .pgrid { padding-top: 12px; }
}

/* ═══════════════════════════════════════════════════════════════════════════
   Tenant home sections
   ═══════════════════════════════════════════════════════════════════════════ */

/* ── Featured blog band (contained card; image left, copy right on desktop) ── */
.featured-blog {
    width: 100%;
    background: var(--rp-card);
    border: 1px solid var(--rp-line);
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
    background-color: var(--rp-card2);
    min-height: 240px;
}
.featured-blog-content {
    display: flex;
    flex-direction: column;
    justify-content: center;
    padding: 48px;
}
.featured-blog-title {
    font-size: clamp(30px, 4vw, 44px);
    margin: 0 0 12px;
}
.featured-blog-excerpt {
    max-width: 52ch;
    color: var(--rp-muted);
    font-size: 14.5px;
    line-height: 1.6;
}
@media (max-width: 960px) {
    .featured-blog-grid { grid-template-columns: 1fr; }
    .featured-blog-content { padding: 28px; }
}

/* ── Next-up carousel ───────────────────────────────────────────────────── */
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
    background: var(--rp-raise);
    color: var(--rp-ink);
    border: 1px solid var(--rp-line2);
    box-shadow: var(--rp-shadow);
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    transition: border-color 0.15s ease, transform 0.15s ease;
}
.next-up-arrow:hover {
    border-color: var(--rp-line3);
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
    padding-top: 4px;
    padding-bottom: 4px;          /* room for hover border without clipping */
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

/* Event card: cover photo (or event-type gradient) with the date block pinned
   top-left and the type pill top-right, both on a frosted dark backdrop so
   they read on any photo. The image is an rp-on-photo surface: the badges use
   the photo tokens and stay dark-glass / white type in both modes. */
.apex-event-card {
    overflow: hidden;
}
.apex-event-image {
    position: relative;
    height: 160px;
    background-size: cover;
    background-position: center;
    background-color: var(--rp-card2);
}
.apex-event-image::after {
    content: '';
    position: absolute;
    inset: 0;
    background: linear-gradient(180deg, transparent 40%, rgba(var(--rp-scrim), 0.75));
    pointer-events: none;
}
.apex-event-datebadge {
    position: absolute;
    top: 10px;
    left: 10px;
    z-index: 1;
    width: 54px;
    text-align: center;
    background: rgba(var(--rp-scrim), 0.82);
    border: 1px solid var(--rp-photo-line);
    border-radius: 11px;
    padding: 6px 0 5px;
    backdrop-filter: blur(6px);
}
.apex-event-datebadge .apex-event-month {
    font-size: 10px;
    font-weight: 800;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: var(--rp-photo-muted);
}
.apex-event-datebadge .apex-event-day {
    font-size: 24px;
    line-height: 1.05;
    color: var(--rp-photo-ink);
}
.event-chip {
    position: absolute;
    top: 10px;
    left: 10px;
    z-index: 1;
    background: rgba(var(--rp-scrim), 0.82);
    border: 1px solid var(--rp-photo-line);
    color: var(--rp-photo-ink);
    backdrop-filter: blur(6px);
}
/* Event-type pill moves to the top-right so it clears the date block. */
.event-chip--right {
    left: auto;
    right: 10px;
}
.lp-card-title {
    font-size: 22px;
    line-height: 1;
    margin-bottom: 4px;
}
.lp-card-sub {
    font-size: 12.5px;
    color: var(--rp-muted);
}
.lp-card-price {
    font-size: 13.5px;
    color: var(--rp-ink2);
    margin-top: 8px;
}

/* ── Benefits band (photo left, checkmark perks right) ─────────────────── */
.apex-benefits {
    background: var(--rp-card);
    border: 1px solid var(--rp-line2);
    border-radius: 22px;
    overflow: hidden;
}
.apex-benefits-photo {
    width: 100%;
    height: 100%;
    min-height: 360px;
    background-size: cover;
    background-position: center;
    background-color: var(--rp-card2);
}
.apex-benefits-photo--placeholder {
    background: linear-gradient(135deg, var(--rp-raise), var(--rp-card2));
}
.apex-benefits-content {
    padding: 38px !important;
    display: flex;
    flex-direction: column;
    justify-content: center;
}
.apex-benefits-title {
    font-size: clamp(32px, 4vw, 44px);
    margin: 0 0 16px;
}
.apex-benefits-content :deep(ul) {
    list-style: none;
    padding-left: 0;
    margin: 0;
}
.apex-benefits-content :deep(li) {
    position: relative;
    padding: 6px 0 6px 22px;
    font-size: 14.5px;
    color: var(--rp-ink2);
    line-height: 1.5;
}
.apex-benefits-content :deep(li::before) {
    content: '✓';
    position: absolute;
    left: 0;
    color: var(--rp-ink);
    font-weight: 800;
}
.apex-benefits-content :deep(p) {
    color: var(--rp-ink2);
    font-size: 14.5px;
    line-height: 1.6;
}
@media (max-width: 960px) {
    .apex-benefits-photo { min-height: 240px; }
    .apex-benefits-content { padding: 26px 22px !important; }
}

/* ── Hours ──────────────────────────────────────────────────────────────── */
.hours-card .hours-row {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 9px 0;
    font-size: 14px;
    border-bottom: 1px solid var(--rp-line);
}
.hours-card .hours-row:last-child { border-bottom: none; }
.hours-card .hours-day { font-weight: 600; }

/* ── Sign-in strip ──────────────────────────────────────────────────────── */
.lp-signup-title {
    font-size: clamp(30px, 4vw, 40px);
    margin: 0 0 10px;
}
.lp-signup-copy {
    color: var(--rp-muted);
    font-size: 14.5px;
    line-height: 1.6;
    max-width: 60ch;
    margin: 0 auto 22px;
}

.rich-text-body :deep(*) { max-width: 100%; }
.rich-text-body :deep(p) {
    color: var(--rp-ink2);
    font-size: 15px;
    line-height: 1.65;
}

/* Gallery thumbnails — hairline frame, lifts on hover. */
.gallery-thumb {
    cursor: pointer;
    border-radius: 12px;
    border: 1px solid var(--rp-line);
    transition: transform 150ms ease, border-color 150ms ease;
}
.gallery-thumb:hover {
    transform: translateY(-2px);
    border-color: var(--rp-line3);
}

/* Full-screen slideshow — black background, carousel + slide expand to fill the
   viewport (toolbar is the only fixed-height element). The image is `contain`
   so portrait/landscape both fit without cropping. */
.gallery-fullscreen {
    display: flex;
    flex-direction: column;
    height: 100vh;
    background: #000 !important;
    border: 0 !important;
    border-radius: 0 !important;
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
    background: rgba(var(--rp-scrim), 0.75);
    border: 1px solid var(--rp-photo-line);
    color: var(--rp-photo-ink);
    border-radius: 10px;
    font-size: 15px;
    line-height: 1.5;
    text-align: center;
}
</style>
