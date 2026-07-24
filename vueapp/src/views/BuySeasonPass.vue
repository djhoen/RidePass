<template>
    <div class="sp-page">
        <v-container v-if="loadError" class="py-6">
            <v-alert type="error" variant="tonal">{{ loadError }}</v-alert>
        </v-container>

        <v-container v-else-if="featureDisabled" class="py-6">
            <v-alert type="info" variant="tonal">This track isn't selling season passes right now.</v-alert>
        </v-container>

        <v-container v-else-if="loading" class="py-6">
            <v-skeleton-loader type="image, article, actions"></v-skeleton-loader>
        </v-container>

        <template v-else>
            <!-- ── HERO ──────────────────────────────────────────────────────
                 Hosted site only. The embed gets the slim header below instead —
                 a full-bleed hero inside someone else's page reads as a
                 site-within-a-site. -->
            <section v-if="!isEmbed" class="sp-hero" :style="heroStyle">
                <div class="sp-hero-overlay">
                    <v-container class="sp-hero-inner">
                        <router-link to="/" class="sp-back">
                            <v-icon icon="mdi-arrow-left" size="18"></v-icon>
                            <span>Back to {{ branding.displayName }}</span>
                        </router-link>
                        <div class="sp-hero-bottom">
                            <h1 class="sp-title font-display text-white">{{ heroTitle }}</h1>
                            <p v-if="priceFromCents !== null" class="sp-hero-sub text-white">
                                Ride the whole season from ${{ (priceFromCents / 100).toFixed(2) }}
                            </p>
                        </div>
                    </v-container>
                </div>
            </section>

            <!-- ── EMBED HEADER ──────────────────────────────────────────────
                 No title/price here: the checkout panel already leads with "Choose
                 Your Pass" and the host page carries its own "Season Passes" heading,
                 so repeating it read as the phrase three times over. Only the (rare)
                 back link, shown when the visitor arrived from another widget. -->
            <v-container v-else-if="embedCameFromWidget" class="pt-3 pb-0">
                <a class="sp-back sp-back-embed" role="button" @click.prevent="router.back()">
                    <v-icon icon="mdi-arrow-left" size="16"></v-icon>
                    <span>Back</span>
                </a>
            </v-container>

            <v-container :class="isEmbed ? 'py-4' : 'py-8'">
                <v-alert v-if="products.length === 0" type="info" variant="tonal">
                    No season passes are on sale right now. Check back soon.
                </v-alert>

                <!-- Item selection (the checkout card) sits on the RIGHT, the descriptive
                     info on the LEFT, on both the hosted page and the embed. In embed, the
                     mobile stack still leads with the checkout box (order 1 below md) so a
                     narrow iframe lands buyers on the buy box rather than scrolling past the
                     details; md+ restores info-left / selection-right (order-md). -->
                <v-row v-else>
                    <!-- ── Pass details (info, left) ─────────────────────────── -->
                    <v-col cols="12" md="6" :order="isEmbed ? 2 : undefined" :order-md="isEmbed ? 1 : undefined">
                        <section class="mb-8">
                            <h2 class="text-h5 font-weight-bold font-display mb-4">Why a Season Pass</h2>
                            <ul class="sp-checklist mb-4">
                                <li v-for="(line, i) in benefitLines" :key="i">
                                    <v-icon icon="mdi-check" size="18" class="sp-check"></v-icon>
                                    <span>{{ line }}</span>
                                </li>
                            </ul>
                            <div class="sp-meta">
                                <div class="sp-meta-row">
                                    <v-icon icon="mdi-calendar-check" class="sp-meta-icon"></v-icon>
                                    <span>{{ seasonRangeLabel }}</span>
                                </div>
                                <div v-if="locationText" class="sp-meta-row">
                                    <v-icon icon="mdi-map-marker" class="sp-meta-icon"></v-icon>
                                    <span>{{ locationText }}</span>
                                </div>
                            </div>
                        </section>

                        <!-- ── Perks ──────────────────────────────────────────
                             What each pass actually grants, straight off the tenant's benefits
                             config, so the copy can't promise something this track doesn't sell.
                             Rendered whenever any pass has benefits; a pass with none is simply
                             omitted rather than shown as an empty card. -->
                        <section v-if="passesWithPerks.length" class="mb-8">
                            <h2 class="text-h5 font-weight-bold font-display mb-4">What's Included</h2>
                            <div v-for="p in passesWithPerks" :key="p.id" class="sp-perk-block mb-4">
                                <div v-if="passesWithPerks.length > 1" class="sp-section-subtitle mb-2">
                                    {{ p.name }}
                                </div>
                                <ul class="sp-checklist">
                                    <li v-for="(line, i) in perkLines(p)" :key="i">
                                        <v-icon :icon="line.icon" size="18" class="sp-check"></v-icon>
                                        <span>{{ line.text }}</span>
                                    </li>
                                </ul>
                            </div>
                        </section>

                        <!-- Pricing: every pass on sale. Hidden in embed — the checkout card
                             already lists each one with its price, so this would be duplicate
                             scroll there. -->
                        <section v-if="!isEmbed" class="mb-8">
                            <h2 class="text-h5 font-weight-bold font-display mb-4">Passes &amp; Pricing</h2>
                            <p v-if="hasServiceFee" class="text-body-2 text-medium-emphasis mb-4">
                                All passes include a {{ serviceFeePercent }}% service fee.
                            </p>
                            <div v-for="p in products" :key="p.id" class="sp-price-block mb-4">
                                <div class="sp-price-row">
                                    <span class="sp-price-name">{{ p.name }}</span>
                                    <span class="sp-price-amt">${{ (p.priceCents / 100).toFixed(2) }}</span>
                                </div>
                                <div class="text-caption text-medium-emphasis">
                                    {{ accessLabel(p) }} · {{ validLabel(p) }}
                                </div>
                                <div v-if="p.description" class="text-body-2 mt-1">{{ p.description }}</div>
                                <router-link v-if="p.landingPublished && p.slug" class="sp-link"
                                    :to="`/SeasonPasses/${p.slug}`">
                                    Learn more about {{ p.name }} &rarr;
                                </router-link>
                            </div>
                        </section>
                    </v-col>

                    <!-- ── Checkout (item selection, right), then about ──────── -->
                    <v-col cols="12" md="6" :order="isEmbed ? 1 : undefined" :order-md="isEmbed ? 2 : undefined">
                        <v-card class="sp-entry-card" variant="flat">
                            <v-card-text class="pa-5">
                                <SeasonPassCheckout :products="products" />

                                <div v-if="waiverNote" class="sp-infobox mt-4">
                                    <v-icon icon="mdi-alert-circle-outline" size="20" class="sp-infobox-icon"></v-icon>
                                    <div>{{ waiverNote }}</div>
                                </div>
                            </v-card-text>
                        </v-card>

                        <!-- Embed only: attribution under the checkout. Opens in a new tab so it
                             never navigates the iframe out of the track's flow. -->
                        <div v-if="isEmbed" class="text-center mt-2">
                            <a class="rp-powered" href="https://ridepass.io" target="_blank" rel="noopener">
                                Powered by <strong>RidePass</strong>
                            </a>
                        </div>

                        <!-- Hidden in embed: it would describe the very site the widget sits on. -->
                        <section v-if="!isEmbed && (aboutHtml || aboutPhoto)" class="mt-8">
                            <h2 class="text-h5 font-weight-bold font-display mb-4">About {{ branding.displayName }}</h2>
                            <div v-if="aboutHtml" class="rich-text-body mb-3" v-html="aboutHtml"></div>
                            <a class="sp-link" href="/">Visit track home &rarr;</a>
                            <div v-if="aboutPhoto" class="sp-about-photo mt-4"
                                :style="{ backgroundImage: `url(${aboutPhoto})` }"></div>
                        </section>
                    </v-col>
                </v-row>
            </v-container>
        </template>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import dayjs from 'dayjs'
