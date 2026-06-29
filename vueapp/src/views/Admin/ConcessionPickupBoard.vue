<template>
    <v-theme-provider theme="tenantDark" with-background>
        <div class="board d-flex flex-column">
            <div class="board-header d-flex align-center px-8 py-4 ga-3">
                <img v-if="branding.logoUrl" :src="branding.logoUrl" class="board-logo" :alt="branding.displayName || 'RidePass'" />
                <h1 class="text-h4 font-weight-bold">Order Pickup</h1>
                <v-spacer />
                <span class="text-h6 text-medium-emphasis">{{ clock }}</span>
            </div>

            <div class="board-body flex-grow-1 px-8 pb-8">
                <!-- Ready for pickup -->
                <div class="text-overline text-success mb-2" style="font-size: 1.1rem !important;">Ready for pickup</div>
                <div v-if="ready.length" class="ready-grid">
                    <div v-for="(e, i) in ready" :key="'r' + i" class="ready-card">
                        <div class="ready-num">{{ e.orderNumber ?? '—' }}</div>
                        <div v-if="e.customerName" class="ready-name text-truncate">{{ e.customerName }}</div>
                    </div>
                </div>
                <div v-else class="text-h6 text-medium-emphasis mb-6">Nothing ready just yet.</div>

                <v-divider class="my-6" />

                <!-- Preparing -->
                <div class="text-overline text-medium-emphasis mb-2" style="font-size: 1.1rem !important;">Preparing</div>
                <div v-if="preparing.length" class="prep-row">
                    <div v-for="(e, i) in preparing" :key="'p' + i" class="prep-chip">{{ e.orderNumber ?? '—' }}</div>
                </div>
                <div v-else class="text-body-1 text-medium-emphasis">No orders in the kitchen.</div>
            </div>
        </div>
    </v-theme-provider>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { ConcessionService, type BoardEntry } from '@/services/ConcessionService'
import { branding } from '@/stores/branding'
import { setHomeScreenIcon } from '@/helpers/HomeScreenIcon'

const svc = new ConcessionService()
const ready = ref<BoardEntry[]>([])
const preparing = ref<BoardEntry[]>([])
const now = ref(Date.now())
let timer: number | undefined
let clockTimer: number | undefined

const clock = computed(() => new Date(now.value).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }))

onMounted(() => {
    // A dedicated lobby tablet can pin a chromeless "Pickup" icon that reopens this screen.
    setHomeScreenIcon({ title: `${branding.displayName || 'RidePass'} Pickup`, iconUrl: '/icon-board.png', startPath: '/Admin/ConcessionPickupBoard' })
    refresh()
    timer = window.setInterval(refresh, 5000)
    clockTimer = window.setInterval(() => { now.value = Date.now() }, 1000)
})
onUnmounted(() => {
    if (timer) window.clearInterval(timer)
    if (clockTimer) window.clearInterval(clockTimer)
})

async function refresh() {
    // Best-effort display: keep the last good board on a transient failure rather than blanking the screen.
    try {
        const b = (await svc.board() as any).data.data
        ready.value = b.ready
        preparing.value = b.preparing
    } catch { /* keep last good */ }
}
</script>

<style scoped>
.board { height: 100vh; }
.board-logo { height: 48px; width: auto; }
.ready-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
    gap: 16px;
}
.ready-card {
    background: rgba(var(--v-theme-success), 0.18);
    border: 2px solid rgb(var(--v-theme-success));
    border-radius: 16px;
    padding: 16px;
    text-align: center;
}
.ready-num { font-size: 4rem; font-weight: 800; line-height: 1; }
.ready-name { font-size: 1.1rem; margin-top: 6px; opacity: 0.85; }
.prep-row { display: flex; flex-wrap: wrap; gap: 12px; }
.prep-chip {
    font-size: 2rem;
    font-weight: 700;
    opacity: 0.6;
    padding: 6px 18px;
    border: 1px solid rgba(128, 128, 128, 0.4);
    border-radius: 12px;
}
</style>
