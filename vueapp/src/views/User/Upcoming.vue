<template>
    <v-container>
        <h1 class="text-h4 mb-2">My upcoming</h1>
        <p class="text-body-2 text-medium-emphasis mb-6">
            Tickets, passes, and memberships across every RidePass track you've bought from.
        </p>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">
            {{ loadError }}
        </v-alert>

        <div v-if="loading && items.length === 0" class="text-center my-12">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <!-- Upcoming events: event tickets + day passes, sorted by date. -->
        <section v-if="eventLikeItems.length > 0" class="mb-10">
            <h2 class="text-h5 mb-3">Upcoming events</h2>
            <v-row dense>
                <v-col v-for="item in eventLikeItems" :key="item.kind + item.id" cols="12" sm="6" md="4">
                    <v-card class="h-100">
                        <v-card-text>
                            <div class="d-flex align-center ga-2 mb-1">
                                <v-chip size="x-small" :color="kindChipColor(item.kind)">{{ kindLabel(item.kind) }}</v-chip>
                                <span v-if="item.occursAtUtc" class="text-caption">
                                    {{ formatWhen(item.occursAtUtc) }}
                                </span>
                            </div>
                            <div class="text-subtitle-1 font-weight-medium mb-1">{{ item.itemName }}</div>
                            <div class="text-body-2 text-medium-emphasis mb-3">
                                at {{ item.tenantDisplayName }}
                            </div>
                            <v-btn variant="tonal" size="small" :href="tenantUserUrl(item.tenantSubdomain)"
                                rel="noopener" prepend-icon="mdi-arrow-right">
                                Open on {{ item.tenantDisplayName }}
                            </v-btn>
                        </v-card-text>
                    </v-card>
                </v-col>
            </v-row>
        </section>

        <!-- Active passes: season passes + memberships. Range-based. -->
        <section v-if="passLikeItems.length > 0" class="mb-10">
            <h2 class="text-h5 mb-3">Active passes and memberships</h2>
            <v-row dense>
                <v-col v-for="item in passLikeItems" :key="item.kind + item.id" cols="12" sm="6" md="4">
                    <v-card class="h-100">
                        <v-card-text>
                            <div class="d-flex align-center ga-2 mb-1">
                                <v-chip size="x-small" :color="kindChipColor(item.kind)">{{ kindLabel(item.kind) }}</v-chip>
                                <span v-if="item.validToUtc" class="text-caption">
                                    Valid through {{ formatDate(item.validToUtc) }}
                                </span>
                                <span v-else class="text-caption">Lifetime</span>
                            </div>
                            <div class="text-subtitle-1 font-weight-medium mb-1">{{ item.itemName }}</div>
                            <div class="text-body-2 text-medium-emphasis mb-3">
                                at {{ item.tenantDisplayName }}
                            </div>
                            <v-btn variant="tonal" size="small" :href="tenantUserUrl(item.tenantSubdomain)"
                                rel="noopener" prepend-icon="mdi-arrow-right">
                                Open on {{ item.tenantDisplayName }}
                            </v-btn>
                        </v-card-text>
                    </v-card>
                </v-col>
            </v-row>
        </section>

        <v-card v-if="!loading && items.length === 0" variant="outlined">
            <v-card-text class="text-center text-medium-emphasis py-12">
                <v-icon icon="mdi-calendar-blank-outline" size="48" class="mb-3"></v-icon>
                <div class="mb-2">Nothing on your schedule yet.</div>
                <v-btn variant="tonal" to="/Discover" prepend-icon="mdi-map-search">Find a track</v-btn>
            </v-card-text>
        </v-card>
    </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import tenantHelper from '@/helpers/TenantHelper'
import { UpcomingService, type UpcomingItem, type UpcomingKind } from '@/services/UpcomingService'

const service = new UpcomingService()

const items = ref<UpcomingItem[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)

const eventLikeItems = computed(() =>
    items.value.filter(i => i.kind === 'event_ticket' || i.kind === 'pass'))
const passLikeItems = computed(() =>
    items.value.filter(i => i.kind === 'season_pass' || i.kind === 'membership'))

onMounted(load)

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const r = await service.list()
        items.value = (r.data as any).data
    } catch (err: any) {
        loadError.value = err.response?.data?.error || 'Failed to load upcoming items.'
    } finally {
        loading.value = false
    }
}

function kindLabel(kind: UpcomingKind): string {
    switch (kind) {
        case 'event_ticket': return 'Event ticket'
        case 'pass':         return 'Day pass'
        case 'season_pass':  return 'Season pass'
        case 'membership':   return 'Membership'
    }
}

function kindChipColor(kind: UpcomingKind): string {
    switch (kind) {
        case 'event_ticket': return 'primary'
        case 'pass':         return 'secondary'
        case 'season_pass':  return 'info'
        case 'membership':   return 'success'
    }
}

function formatWhen(utc: string): string {
    return dayjs.utc(utc).local().format('ddd, MMM D · h:mm A')
}

function formatDate(utc: string): string {
    return dayjs.utc(utc).local().format('MMM D, YYYY')
}

// Each card links to the tenant subdomain's User area where the existing
// MyPasses page already renders QR codes, cancellation, etc. Reuses the
// per-tenant detail UI rather than rebuilding it at the apex.
function tenantUserUrl(subdomain: string): string {
    const proto = window.location.protocol
    const port = window.location.port ? `:${window.location.port}` : ''
    return `${proto}//${subdomain}.${tenantHelper.rootDomain()}${port}/User/MyPasses`
}
</script>
