<template>
    <v-container fluid class="pa-0">
        <div v-if="loading" class="d-flex justify-center pa-12"><v-progress-circular indeterminate size="64" /></div>

        <!-- Board chooser: shown when the tenant has several boards and no specific one was requested.
             Each TV/tablet taps its board once and pins that URL. -->
        <div v-else-if="showChooser" class="chooser d-flex flex-column align-center justify-center pa-8">
            <v-img v-if="branding.logoUrl" :src="branding.logoUrl" max-height="80" max-width="240" class="mb-4" />
            <h1 class="text-h4 font-weight-bold mb-2">Pick a menu board</h1>
            <p class="text-medium-emphasis mb-6">Each screen shows its own board. Pin this screen's board on the display device.</p>
            <v-btn v-for="b in boards" :key="b.id" size="x-large" variant="tonal" color="primary"
                class="mb-3 chooser-btn" prepend-icon="mdi-television-guide"
                :to="`/Admin/ConcessionMenu/${b.id}`">{{ b.name }}</v-btn>
        </div>

        <MenuBoardDisplay v-else :products="boardProducts" :settings="settings" :promos="boardPromos"
            :title="boardTitle" :fallback-logo="branding.logoUrl"
            :fallback-accent="branding.primaryColor" />
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ConcessionService, type ConcessionProduct, type ConcessionMenuSettings, type ConcessionCategory, type ConcessionMenuBoard, type ConcessionMenuPromo } from '@/services/ConcessionService'
import { branding } from '@/stores/branding'
import { setHomeScreenIcon } from '@/helpers/HomeScreenIcon'
import MenuBoardDisplay from '@/components/MenuBoardDisplay.vue'

const route = useRoute()
const svc = new ConcessionService()
const products = ref<ConcessionProduct[]>([])
const categories = ref<ConcessionCategory[]>([])
const boards = ref<ConcessionMenuBoard[]>([])
const promos = ref<ConcessionMenuPromo[]>([])
const settings = ref<ConcessionMenuSettings>({
    logoUrl: null, backgroundColor: null, textColor: null, accentColor: null, showCarousel: true, carouselSeconds: 5, tipsEnabled: false,
    prepWarnMinutes: 5, prepLateMinutes: 10, orderingHours: null, orderingSeasons: null, requireEventDay: true, pricesIncludeTax: false,
    seasonPassDiscountEnabled: false, seasonPassDiscountKind: 'percent', seasonPassDiscountValue: 0,
    loampassDiscountEnabled: false, loampassDiscountKind: 'percent', loampassDiscountValue: 0, requireManagerForManualDiscount: true,
    starterSeeded: false, orderingOpenNow: true,
})
const loading = ref(true)
let timer: number | undefined

const requestedBoardId = computed(() => (route.params.boardId as string | undefined) || null)
const board = computed(() => {
    if (requestedBoardId.value) return boards.value.find(b => b.id === requestedBoardId.value) ?? null
    // No board in the URL: a single-board tenant just shows it; zero boards = the classic all-in-one screen.
    return boards.value.length === 1 ? boards.value[0] : null
})
const showChooser = computed(() => !board.value && boards.value.length > 1)
const boardTitle = computed(() =>
    board.value ? board.value.name : `${branding.displayName || 'RidePass'} Menu`)

// Products for this screen: categories assigned to this board, plus "all boards" categories
// (menuBoardId null) and uncategorized items. With no board selected, everything shows.
// The photo carousel inside MenuBoardDisplay derives from this same list, so it automatically
// rotates only this screen's items.
const boardProducts = computed(() => {
    if (!board.value) return products.value
    const boardId = board.value.id
    const onThisBoard = new Set(categories.value
        .filter(c => c.menuBoardId === boardId || c.menuBoardId === null)
        .map(c => c.id))
    return products.value.filter(p => p.categoryId === null || onThisBoard.has(p.categoryId))
})

// Promo tiles for this screen: assigned to this board, or "all boards" (menuBoardId null).
const boardPromos = computed(() => {
    if (!board.value) return promos.value
    const boardId = board.value.id
    return promos.value.filter(pr => pr.menuBoardId === boardId || pr.menuBoardId === null)
})

onMounted(() => {
    refresh()
    timer = window.setInterval(refresh, 30000)   // reflect catalog/sold-out/style changes without touching the board
})
onUnmounted(() => { if (timer) window.clearInterval(timer) })

// A dedicated menu-board tablet can pin a chromeless icon that reopens this exact board.
watch(board, b => {
    const name = branding.displayName || 'RidePass'
    setHomeScreenIcon({
        title: b ? `${name} ${b.name}` : `${name} Menu`,
        iconUrl: '/icon-menu.png',
        startPath: b ? `/Admin/ConcessionMenu/${b.id}` : '/Admin/ConcessionMenu',
    })
}, { immediate: true })

async function refresh() {
    try {
        const [items, ms, cats, brds, prms] = await Promise.all([
            svc.items(), svc.menuSettings(), svc.categories(), svc.menuBoards(), svc.menuPromos(),
        ])
        products.value = (items as any).data.data
        settings.value = (ms as any).data.data
        categories.value = (cats as any).data.data
        boards.value = (brds as any).data.data
        promos.value = (prms as any).data.data
    } catch { /* board is best-effort; keep showing the last good menu on a transient failure */ }
    finally { loading.value = false }
}
</script>

<style scoped>
.chooser { min-height: 100vh; }
.chooser-btn { min-width: 320px; }
</style>
