<template>
    <div class="menu-board" :style="boardStyle">
        <div class="board-inner">
            <div class="d-flex flex-column align-center mb-4">
                <v-img v-if="logo" :src="logo" max-height="80" max-width="240" class="mb-2" />
                <h1 class="board-title" :style="accentStyle">{{ title }}</h1>
            </div>

            <!-- Auto-rotating hero carousel: promo callout tiles ("Make it a combo") mixed with
                 product photos (only items flagged for the carousel, with a photo, in stock) -->
            <v-carousel v-if="settings.showCarousel && slides.length" cycle
                :interval="(settings.carouselSeconds || 5) * 1000" hide-delimiter-background
                height="300" class="mb-6 rounded-xl elevation-4">
                <v-carousel-item v-for="s in slides" :key="s.key" :src="s.imageUrl ?? undefined" cover>
                    <!-- Text-only promo: full-bleed callout on the accent color -->
                    <div v-if="s.kind === 'promo' && !s.imageUrl" class="promo-tile" :style="promoTileStyle">
                        <span class="promo-tile__title">{{ s.title }}</span>
                        <span v-if="s.subtitle" class="promo-tile__subtitle">{{ s.subtitle }}</span>
                    </div>
                    <!-- Promo over an image: centered callout with a dark scrim for contrast -->
                    <div v-else-if="s.kind === 'promo'" class="promo-tile promo-tile--overlay">
                        <span class="promo-tile__title">{{ s.title }}</span>
                        <span v-if="s.subtitle" class="promo-tile__subtitle">{{ s.subtitle }}</span>
                    </div>
                    <!-- Product photo slide -->
                    <div v-else class="carousel-caption">
                        <span class="text-h3 font-weight-bold">{{ s.title }}</span>
                        <span class="text-h4 font-weight-bold">{{ s.subtitle }}</span>
                    </div>
                </v-carousel-item>
            </v-carousel>

            <div v-if="products.length === 0" class="text-center text-h5 text-medium-emphasis pa-12">
                Menu coming soon.
            </div>

            <!-- Compact multi-column board so the whole menu fits one screen, like a printed/QSR board. -->
            <div class="board-sections">
                <section v-for="g in groups" :key="g.key" class="board-section">
                    <h2 class="board-section-title" :style="accentBorder">{{ g.name }}</h2>
                    <div v-for="p in g.items" :key="p.id" class="board-item" :class="{ 'board-item--out': p.soldOut }">
                        <div class="board-item__row">
                            <span class="board-item__name">{{ p.name }}</span>
                            <span class="board-item__dots"></span>
                            <span class="board-item__price" :style="accentStyle">{{ p.soldOut ? 'Sold out' : priceLabel(p) }}</span>
                        </div>
                        <div v-if="p.description" class="board-item__desc">{{ p.description }}</div>
                    </div>
                </section>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { ConcessionProduct, ConcessionMenuSettings, ConcessionMenuPromo } from '@/services/ConcessionService'

const props = defineProps<{
    products: ConcessionProduct[]
    settings: ConcessionMenuSettings
    title: string
    promos?: ConcessionMenuPromo[]
    fallbackLogo?: string | null
    fallbackAccent?: string | null
}>()

const logo = computed(() => props.settings.logoUrl || props.fallbackLogo || null)
const boardStyle = computed(() => {
    const s: Record<string, string> = {}
    if (props.settings.backgroundColor) s.backgroundColor = props.settings.backgroundColor
    if (props.settings.textColor) s.color = props.settings.textColor
    return s
})
const accentColor = computed(() => props.settings.accentColor || props.fallbackAccent || null)
const accentStyle = computed(() => accentColor.value ? { color: accentColor.value } : {})
const accentBorder = computed(() => accentColor.value ? { borderColor: accentColor.value } : {})

const carouselItems = computed(() =>
    props.products.filter(p => p.showInCarousel && p.imageUrl && !p.soldOut))

