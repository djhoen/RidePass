<template>
    <v-container fluid class="pa-0">
        <div v-if="loading" class="d-flex justify-center pa-12"><v-progress-circular indeterminate size="64" /></div>
        <MenuBoardDisplay v-else :products="products" :settings="settings"
            :title="branding.displayName || 'RidePass'" :fallback-logo="branding.logoUrl"
            :fallback-accent="branding.primaryColor" />
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { ConcessionService, type ConcessionProduct, type ConcessionMenuSettings } from '@/services/ConcessionService'
import { branding } from '@/stores/branding'
import { setHomeScreenIcon } from '@/helpers/HomeScreenIcon'
import MenuBoardDisplay from '@/components/MenuBoardDisplay.vue'

const svc = new ConcessionService()
const products = ref<ConcessionProduct[]>([])
const settings = ref<ConcessionMenuSettings>({
    logoUrl: null, backgroundColor: null, textColor: null, accentColor: null, showCarousel: true, carouselSeconds: 5, tipsEnabled: false,
    prepWarnMinutes: 5, prepLateMinutes: 10, orderingHours: null, orderingSeasons: null, requireEventDay: true, pricesIncludeTax: false,
    seasonPassDiscountEnabled: false, seasonPassDiscountKind: 'percent', seasonPassDiscountValue: 0,
    loampassDiscountEnabled: false, loampassDiscountKind: 'percent', loampassDiscountValue: 0, requireManagerForManualDiscount: true,
    starterSeeded: false, orderingOpenNow: true,
})
const loading = ref(true)
let timer: number | undefined

onMounted(() => {
    // A dedicated menu-board tablet can pin a chromeless "Menu" icon that reopens this screen.
    setHomeScreenIcon({ title: `${branding.displayName || 'RidePass'} Menu`, iconUrl: '/icon-menu.png', startPath: '/Admin/ConcessionMenu' })
    refresh()
    timer = window.setInterval(refresh, 30000)   // reflect catalog/sold-out/style changes without touching the board
})
onUnmounted(() => { if (timer) window.clearInterval(timer) })

async function refresh() {
    try {
        const [items, ms] = await Promise.all([svc.items(), svc.menuSettings()])
        products.value = (items as any).data.data
        settings.value = (ms as any).data.data
    } catch { /* board is best-effort; keep showing the last good menu on a transient failure */ }
    finally { loading.value = false }
}
</script>
