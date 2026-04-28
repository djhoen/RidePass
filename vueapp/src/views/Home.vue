<template>
    <div>
        <section v-if="branding.heroImageUrl" class="hero" :style="{ backgroundImage: `url(${branding.heroImageUrl})` }">
            <div class="hero-overlay">
                <h1 class="text-h2 font-weight-bold text-white">{{ branding.displayName }}</h1>
                <p v-if="branding.tagline" class="text-h6 text-white">{{ branding.tagline }}</p>
            </div>
        </section>

        <v-container v-else>
            <v-row class="my-12" align="center" justify="center">
                <v-col cols="12" md="8" class="text-center">
                    <h1 class="text-h2 font-weight-bold mb-4">{{ branding.displayName }}</h1>
                    <p v-if="branding.tagline" class="text-h6 text-medium-emphasis mb-8">{{ branding.tagline }}</p>
                    <template v-if="isApex">
                        <v-btn color="primary" size="x-large" to="/Discover" class="mr-3"
                            prepend-icon="mdi-map-search">Find Tracks Near You</v-btn>
                        <v-btn variant="outlined" size="x-large" to="/Login">Super Admin Login</v-btn>
                    </template>
                    <template v-else>
                        <v-btn color="primary" size="x-large" to="/BuyPass" class="mr-3">Buy Day Pass</v-btn>
                        <v-btn variant="outlined" size="x-large" to="/Login">Sign In</v-btn>
                    </template>
                </v-col>
            </v-row>
        </v-container>

        <v-container>
            <h2 class="text-h4 mb-4">Upcoming</h2>
            <p class="text-caption text-medium-emphasis mb-4">Next 30 days in {{ branding.timezone }}</p>

            <v-progress-circular v-if="loading" indeterminate color="primary"></v-progress-circular>

            <div v-else-if="days.length === 0" class="text-medium-emphasis">
                Nothing scheduled in the next 30 days.
            </div>

            <v-row v-else>
                <v-col v-for="day in days" :key="day.dateKey" cols="12" md="6" lg="4">
                    <v-card class="pa-3" variant="outlined">
                        <div class="text-subtitle-1 font-weight-bold mb-2">{{ day.label }}</div>

                        <div v-for="b in day.blackouts" :key="'b-' + b.id" class="blackout mb-2">
                            <v-icon start color="error">mdi-close-circle</v-icon>
                            <strong>Closed</strong>
                            <span v-if="!b.allDay"> — {{ formatTime(b.startsAtUtc) }}–{{ formatTime(b.endsAtUtc) }}</span>
                            <span v-if="b.reason"> • {{ b.reason }}</span>
                        </div>

                        <div v-for="e in day.events" :key="'e-' + e.id" class="event-row mb-2">
                            <v-chip size="small" :style="{ backgroundColor: e.eventTypeColor, color: '#fff' }" class="mr-2">
                                {{ e.eventTypeName }}
                            </v-chip>
                            <strong>{{ e.title }}</strong>
                            <div class="text-caption text-medium-emphasis">
                                <span v-if="e.allDay">All day</span>
                                <span v-else>{{ formatTime(e.startsAtUtc) }}–{{ formatTime(e.endsAtUtc) }}</span>
                                <span v-if="e.locationLabel"> • {{ e.locationLabel }}</span>
                                <span v-if="e.status === 'cancelled'" class="text-error"> • CANCELLED</span>
                                <span v-if="e.capacity">
                                    • {{ Math.max(0, e.capacity - (e.spotsReserved ?? 0)) }} of {{ e.capacity }} spots left
                                </span>
                            </div>
                            <div class="d-flex ga-2 mt-1">
                                <v-btn v-if="e.hasActiveTiers && e.status !== 'cancelled'" size="small" color="primary"
                                    :to="`/BuyTicket/${e.id}`">
                                    Buy Ticket{{ e.minTicketPriceCents ? ` from $${(e.minTicketPriceCents / 100).toFixed(2)}` : '' }}
                                </v-btn>
                                <v-btn v-if="branding.requireReservationForPasses && e.capacity && e.status !== 'cancelled' && (e.capacity - (e.spotsReserved ?? 0)) > 0"
                                    size="small" color="secondary" :to="`/BuyPass?eventId=${e.id}`">
                                    Reserve Day Pass
                                </v-btn>
                            </div>
                        </div>
                    </v-card>
                </v-col>
            </v-row>

            <v-container v-if="branding.secondaryHeroUrl" class="mt-8 pa-0">
                <v-img :src="branding.secondaryHeroUrl" max-height="400" cover></v-img>
            </v-container>

            <div v-if="!isApex" class="mt-8">
                <NewsletterSignup title="Stay in the loop"
                    :subtitle="`Event updates and announcements from ${branding.displayName}.`" />
            </div>
        </v-container>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch, onMounted } from 'vue'
