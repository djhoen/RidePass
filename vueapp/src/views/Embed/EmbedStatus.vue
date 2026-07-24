<template>
    <!-- Chromeless daily-status strip: framed on a track's own website via embed.js.
         One compact banner: open/closed chip, the day's status message, today's hours.
         Small on purpose; the host iframe auto-sizes to it. -->
    <div class="embed-status pa-3">
        <div v-if="!branding.loaded" class="text-center py-2">
            <v-progress-circular indeterminate size="20" color="primary" />
        </div>
        <div v-else-if="status" class="d-flex align-center ga-3 flex-wrap">
            <v-chip :color="status.color" size="small" variant="flat" :prepend-icon="status.icon">
                {{ status.label }}
            </v-chip>
            <span v-if="status.message" class="text-body-2">{{ status.message }}</span>
            <v-spacer />
            <span v-if="todayHours" class="text-caption text-medium-emphasis text-no-wrap">
                Today: {{ todayHours }}
            </span>
        </div>
        <div v-else class="d-flex align-center ga-3">
            <span v-if="todayHours" class="text-caption text-medium-emphasis">Today: {{ todayHours }}</span>
            <span v-else class="text-caption text-medium-emphasis">Check back for today's status.</span>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { branding, loadBranding } from '@/stores/branding'

// Mirrors the home page's freshness rule: a status older than a day is stale and
// better left unshown than shown wrong.
const status = computed(() => {
    if (branding.dailyStatusOpen === null || !branding.dailyStatusUpdatedAt) return null
    if (dayjs().diff(dayjs(branding.dailyStatusUpdatedAt), 'hour') > 24) return null
    return branding.dailyStatusOpen
        ? { label: 'Open today', message: branding.dailyStatusMessage ?? '', color: 'success', icon: 'mdi-check-circle' }
        : { label: 'Closed today', message: branding.dailyStatusMessage ?? '', color: 'error', icon: 'mdi-close-circle' }
})

const todayHours = computed(() => {
    if (!branding.hoursJson) return null
    try {
        const parsed = JSON.parse(branding.hoursJson) as Record<string, { closed?: boolean; open?: string; close?: string }>
        const key = dayjs().tz(branding.timezone || 'UTC').format('ddd').toLowerCase()
        const v = parsed[key]
        if (!v) return null
        if (v.closed) return 'Closed'
        return `${to12h(v.open ?? '09:00')} to ${to12h(v.close ?? '17:00')}`
    } catch {
        return null
    }
})

function to12h(hhmm: string): string {
    const [h, m] = hhmm.split(':').map(Number)
    const ampm = h >= 12 ? 'PM' : 'AM'
    const h12 = h % 12 === 0 ? 12 : h % 12
    return m ? `${h12}:${String(m).padStart(2, '0')} ${ampm}` : `${h12} ${ampm}`
}

onMounted(async () => {
    if (!branding.loaded) await loadBranding()
})
</script>
