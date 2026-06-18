<template>
    <v-card class="h-100 up-card d-flex flex-column">
        <div class="up-image" :style="bandStyle">
            <div v-if="item.occursAtUtc" class="up-datebadge">
                <div class="up-day">{{ formatDay(item.occursAtUtc) }}</div>
                <div class="up-month">{{ formatMonth(item.occursAtUtc) }}</div>
            </div>
            <v-chip size="x-small" color="primary" class="up-chip">Event ticket</v-chip>
            <img v-if="logoUrl" class="up-logo" :src="logoUrl" :alt="item.tenantDisplayName" />
        </div>

        <v-card-text class="pa-3 d-flex align-start ga-3 flex-grow-1">
            <!-- Left: event info -->
            <div class="flex-grow-1" style="min-width: 0">
                <div class="d-flex align-center ga-2 mb-1">
                    <span class="text-subtitle-1 font-weight-bold text-truncate">{{ item.itemName }}</span>
                    <v-btn size="x-small" variant="tonal" class="flex-shrink-0" :href="eventUrl" rel="noopener"
                        prepend-icon="mdi-open-in-new">View</v-btn>
                </div>
                <div class="text-caption text-medium-emphasis d-flex align-center ga-1">
                    <v-icon icon="mdi-map-marker" size="14"></v-icon>
                    <span class="text-truncate">{{ item.tenantDisplayName }}</span>
                </div>
                <div v-if="item.occursAtUtc" class="text-caption text-medium-emphasis d-flex align-center ga-1 mt-1">
                    <v-icon icon="mdi-calendar" size="14"></v-icon>
                    <span>{{ formatWhen(item.occursAtUtc) }}</span>
                </div>
                <v-chip v-if="item.registrationComplete" size="x-small" color="success" variant="tonal"
                    prepend-icon="mdi-check-circle" class="mt-2">Waivers signed</v-chip>
                <v-chip v-else size="x-small" color="warning" variant="tonal"
                    prepend-icon="mdi-alert-circle" class="mt-2">Waiver / details needed</v-chip>
            </div>

            <!-- Right: actions -->
            <div class="d-flex flex-column ga-2 flex-shrink-0 up-actions">
                <v-btn size="small" variant="tonal" block prepend-icon="mdi-receipt-text-outline"
                    @click="$emit('order', item)">Order detail</v-btn>
                <v-btn v-if="!item.registrationComplete && item.redemptionToken"
                    size="small" color="warning" variant="flat" block :href="finishUrl" rel="noopener"
                    prepend-icon="mdi-draw-pen">Finish</v-btn>
            </div>
        </v-card-text>
    </v-card>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import dayjs from 'dayjs'
import tenantHelper from '@/helpers/TenantHelper'
import type { UpcomingItem } from '@/services/UpcomingService'

const props = defineProps<{ item: UpcomingItem }>()
defineEmits<{ (e: 'order', item: UpcomingItem): void }>()

const apiOrigin = (() => {
    try { return new URL((import.meta as any).env?.VITE_API_ENDPOINT ?? '', window.location.origin).origin }
    catch { return '' }
})()
function resolveImg(url: string | null): string | null {
    if (!url) return null
    return /^https?:\/\//i.test(url) ? url : `${apiOrigin}${url}`
}

const bandStyle = computed(() => {
    const u = resolveImg(props.item.imageUrl)
    const style: Record<string, string> = {}
    if (u) {
        style.backgroundImage = `url(${u})`
        style.backgroundSize = 'cover'
        style.backgroundPosition = 'center'
    }
    return style
})
const logoUrl = computed(() => resolveImg(props.item.tenantLogoUrl))

// Cross-subdomain links to the track site (this card lives on the apex feed).
function tenantUrl(path: string): string {
    const proto = window.location.protocol
    const port = window.location.port ? `:${window.location.port}` : ''
    return `${proto}//${props.item.tenantSubdomain}.${tenantHelper.rootDomain()}${port}${path}`
}
const eventUrl = computed(() => tenantUrl(`/Event/${props.item.id}`))
const finishUrl = computed(() => tenantUrl(`/FinishRegistration/${props.item.redemptionToken}`))

function formatWhen(utc: string): string { return dayjs.utc(utc).local().format('ddd, MMM D · h:mm A') }
function formatDay(utc: string): string { return dayjs.utc(utc).local().format('D') }
function formatMonth(utc: string): string { return dayjs.utc(utc).local().format('MMM').toUpperCase() }
</script>

<style scoped>
.up-card { overflow: visible; transition: transform 0.15s ease; }
.up-card:hover { transform: translateY(-2px); }

.up-actions {
    width: 124px;
}
.up-image {
    position: relative;
    height: 92px;
    background: linear-gradient(135deg, #334155, #64748b);
    background-size: cover;
    background-position: center;
    border-top-left-radius: inherit;
    border-top-right-radius: inherit;
}
.up-datebadge {
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
    box-shadow: 1px 2px 5px 0 rgba(0, 0, 0, 0.34);
}
.up-datebadge .up-day { font-size: 1.4rem; font-weight: 700; }
.up-datebadge .up-month { font-size: 0.65rem; letter-spacing: 0.08em; opacity: 0.85; margin-top: 2px; }
.up-chip { position: absolute; top: 8px; right: 8px; }
.up-logo {
    position: absolute;
    bottom: 6px;
    right: 6px;
    max-height: 24px;
    max-width: 84px;
    object-fit: contain;
    filter: drop-shadow(0 1px 2px rgba(0, 0, 0, 0.5));
}
</style>
