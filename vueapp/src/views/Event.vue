<template>
    <div class="evt-page">
        <v-container v-if="loadError" class="py-6">
            <v-alert type="error" variant="tonal">{{ loadError }}</v-alert>
        </v-container>

        <template v-else-if="event">
            <!-- ── HERO ──────────────────────────────────────────────────────
                 Dark band with the event image fading in from the right; title +
                 date / location / type meta on the left. -->
            <section class="evt-hero" :style="heroStyle">
                <div class="evt-hero-overlay">
                    <v-container class="evt-hero-inner">
                        <router-link to="/Events" class="evt-back">
                            <v-icon icon="mdi-arrow-left" size="18"></v-icon>
                            <span>Back to Events</span>
                        </router-link>
                        <div class="evt-hero-bottom">
                            <h1 class="evt-title font-display text-white">{{ event.title }}</h1>
                        </div>
                    </v-container>
                </div>
            </section>

            <v-container class="py-8">
                <v-alert v-if="event.status === 'cancelled'" type="error" variant="tonal" class="mb-6">
                    This event has been cancelled.
                </v-alert>

                <v-row>
                    <!-- ── LEFT: event content ─────────────────────────────── -->
                    <v-col cols="12" md="6">
                        <section class="mb-8">
                            <h2 class="text-h5 font-weight-bold font-display mb-4">Event Details</h2>
                            <ul v-if="detailLines.length > 0" class="evt-checklist mb-4">
                                <li v-for="(line, i) in detailLines" :key="i">
                                    <v-icon icon="mdi-check" size="18" class="evt-check"></v-icon>
                                    <span>{{ line }}</span>
                                </li>
                            </ul>
                            <div class="evt-meta">
                                <div class="evt-meta-row">
                                    <v-icon icon="mdi-calendar" class="evt-meta-icon"></v-icon>
                                    <span>{{ dateLine }}</span>
                                </div>
                                <div v-if="locationText" class="evt-meta-row">
                                    <v-icon icon="mdi-map-marker" class="evt-meta-icon"></v-icon>
                                    <span>{{ locationText }}</span>
                                </div>
                            </div>
                        </section>

                        <section v-if="schedule.length > 0" class="mb-8">
                            <h2 class="text-h5 font-weight-bold font-display mb-4">Event Schedule</h2>
                            <div class="evt-schedule">
                                <div v-for="(row, i) in schedule" :key="i" class="evt-schedule-row">
                                    <div class="evt-schedule-time">{{ row.time }}</div>
                                    <div class="evt-schedule-label">{{ row.label }}</div>
                                </div>
                            </div>
                        </section>

                        <!-- Pricing: everything purchasable for this event. -->
                        <section v-if="pricingGroups.length" class="mb-8">
                            <h2 class="text-h5 font-weight-bold font-display mb-4">Pricing &amp; Passes</h2>
                            <div v-for="(group, gi) in pricingGroups" :key="group.label"
                                :class="gi > 0 ? 'mt-4' : ''">
                                <div class="evt-section-subtitle mb-2">{{ group.label }}</div>
                                <div v-for="row in group.items" :key="row.key" class="evt-price-row pl-4">
                                    <span class="evt-price-name">{{ row.name }}</span>
                                    <span class="evt-price-amt">{{ row.price }}</span>
                                </div>
                            </div>
                        </section>
                    </v-col>

                    <!-- ── RIGHT: entry options + checkout, then about ─────── -->
                    <v-col cols="12" md="6">
                        <v-card class="evt-entry-card" variant="flat">
                            <v-card-text class="pa-5">
                                <!-- Unified inline checkout: select tiers, pay, then register.
                                     The rider never leaves the event page. -->
                                <EventCheckout v-if="hasActiveTiers" :event="event" :tiers="tiers" />
                                <div v-else class="text-body-2 text-medium-emphasis">
                                    No entry options are available for this event yet.
                                </div>

                                <!-- Waiver notice, driven by the event's rider/spectator
                                     waiver settings. Safety-gear rules live in the waiver itself. -->
                                <div v-if="waiverNote" class="evt-infobox mt-4">
                                    <v-icon icon="mdi-alert-circle-outline" size="20" class="evt-infobox-icon"></v-icon>
                                    <div>{{ waiverNote }}</div>
                                </div>
                            </v-card-text>
                        </v-card>

                        <section v-if="aboutHtml || aboutPhoto" class="mt-8">
                            <h2 class="text-h5 font-weight-bold font-display mb-4">About {{ branding.displayName }}</h2>
                            <div v-if="aboutHtml" class="rich-text-body mb-3" v-html="aboutHtml"></div>
                            <a class="evt-link" href="/">Visit track home &rarr;</a>
                            <div v-if="aboutPhoto" class="evt-about-photo mt-4"
                                :style="{ backgroundImage: `url(${aboutPhoto})` }"></div>
                        </section>
                    </v-col>
                </v-row>
            </v-container>
        </template>

        <v-container v-else class="py-6">
            <v-skeleton-loader type="image, article, actions"></v-skeleton-loader>
        </v-container>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import { EventService, type EventDto } from '@/services/EventService'
