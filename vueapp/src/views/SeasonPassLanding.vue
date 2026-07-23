<template>
    <div class="spl-page">
        <v-container v-if="loadError" class="py-6">
            <v-alert type="error" variant="tonal">{{ loadError }}</v-alert>
        </v-container>

        <v-container v-else-if="notFound" class="py-10 text-center">
            <v-icon icon="mdi-ticket-percent" size="44" class="text-medium-emphasis mb-3"></v-icon>
            <h1 class="text-h5 font-weight-bold mb-2">This pass isn't available</h1>
            <p class="text-body-2 text-medium-emphasis mb-4">
                It may have been unpublished or the link may be out of date.
            </p>
            <v-btn color="primary" variant="flat" to="/SeasonPasses">See all season passes</v-btn>
        </v-container>

        <v-container v-else-if="loading" class="py-6">
            <v-skeleton-loader type="image, article, actions"></v-skeleton-loader>
        </v-container>

        <template v-else-if="landing">
            <!-- Draft banner. Content presence is the staff discriminator: the server strips
                 unpublished landing content for the public (and 404s draft slugs outright),
                 so unpublished-with-content only ever renders for CatalogManage staff. A
                 public id-based widget load of a pass with no landing gets no banner. -->
            <v-alert v-if="!landing.landingPublished && (landing.landingHtml || landing.heroImageUrl)"
                type="warning" variant="flat" density="compact" class="text-center" rounded="0">
                Draft: riders can't see this page yet. Publish it from Admin &gt; Season Passes.
            </v-alert>

            <!-- ── HERO (hosted only; the embed gets a slim header) ──────────── -->
            <section v-if="!isEmbed" class="spl-hero" :style="heroStyle">
                <div class="spl-hero-overlay">
                    <v-container class="spl-hero-inner">
                        <router-link to="/SeasonPasses" class="spl-back">
                            <v-icon icon="mdi-arrow-left" size="18"></v-icon>
                            <span>All season passes</span>
                        </router-link>
                        <div class="spl-hero-bottom">
                            <h1 class="spl-title font-display text-white">{{ landing.name }}</h1>
                            <p class="spl-hero-sub text-white">
                                {{ accessLabel }} · ${{ (landing.priceCents / 100).toFixed(2) }}
                            </p>
                            <v-btn color="primary" size="large" class="mt-4" @click="scrollToBuy">
                                Buy {{ landing.name }}
                            </v-btn>
                        </div>
                    </v-container>
                </div>
            </section>

            <v-container v-else class="pt-3 pb-0">
                <h1 class="text-h6 font-weight-bold font-display">{{ landing.name }}</h1>
                <div class="text-caption text-medium-emphasis">
                    {{ accessLabel }} · ${{ (landing.priceCents / 100).toFixed(2) }}
                </div>
            </v-container>

            <v-container :class="isEmbed ? 'py-4' : 'py-8'">
                <v-row>
                    <!-- ── Marketing content + live facts ───────────────────── -->
                    <v-col cols="12" md="7" :order="isEmbed ? 2 : undefined">
                        <div class="spl-facts mb-6">
                            <div class="spl-fact">
                                <v-icon icon="mdi-ticket-confirmation" class="spl-fact-icon"></v-icon>
                                <span>{{ accessLabel }}</span>
                            </div>
                            <div class="spl-fact">
                                <v-icon icon="mdi-calendar-check" class="spl-fact-icon"></v-icon>
                                <span>Valid {{ validLabel }}</span>
                            </div>
                            <div class="spl-fact">
                                <v-icon icon="mdi-account" class="spl-fact-icon"></v-icon>
                                <span>One rider per pass, registered with photo at the gate</span>
                            </div>
                        </div>

                        <RichTextView v-if="landing.landingHtml" :html="landing.landingHtml" class="mb-6" />
                        <p v-else-if="landing.description" class="text-body-1 mb-6">{{ landing.description }}</p>

                        <section v-if="perkList.length" class="mb-6">
                            <h2 class="text-h5 font-weight-bold font-display mb-3">What's included</h2>
                            <ul class="spl-checklist">
                                <li v-for="(line, i) in perkList" :key="i">
                                    <v-icon :icon="line.icon" size="18" class="spl-check"></v-icon>
                                    <span>{{ line.text }}</span>
                                </li>
                            </ul>
                        </section>
                    </v-col>

                    <!-- ── Checkout ─────────────────────────────────────────── -->
                    <v-col cols="12" md="5" :order="isEmbed ? 1 : undefined">
                        <v-card ref="buyCard" id="spl-buy" class="spl-entry-card" variant="flat">
                            <v-card-text class="pa-5">
                                <SeasonPassCheckout :products="checkoutProducts" />
                                <div v-if="landing.requiresWaiver" class="spl-infobox mt-4">
                                    <v-icon icon="mdi-alert-circle-outline" size="20" class="spl-infobox-icon"></v-icon>
                                    <div>
                                        Every pass holder signs a waiver during checkout, and we take a photo so
                                        gate staff can confirm the pass belongs to the rider using it.
                                    </div>
                                </div>
                            </v-card-text>
                        </v-card>

                        <div v-if="isEmbed" class="text-center mt-2">
                            <a class="rp-powered" href="https://ridepass.io" target="_blank" rel="noopener">
                                Powered by <strong>RidePass</strong>
                            </a>
                        </div>
                    </v-col>
                </v-row>
            </v-container>
        </template>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import { SeasonPassService, type SeasonPassLanding, type SeasonPassProduct, type SeasonPassBenefit }
    from '@/services/SeasonPassService'