import DOMPurify from 'dompurify'
import { SeasonPassService, type SeasonPassProduct, type SeasonPassBenefit } from '@/services/SeasonPassService'
import { branding } from '@/stores/branding'
import SeasonPassCheckout from '@/components/SeasonPassCheckout.vue'

const route = useRoute()
const router = useRouter()

// Embed mode (/embed/seasonpasses, /embed/seasonpass/:id): framed on a track's own site, so
// no hero and no navigation out of the checkout flow. The only escape hatch is Back, and only
// when the visitor arrived from another embedded widget in this same iframe.
const isEmbed = computed(() => !!route.meta.embed)
const embedCameFromWidget: boolean = (() => {
    const back = String(window.history.state?.back ?? '')
    return back.startsWith('/embed/')
})()

const service = new SeasonPassService()
const products = ref<SeasonPassProduct[]>([])
const loading = ref(true)
const loadError = ref('')
const featureDisabled = ref(false)

const aboutHtml = computed(() => DOMPurify.sanitize(branding.aboutHtml || ''))
const aboutPhoto = computed(() => branding.secondaryHeroUrl || branding.heroImageUrl || null)
const heroStyle = computed(() =>
    branding.heroImageUrl ? { backgroundImage: `url(${branding.heroImageUrl})` } : {})