import { TicketService, type TicketTier } from '@/services/TicketService'
import { branding } from '@/stores/branding'
import EventCheckout from '@/components/EventCheckout.vue'

const route = useRoute()
const service = new EventService()
const ticketService = new TicketService()
const event = ref<EventDto | null>(null)
const tiers = ref<TicketTier[]>([])
const loadError = ref('')

const apiUrl: string = (import.meta as any).env?.VITE_API_ENDPOINT ?? ''
function imgUrl(url: string | null | undefined): string | null {
    if (!url) return null
    if (/^https?:\/\//i.test(url)) return url
    try { return `${new URL(apiUrl, window.location.origin).origin}${url}` } catch { return url }
}

const heroStyle = computed(() => {
    const url = imgUrl(event.value?.imageUrl) || branding.heroImageUrl
    return url ? { backgroundImage: `url(${url})` } : {}
})

// Prefer the track's full street address; fall back to the event's location
// label, then the track name.
const locationText = computed(() => {
    const cityLine = [branding.city, [branding.region, branding.postalCode].filter(Boolean).join(' ')]
        .filter(p => p && p.trim()).join(', ')
    const parts = [branding.addressLine, cityLine].filter(p => p && p.trim())
    if (parts.length > 0) return parts.join(', ')
    return event.value?.locationLabel || branding.displayName || ''
})

// About-the-track content is the tenant's branding (shared across events).
const aboutHtml = computed(() => branding.aboutHtml || '')
const aboutPhoto = computed(() => branding.secondaryHeroUrl || branding.heroImageUrl || null)

// The event description doubles as the "Event Details" checklist — one bullet
// per non-empty line, matching the mock's bulleted layout.
const detailLines = computed(() =>
    (event.value?.description || '').split('\n').map(s => s.trim()).filter(Boolean))

// Waiver notice text, built from the event's rider/spectator waiver settings.
// Safety-gear requirements are intentionally left to the waiver itself.
const waiverNote = computed(() => {
    const rider = !!event.value?.requiresRiderWaiver
    const spectator = !!event.value?.requiresSpectatorWaiver
    let who = ''
    if (rider && spectator) who = 'Racers and spectators must sign a waiver before entry.'
    else if (rider) who = 'Racers must sign a waiver before riding before entry.'
    else if (spectator) who = 'Spectators must sign a waiver before entry.'
    else return ''
    return `${who} You'll be asked to sign during checkout if it isn't already on file.`
})

const schedule = computed(() => event.value?.schedule ?? [])

// The unified checkout renders whenever the event has any active tier (rider or gate).
const hasActiveTiers = computed(() => tiers.value.some(t => t.isActive))

// ── Pricing list ─────────────────────────────────────────────────────────────
// Everything purchasable for this event, grouped the way the checkout sells it:
// race classes (tiers), spectator Gate Fees, riding day passes, and other add-ons.
// Spectator_pass tiers are intentionally omitted — this page sells spectator entry
// as a Gate Fee, so listing a tier price you can't buy here would mislead.
function fmtPrice(cents: number): string {
    return cents === 0 ? 'Free' : `$${(cents / 100).toFixed(2)}`
}
const isGateFeeExtra = (e: { kind: string; name: string }) =>
    e.kind === 'gate_fee' || e.name.trim().toLowerCase() === 'gate fee'

interface PriceRow { key: string; name: string; price: string }
interface PriceGroup { label: string; items: PriceRow[] }
const pricingGroups = computed<PriceGroup[]>(() => {
    const groups: PriceGroup[] = []
    const extras = event.value?.eligibleExtras ?? []
    const activeTiers = tiers.value.filter(t => t.isActive)

    const raceTiers = activeTiers.filter(t => t.kind === 'race_entry')
    if (raceTiers.length) {
        groups.push({
            label: 'Race Classes',
            items: raceTiers.map(t => ({ key: t.id, name: t.name, price: fmtPrice(t.priceCents) })),
        })
    }

    const riderGate = activeTiers.filter(t => t.kind === 'gate_fee' && t.audience === 'rider')
    if (riderGate.length) {
        groups.push({
            label: 'Rider Gate',
            items: riderGate.map(t => ({ key: t.id, name: t.name, price: fmtPrice(t.priceCents) })),
        })
    }

    const spectatorGate = activeTiers.filter(t => t.kind === 'gate_fee' && t.audience === 'spectator')
    if (spectatorGate.length) {
        groups.push({
            label: 'Spectator Gate',
            items: spectatorGate.map(t => ({ key: t.id, name: t.name, price: fmtPrice(t.priceCents) })),
        })
    }

    // Add-ons: real extras only (the legacy gate-fee extra is excluded — gate fees are tiers now).
    const addons = extras.filter(e => !isGateFeeExtra(e))
    if (addons.length) {
        groups.push({
            label: 'Add-ons',
            items: addons.map(e => ({ key: e.productId, name: e.name, price: fmtPrice(e.priceCents) })),
        })
    }

    return groups
})

const dateLine = computed(() => {
    if (!event.value) return ''
    const tz = branding.timezone || 'UTC'
    const start = dayjs.utc(event.value.startsAtUtc).tz(tz)
    const end = dayjs.utc(event.value.endsAtUtc).tz(tz)
    if (event.value.allDay) {
        return start.isSame(end, 'day')
            ? start.format('dddd, MMMM D, YYYY')
            : `${start.format('MMM D')} – ${end.format('MMM D, YYYY')}`
    }
    if (start.isSame(end, 'day')) {
        return `${start.format('dddd, MMMM D, YYYY')} · ${start.format('h:mm A')} – ${end.format('h:mm A')}`
    }
    return `${start.format('MMM D, h:mm A')} – ${end.format('MMM D, YYYY h:mm A')}`
})

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

onMounted(async () => {
    const eventId = route.params.id as string
    try {
        const r = await service.getPublic(eventId)
        event.value = (r.data as any).data
    } catch (err: any) {
        loadError.value = err.response?.data?.error || 'This event is not available.'
        return
    }
    // Race-class prices aren't in the public event payload, so pull the active
    // tiers for the Pricing section. Only needed when the event has race entries.
    if (event.value?.hasRaceEntryTiers) {
        try {
            const tr = await ticketService.listActiveTiers(eventId)
            tiers.value = (tr.data as any).data ?? []
        } catch {
            tiers.value = []
        }
    }
})
</script>

<style scoped>
.evt-page {
    background-color: #f5f5f5;
    min-height: 100vh;
}

/* ── Hero ───────────────────────────────────────────────────────────────── */
.evt-hero {
    background-size: cover;
    background-position: center;
    background-color: rgb(var(--v-theme-secondary));
}
.evt-hero-overlay {
    /* Bottom-up darken (where the title + meta sit) layered over a left-to-right
       darken, so text and icons stay legible over any photo. */
    background:
        linear-gradient(0deg, rgba(10, 13, 20, 0.9) 0%, rgba(10, 13, 20, 0.25) 55%, rgba(10, 13, 20, 0.45) 100%),
        linear-gradient(90deg, rgba(16, 20, 28, 0.85) 0%, rgba(16, 20, 28, 0.5) 60%, rgba(16, 20, 28, 0.2) 100%);
    min-height: 220px;
    display: flex;
}
.evt-hero-inner {
    display: flex;
    flex-direction: column;
    justify-content: space-between;
    padding-top: 1.5rem;
    padding-bottom: 2.5rem;
}
/* Back link as a translucent pill so it's visible over light or busy photos. */
.evt-back {
    align-self: flex-start;
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    color: #fff;
    background: rgba(255, 255, 255, 0.16);
    border: 1px solid rgba(255, 255, 255, 0.3);
    border-radius: 999px;
    padding: 6px 14px 6px 10px;
    font-size: 0.85rem;
    font-weight: 600;
    text-decoration: none;
    backdrop-filter: blur(4px);
    transition: background-color 0.15s ease;
}
.evt-back:hover { background: rgba(255, 255, 255, 0.3); }
.evt-hero-bottom { margin-top: 1rem; }
.evt-title {
    font-size: clamp(2rem, 5vw, 3.25rem);
    line-height: 1.05;
    font-weight: 700;
    text-shadow: 0 2px 14px rgba(0, 0, 0, 0.55);
}
/* Meta now lives in the Event Details column on a light page, so it's a plain
   stacked list: dark text with the brand-colored icons. */
.evt-meta {
    display: flex;
    flex-direction: column;
    gap: 0.55rem;
    width: fit-content;
    max-width: 100%;
}
.evt-meta-row {
    display: flex;
    align-items: center;
    gap: 0.6rem;
    color: rgba(0, 0, 0, 0.82);
    font-size: 1rem;
}
.evt-meta-icon { color: rgb(var(--v-theme-primary)); }

/* ── Section sub-heading (used for "Day Passes" etc.) ───────────────────── */
.evt-section-subtitle {
    text-transform: uppercase;
    letter-spacing: 0.06em;
    font-size: 0.78rem;
    font-weight: 700;
    color: rgba(0, 0, 0, 0.6);
}

/* ── Pricing list ───────────────────────────────────────────────────────── */
.evt-price-row {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: 1rem;
    padding: 0.3rem 0;
    border-bottom: 1px dashed rgba(0, 0, 0, 0.08);
}
.evt-price-row:last-child { border-bottom: none; }
.evt-price-name { color: rgba(0, 0, 0, 0.82); }
.evt-price-amt {
    font-weight: 700;
    white-space: nowrap;
    color: rgb(var(--v-theme-primary));
}

/* ── Event details checklist ────────────────────────────────────────────── */
.evt-checklist {
    list-style: none;
    padding: 0;
    margin: 0;
}
.evt-checklist li {
    display: flex;
    align-items: flex-start;
    gap: 0.5rem;
    padding: 0.35rem 0;
    font-size: 1rem;
}
.evt-check { color: rgb(var(--v-theme-primary)); margin-top: 1px; }

/* ── Important info box ─────────────────────────────────────────────────── */
.evt-infobox {
    display: flex;
    gap: 0.75rem;
    background: #fff6ed;
    border: 1px solid #ffd9b3;
    border-radius: 10px;
    padding: 1rem 1.1rem;
    font-size: 0.95rem;
    color: rgba(0, 0, 0, 0.8);
}
.evt-infobox-icon { color: #e8820c; flex-shrink: 0; }

/* ── Schedule ───────────────────────────────────────────────────────────── */
.evt-schedule {
    border: 1px solid rgba(0, 0, 0, 0.08);
    border-radius: 10px;
    overflow: hidden;
}
.evt-schedule-row {
    display: flex;
    gap: 1rem;
    padding: 0.65rem 1rem;
}
.evt-schedule-row:nth-child(odd) { background: rgba(0, 0, 0, 0.03); }
.evt-schedule-time {
    width: 116px;
    flex-shrink: 0;
    font-weight: 700;
    color: rgb(var(--v-theme-primary));
}
.evt-schedule-label { color: rgba(0, 0, 0, 0.8); }

/* ── About + photo ──────────────────────────────────────────────────────── */
.evt-link {
    color: rgb(var(--v-theme-primary));
    text-decoration: none;
    font-weight: 600;
    font-size: 0.9rem;
}
.evt-link:hover { text-decoration: underline; }
.evt-about-photo {
    width: 100%;
    height: 220px;
    border-radius: 12px;
    background-size: cover;
    background-position: center;
}
.rich-text-body :deep(*) { max-width: 100%; }

/* ── Entry card ─────────────────────────────────────────────────────────── */
.evt-entry-card {
    border-radius: 14px;
}
.evt-choice {
    display: flex;
    gap: 0.75rem;
}
.evt-choice-card {
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    border: 2px solid rgba(var(--v-theme-primary), 0.4);
    border-radius: 12px;
    padding: 1.25rem 0.75rem 1.1rem;
    text-align: center;
    background: rgba(var(--v-theme-primary), 0.05);
    cursor: pointer;
    transition: transform 0.12s ease, border-color 0.15s ease, box-shadow 0.15s ease, background-color 0.15s ease;
}
.evt-choice-card:hover {
    border-color: rgb(var(--v-theme-primary));
    background: rgba(var(--v-theme-primary), 0.1);
    box-shadow: 0 6px 18px rgba(0, 0, 0, 0.14);
    transform: translateY(-2px);
}
.evt-choice-card:active {
    transform: translateY(0);
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.12);
}
.evt-choice-card:focus-visible {
    outline: 3px solid rgba(var(--v-theme-primary), 0.5);
    outline-offset: 2px;
}
.evt-choice-card :deep(.v-icon) { color: rgb(var(--v-theme-primary)); }
/* Explicit call-to-action pill so the card reads unmistakably as a button. */
.evt-choice-cta {
    margin-top: 0.9rem;
    display: inline-block;
    background: rgb(var(--v-theme-primary));
    color: #fff;
    font-weight: 700;
    font-size: 0.8rem;
    letter-spacing: 0.02em;
    padding: 0.4rem 1.15rem;
    border-radius: 999px;
}
</style>