import { branding } from '@/stores/branding'
import SeasonPassCheckout from '@/components/SeasonPassCheckout.vue'
import RichTextView from '@/components/RichTextView.vue'

const route = useRoute()
const service = new SeasonPassService()

const isEmbed = computed(() => !!route.meta.embed)

const landing = ref<SeasonPassLanding | null>(null)
const loading = ref(true)
const loadError = ref('')
const notFound = ref(false)
const buyCard = ref<any>(null)

// Hosted route carries :slug; the embed route carries :id. Either resolves server-side.
const slugOrId = computed(() => String(route.params.slug ?? route.params.id ?? ''))

// SeasonPassCheckout takes the products-list shape; the landing payload carries the same
// fields plus content, so pad the two list-only fields it never reads meaningfully here.
const checkoutProducts = computed<SeasonPassProduct[]>(() =>
    landing.value ? [{ ...landing.value, isActive: true, sortOrder: 0, perks: [] }] : [])

function absoluteUrl(url: string | null): string | null {
    if (!url) return null
    if (/^https?:\/\//i.test(url)) return url
    const base = import.meta.env.VITE_API_ENDPOINT ?? ''
    return base ? new URL(url, base).toString() : url
}

const heroStyle = computed(() => {
    const url = absoluteUrl(landing.value?.heroImageUrl ?? null) || branding.heroImageUrl
    return url ? { backgroundImage: `url(${url})` } : {}
})

const accessLabel = computed(() => {
    const l = landing.value
    if (!l) return ''
    if (l.kind === 'credits') return `${l.totalCredits} ride ${l.totalCredits === 1 ? 'day' : 'days'}, any open day`
    if (l.kind === 'days_of_week') return `${daysLabel(l.validDaysOfWeek)} only`
    return 'Unlimited riding all season'
})

const validLabel = computed(() => {
    const l = landing.value
    if (!l) return ''
    return `${dayjs(l.validFromDate).format('MMM D')} to ${dayjs(l.validToDate).format('MMM D, YYYY')}`
})

function daysLabel(days: number[] | null): string {
    if (!days || days.length === 0) return ''
    const names = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
    return days.slice().sort((a, b) => a - b).map(d => names[d]).join('/')
}

function discountLabel(b: SeasonPassBenefit): string {
    return b.discountKind === 'amount'
        ? `$${(b.discountValue / 100).toFixed(2)} off`
        : `${b.discountValue / 100}% off`
}

// Same wording rules as the lineup page: benefits render from the tenant's actual config so
// the copy can't promise something this track doesn't sell; free-inclusion leads.
const perkList = computed(() => {
    const lines: { icon: string; text: string }[] = []
    const benefits = landing.value?.benefits ?? []
    const events = benefits.filter(b => b.benefitType === 'event')
    for (const b of events.filter(x => x.discountValue >= 10000 && x.discountKind === 'percent')) {
        lines.push({
            icon: 'mdi-check',
            text: b.scopeName ? `${b.scopeName} included, no entry fee` : 'Every event included, no entry fee',
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
})

function scrollToBuy() {
    document.getElementById('spl-buy')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

onMounted(async () => {
    try {
        const r = await service.getLanding(slugOrId.value)
        landing.value = (r.data as any).data
        if (landing.value) {
            document.title = `${landing.value.name} | ${branding.displayName}`
        }
    } catch (err: any) {
        if (err.response?.status === 404) {
            notFound.value = true
        } else {
            loadError.value = err.response?.data?.error
                || 'Could not load this pass. Refresh to try again, or check your connection.'
        }
    } finally {
        loading.value = false
    }
})
</script>

<style scoped>
.spl-page {
    background:
        linear-gradient(rgba(var(--v-theme-on-surface), 0.04), rgba(var(--v-theme-on-surface), 0.04)),
        rgb(var(--v-theme-background));
    min-height: 100vh;
}

/* Hero (same treatment as the season pass lineup page, so the two read as one family). */
.spl-hero {
    background-size: cover;
    background-position: center;
    background-color: rgb(var(--v-theme-secondary));
}
.spl-hero-overlay {
    background:
        linear-gradient(0deg, rgba(10, 13, 20, 0.9) 0%, rgba(10, 13, 20, 0.25) 55%, rgba(10, 13, 20, 0.45) 100%),
        linear-gradient(90deg, rgba(16, 20, 28, 0.85) 0%, rgba(16, 20, 28, 0.5) 60%, rgba(16, 20, 28, 0.2) 100%);
    min-height: 280px;
    display: flex;
}
.spl-hero-inner {
    display: flex;
    flex-direction: column;
    justify-content: space-between;
    padding-top: 1.5rem;
    padding-bottom: 2.5rem;
}
.spl-back {
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
}
.spl-back:hover { background: rgba(255, 255, 255, 0.3); }
.spl-hero-bottom { margin-top: 1.25rem; }
.spl-title {
    font-size: clamp(2rem, 5vw, 3.25rem);
    line-height: 1.05;
    font-weight: 700;
    text-shadow: 0 2px 14px rgba(0, 0, 0, 0.55);
}
.spl-hero-sub {
    font-size: 1.1rem;
    margin-top: 0.5rem;
    text-shadow: 0 2px 10px rgba(0, 0, 0, 0.5);
}

.spl-facts {
    display: flex;
    flex-direction: column;
    gap: 0.55rem;
}
.spl-fact {
    display: flex;
    align-items: center;
    gap: 0.6rem;
    color: rgba(var(--v-theme-on-surface), 0.82);
    font-size: 1rem;
}
.spl-fact-icon { color: rgb(var(--v-theme-primary)); }

.spl-checklist {
    list-style: none;
    padding: 0;
    margin: 0;
}
.spl-checklist li {
    display: flex;
    align-items: flex-start;
    gap: 0.5rem;
    padding: 0.35rem 0;
    font-size: 1rem;
}
.spl-check { color: rgb(var(--v-theme-primary)); margin-top: 1px; }

.spl-infobox {
    display: flex;
    gap: 0.75rem;
    background: rgba(232, 130, 12, 0.12);
    border: 1px solid rgba(232, 130, 12, 0.35);
    border-radius: 10px;
    padding: 1rem 1.1rem;
    font-size: 0.95rem;
    color: rgba(var(--v-theme-on-surface), 0.8);
}
.spl-infobox-icon { color: #e8820c; flex-shrink: 0; }

.spl-entry-card { border-radius: 14px; }

.rp-powered {
    font-size: 0.75rem;
    color: rgba(var(--v-theme-on-surface), 0.5);
    text-decoration: none;
}
.rp-powered:hover { color: rgba(var(--v-theme-on-surface), 0.8); text-decoration: underline; }
</style>