const heroTitle = 'Season Passes'

const priceFromCents = computed(() =>
    products.value.length === 0 ? null : Math.min(...products.value.map(p => p.priceCents)))

const locationText = computed(() => {
    const cityLine = [branding.city, [branding.region, branding.postalCode].filter(Boolean).join(' ')]
        .filter(p => p && p.trim()).join(', ')
    const parts = [branding.addressLine, cityLine].filter(p => p && p.trim())
    return parts.length > 0 ? parts.join(', ') : branding.displayName || ''
})

// The season's outer bounds across every pass on sale — the "when is this good for" answer
// a buyer wants before reading individual passes.
const seasonRangeLabel = computed(() => {
    if (products.value.length === 0) return ''
    const from = products.value.map(p => dayjs(p.validFromDate)).sort((a, b) => a.valueOf() - b.valueOf())[0]
    const to = products.value.map(p => dayjs(p.validToDate)).sort((a, b) => b.valueOf() - a.valueOf())[0]
    return `Good for the ${from.format('MMM D, YYYY')} to ${to.format('MMM D, YYYY')} season`
})

// Benefits are derived from what the passes on sale actually are, so the copy can't promise
// something this track doesn't sell. Per-event-type perks are deliberately NOT advertised:
// they're configurable today but checkout doesn't apply them yet, so naming them here would
// sell a discount the buyer wouldn't get.
const benefitLines = computed(() => {
    const lines: string[] = []
    if (products.value.some(p => p.kind === 'unlimited')) {
        lines.push('Unlimited riding all season — one price, no per-visit gate fee.')
    }
    if (products.value.some(p => p.kind === 'credits')) {
        lines.push('Credit passes: buy a block of rides up front and use them whenever you like.')
    }
    if (products.value.some(p => p.kind === 'days_of_week')) {
        lines.push('Weekday passes for riders with a flexible schedule, at a lower price.')
    }
    lines.push('One pass per rider — buy for the whole family in a single checkout.')
    lines.push('Scan your QR code at the gate. No paperwork on the day.')
    return lines
})

// ── Perks ────────────────────────────────────────────────────────────────────
const passesWithPerks = computed(() => products.value.filter(p => (p.benefits?.length ?? 0) > 0))

function discountLabel(b: SeasonPassBenefit): string {
    return b.discountKind === 'amount'
        ? `$${(b.discountValue / 100).toFixed(2)} off`
        : `${b.discountValue / 100}% off`
}

interface PerkLine { icon: string; text: string }

// Benefits are stored generically, so the wording is built per surface here rather than by the
// tenant. 10000 bps = 100% = included free, which reads very differently from a discount and is
// the strongest thing a pass offers, so it leads.
function perkLines(p: SeasonPassProduct): PerkLine[] {
    const lines: PerkLine[] = []
    const benefits = p.benefits ?? []

    const events = benefits.filter(b => b.benefitType === 'event')
    for (const b of events.filter(x => x.discountValue >= 10000 && x.discountKind === 'percent')) {
        lines.push({
            icon: 'mdi-check',
            text: b.scopeName ? `${b.scopeName} included — no entry fee` : 'Every event included — no entry fee',
        })
    }
    for (const b of events.filter(x => !(x.discountValue >= 10000 && x.discountKind === 'percent'))) {
        lines.push({
            icon: 'mdi-sale',
            text: b.scopeName ? `${discountLabel(b)} ${b.scopeName}` : `${discountLabel(b)} event entry`,
        })
    }
    for (const b of benefits.filter(b => b.benefitType === 'concession')) {
        lines.push({ icon: 'mdi-silverware-fork-knife', text: `${discountLabel(b)} food & drink` })
    }
    for (const b of benefits.filter(b => b.benefitType === 'rental')) {
        lines.push({ icon: 'mdi-motorbike', text: `${discountLabel(b)} bike & gear rentals` })
    }
    for (const b of benefits.filter(b => b.benefitType === 'retail')) {
        lines.push({ icon: 'mdi-bike', text: `${discountLabel(b)} in the bike shop` })
    }
    for (const b of benefits.filter(b => b.benefitType === 'buddy_pass')) {
        const n = b.quantity ?? 0
        lines.push({
            icon: 'mdi-account-multiple',
            text: `${n} buddy ${n === 1 ? 'pass' : 'passes'} a season: bring a friend at ${discountLabel(b)}`,
        })
    }
    return lines
}