// One flat slide list: promo tiles lead, then product photos. Promos without an image render as
// full-bleed callouts on the accent color, like a QSR board's "combo" tiles.
interface Slide { key: string; kind: 'promo' | 'product'; imageUrl: string | null; title: string; subtitle: string | null }
const slides = computed<Slide[]>(() => [
    ...(props.promos ?? []).map(pr => ({
        key: `promo-${pr.id}`, kind: 'promo' as const, imageUrl: pr.imageUrl, title: pr.title, subtitle: pr.subtitle,
    })),
    ...carouselItems.value.map(p => ({
        key: `product-${p.id}`, kind: 'product' as const, imageUrl: p.imageUrl, title: p.name, subtitle: priceLabel(p),
    })),
])

const promoTileStyle = computed(() => ({
    backgroundColor: accentColor.value || '#1867c0',
}))

// Group by category, ordered by the category's sort order; uncategorized items fall last under "Other".
const groups = computed(() => {
    const map = new Map<string, { key: string; name: string; sort: number; items: ConcessionProduct[] }>()
    for (const p of props.products) {
        const key = p.categoryId ?? 'uncategorized'
        const name = p.categoryName ?? 'Other'
        const sort = p.categoryId ? p.categorySortOrder : Number.MAX_SAFE_INTEGER
        if (!map.has(key)) map.set(key, { key, name, sort, items: [] })
        map.get(key)!.items.push(p)
    }
    return [...map.values()].sort((a, b) => a.sort - b.sort || a.name.localeCompare(b.name))
})

function priceLabel(p: ConcessionProduct): string {
    if (p.variants.length === 0) return money(p.priceCents)
    const prices = p.variants.map(v => v.priceCents ?? p.priceCents)
    const min = Math.min(...prices)
    return min === Math.max(...prices) ? money(min) : `from ${money(min)}`
}
function money(c: number) { return `$${(c / 100).toFixed(2)}` }
</script>

<style scoped>
.menu-board { padding: 32px 24px; min-height: 100%; }
.board-inner { max-width: 1280px; margin: 0 auto; }
.board-title { font-size: clamp(2rem, 4vw, 3.25rem); font-weight: 800; letter-spacing: -0.01em; text-align: center; }

/* Promo callout slide: big centered text, QSR-board style. */
.promo-tile {
    height: 100%;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 12px;
    padding: 24px 48px;
    text-align: center;
    color: #fff;
}
.promo-tile--overlay { background: rgba(0, 0, 0, 0.45); }
.promo-tile__title {
    font-size: clamp(2rem, 4.5vw, 3.5rem);
    font-weight: 900;
    letter-spacing: 0.02em;
    text-transform: uppercase;
    line-height: 1.1;
    text-shadow: 0 2px 8px rgba(0, 0, 0, 0.35);
}
.promo-tile__subtitle {
    font-size: clamp(1.1rem, 2vw, 1.8rem);
    font-weight: 700;
    opacity: 0.95;
    text-shadow: 0 1px 6px rgba(0, 0, 0, 0.35);
}

.carousel-caption {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 16px;
    padding: 24px 32px;
    background: linear-gradient(to top, rgba(0, 0, 0, 0.8), rgba(0, 0, 0, 0));
    color: #fff;
}

/* Flow categories into balanced columns so the whole menu fits the screen width. */
.board-sections { columns: 3 340px; column-gap: 48px; }
.board-section { break-inside: avoid; margin-bottom: 22px; }
.board-section-title {
    font-size: clamp(1.2rem, 2vw, 1.8rem);
    font-weight: 800;
    letter-spacing: -0.01em;
    padding-left: 12px;
    margin-bottom: 8px;
    border-left: 5px solid currentColor;
    line-height: 1.1;
}
.board-item { padding: 5px 0; }
.board-item--out { opacity: 0.45; }
/* Name ........ price leader row, like a printed menu. */
.board-item__row { display: flex; align-items: baseline; gap: 8px; }
.board-item__name { font-size: clamp(1rem, 1.3vw, 1.35rem); font-weight: 700; line-height: 1.25; }
.board-item__dots { flex: 1 1 auto; border-bottom: 2px dotted currentColor; opacity: 0.25; transform: translateY(-3px); min-width: 16px; }
.board-item__price { font-size: clamp(1rem, 1.3vw, 1.35rem); font-weight: 800; white-space: nowrap; }
.board-item__desc { font-size: 0.9rem; opacity: 0.65; line-height: 1.25; max-width: 90%; }
</style>