import dayjs from 'dayjs'
import { branding } from '../stores/branding'
import { EventService, type EventDto } from '../services/EventService'
import { BlackoutService, type BlackoutDto } from '../services/BlackoutService'
import tenantHelper from '../helpers/TenantHelper'
import NewsletterSignup from '@/components/NewsletterSignup.vue'

const isApex = computed(() => !tenantHelper.getSubdomain())

const eventService = new EventService()
const blackoutService = new BlackoutService()

const events = ref<EventDto[]>([])
const blackouts = ref<BlackoutDto[]>([])
const loading = ref(false)

async function load() {
    if (!branding.loaded) return
    loading.value = true
    try {
        const tz = branding.timezone || 'UTC'
        const fromUtc = dayjs().tz(tz).startOf('day').utc().toISOString()
        const toUtc = dayjs().tz(tz).startOf('day').add(30, 'day').utc().toISOString()
        const [e, b] = await Promise.all([
            eventService.list(fromUtc, toUtc),
            blackoutService.list(fromUtc, toUtc),
        ])
        events.value = (e.data as any).data
        blackouts.value = (b.data as any).data
    } catch (err) {
        console.error('Failed to load schedule', err)
    } finally {
        loading.value = false
    }
}

onMounted(load)
watch(() => branding.loaded, load)
watch(() => branding.timezone, load)

function formatTime(utc: string): string {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('h:mm A')
}

function formatDayLabel(d: dayjs.Dayjs): string {
    return d.format('ddd, MMM D')
}

// Group events and blackouts by date (in tenant tz), for the next 30 days.
const days = computed(() => {
    const tz = branding.timezone || 'UTC'
    const buckets = new Map<string, { dateKey: string; label: string; events: EventDto[]; blackouts: BlackoutDto[] }>()

    const addToBucket = (utc: string, add: (bucket: any) => void) => {
        const d = dayjs.utc(utc).tz(tz).startOf('day')
        const key = d.format('YYYY-MM-DD')
        if (!buckets.has(key)) {
            buckets.set(key, { dateKey: key, label: formatDayLabel(d), events: [], blackouts: [] })
        }
        add(buckets.get(key)!)
    }

    for (const e of events.value) {
        addToBucket(e.startsAtUtc, (b) => b.events.push(e))
    }
    for (const bo of blackouts.value) {
        addToBucket(bo.startsAtUtc, (b) => b.blackouts.push(bo))
    }

    return [...buckets.values()].sort((a, b) => a.dateKey.localeCompare(b.dateKey))
})
</script>

<style scoped>
.hero {
    position: relative;
    height: 60vh;
    min-height: 360px;
    background-size: cover;
    background-position: center;
    display: flex;
    align-items: center;
    justify-content: center;
}
.hero-overlay {
    background: rgba(0, 0, 0, 0.45);
    padding: 2rem 3rem;
    border-radius: 8px;
    text-align: center;
}
.blackout {
    padding: 6px 10px;
    background: rgba(244, 67, 54, 0.08);
    border-left: 3px solid #f44336;
    border-radius: 4px;
}
.event-row {
    display: block;
}
</style>