const serviceFeePercent = computed(() => (branding.serviceChargeBps ?? 0) / 100)
const hasServiceFee = computed(() =>
    (branding.serviceChargeBps ?? 0) > 0
    && products.value.some(p => (p.riderPaidServiceChargeBps ?? 10000) > 0))

const waiverNote = computed(() =>
    products.value.some(p => p.requiresWaiver)
        ? 'Every pass holder signs a waiver during checkout, and we take a photo so gate staff '
          + 'can confirm the pass belongs to the rider using it.'
        : '')

function accessLabel(p: SeasonPassProduct): string {
    if (p.kind === 'days_of_week') return `${daysLabel(p.validDaysOfWeek)} only`
    if (p.kind === 'credits') return `${p.totalCredits} ride credits`
    return 'Unlimited rides'
}
function daysLabel(days: number[] | null): string {
    if (!days || days.length === 0) return ''
    const names = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
    return days.slice().sort((a, b) => a - b).map(d => names[d]).join('/')
}
function validLabel(p: SeasonPassProduct): string {
    return `valid ${dayjs(p.validFromDate).format('MMM D')} to ${dayjs(p.validToDate).format('MMM D, YYYY')}`
}

onMounted(async () => {
    // Feature off: explain in-page rather than silently bouncing home (consistent with how
    // rentals / gift cards report a disabled feature).
    if (branding.loaded && !branding.seasonPassesEnabled) {
        featureDisabled.value = true
        loading.value = false
        return
    }
    try {
        const r = await service.listActive()
        products.value = (r.data as any).data ?? []
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            || 'Could not load season passes. Refresh to try again, or check your connection.'
    } finally {
        loading.value = false
    }
})
</script>

<style scoped>
/* Theme-aware surface + text colors so dark-mode tenants (theme_mode = 'dark') get a dark page
   with light text instead of inheriting invisible headings. The page is the plain theme
   background (no gray tint overlay) so the widget blends into the host site it's embedded on. */
.sp-page {
    background: rgb(var(--v-theme-background));
    min-height: 100vh;
}

/* ── Hero ───────────────────────────────────────────────────────────────── */
.sp-hero {
    background-size: cover;
    background-position: center;
    background-color: rgb(var(--v-theme-secondary));
}
.sp-hero-overlay {
    /* Bottom-up darken (where the title sits) over a left-to-right darken, so text stays
       legible over any photo. */
    background:
        linear-gradient(0deg, rgba(10, 13, 20, 0.9) 0%, rgba(10, 13, 20, 0.25) 55%, rgba(10, 13, 20, 0.45) 100%),
        linear-gradient(90deg, rgba(16, 20, 28, 0.85) 0%, rgba(16, 20, 28, 0.5) 60%, rgba(16, 20, 28, 0.2) 100%);
    min-height: 220px;
    display: flex;
}
.sp-hero-inner {
    display: flex;
    flex-direction: column;
    justify-content: space-between;
    padding-top: 1.5rem;
    padding-bottom: 2.5rem;
}
/* Back link as a translucent pill so it stays visible over light or busy photos. */
.sp-back {
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
    cursor: pointer;
}
.sp-back:hover { background: rgba(255, 255, 255, 0.3); }
/* Embed header variant: primary-tinted so it reads on light and dark themes alike. */
.sp-back-embed {
    color: rgb(var(--v-theme-primary));
    background: rgba(var(--v-theme-primary), 0.08);
    border-color: rgba(var(--v-theme-primary), 0.35);
}
.sp-back-embed:hover { background: rgba(var(--v-theme-primary), 0.16); }
.sp-hero-bottom { margin-top: 1rem; }
.sp-title {
    font-size: clamp(2rem, 5vw, 3.25rem);
    line-height: 1.05;
    font-weight: 700;
    text-shadow: 0 2px 14px rgba(0, 0, 0, 0.55);
}
.sp-hero-sub {
    font-size: 1.1rem;
    margin-top: 0.5rem;
    text-shadow: 0 2px 10px rgba(0, 0, 0, 0.5);
}

.rp-powered {
    font-size: 0.75rem;
    color: rgba(var(--v-theme-on-surface), 0.5);
    text-decoration: none;
}
.rp-powered:hover { color: rgba(var(--v-theme-on-surface), 0.8); text-decoration: underline; }

/* ── Meta ───────────────────────────────────────────────────────────────── */
.sp-meta {
    display: flex;
    flex-direction: column;
    gap: 0.55rem;
    width: fit-content;
    max-width: 100%;
}
.sp-meta-row {
    display: flex;
    align-items: center;
    gap: 0.6rem;
    color: rgba(var(--v-theme-on-surface), 0.82);
    font-size: 1rem;
}
.sp-meta-icon { color: rgb(var(--v-theme-primary)); }

/* ── Pricing list ───────────────────────────────────────────────────────── */
.sp-price-block {
    padding-bottom: 0.6rem;
    border-bottom: 1px dashed rgba(var(--v-theme-on-surface), 0.08);
}
.sp-price-block:last-child { border-bottom: none; }
.sp-price-row {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: 1rem;
}
.sp-price-name { color: rgba(var(--v-theme-on-surface), 0.9); font-weight: 600; }
.sp-price-amt {
    font-weight: 700;
    white-space: nowrap;
    color: rgb(var(--v-theme-primary));
}

/* ── Perks ──────────────────────────────────────────────────────────────── */
.sp-section-subtitle {
    text-transform: uppercase;
    letter-spacing: 0.06em;
    font-size: 0.78rem;
    font-weight: 700;
    color: rgba(var(--v-theme-on-surface), 0.6);
}
.sp-perk-block:last-child { margin-bottom: 0 !important; }

/* ── Benefits checklist ─────────────────────────────────────────────────── */
.sp-checklist {
    list-style: none;
    padding: 0;
    margin: 0;
}
.sp-checklist li {
    display: flex;
    align-items: flex-start;
    gap: 0.5rem;
    padding: 0.35rem 0;
    font-size: 1rem;
}
.sp-check { color: rgb(var(--v-theme-primary)); margin-top: 1px; }

/* ── Important info box ─────────────────────────────────────────────────── */
.sp-infobox {
    display: flex;
    gap: 0.75rem;
    /* Alpha-tinted orange reads as the same soft warning band on light and dark. */
    background: rgba(232, 130, 12, 0.12);
    border: 1px solid rgba(232, 130, 12, 0.35);
    border-radius: 10px;
    padding: 1rem 1.1rem;
    font-size: 0.95rem;
    color: rgba(var(--v-theme-on-surface), 0.8);
}
.sp-infobox-icon { color: #e8820c; flex-shrink: 0; }

/* ── About + photo ──────────────────────────────────────────────────────── */
.sp-link {
    color: rgb(var(--v-theme-primary));
    text-decoration: none;
    font-weight: 600;
    font-size: 0.9rem;
}
.sp-link:hover { text-decoration: underline; }
.sp-about-photo {
    width: 100%;
    height: 220px;
    border-radius: 12px;
    background-size: cover;
    background-position: center;
}
.rich-text-body :deep(*) { max-width: 100%; }

/* ── Entry card (the item-selection container) ──────────────────────────────
   A very light gray surface on light themes (a little darker than the white page) and a
   little LIGHTER than the background on dark themes so the pick-your-pass box reads as the
   actionable panel. A drop shadow looked harsh on the white page, so the panel is defined
   by a hairline border instead: on-surface at 0.2 renders to exactly #cccccc on a white
   page and stays a subtle hairline on dark themes. on-surface alpha inverts per theme to
   give both directions from one rule; !important on the fill beats Vuetify's flat variant. */
.sp-entry-card {
    border-radius: 14px;
    background-color: rgba(var(--v-theme-on-surface), 0.05) !important;
    border: 1px solid rgba(var(--v-theme-on-surface), 0.2);
}
</style>
