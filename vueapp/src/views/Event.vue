<template>
    <v-container class="py-6" max-width="900">
        <v-alert v-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>

        <v-card v-if="event" elevation="2">
            <v-img v-if="event.imageUrl" :src="event.imageUrl" :max-height="320" cover></v-img>
            <v-card-text>
                <div class="d-flex align-center flex-wrap ga-2 mb-2">
                    <v-chip size="small" :color="event.eventTypeColor || 'primary'" variant="tonal">
                        {{ event.eventTypeName }}
                    </v-chip>
                    <v-chip v-if="capacityLabel" size="small" variant="text">
                        <v-icon start size="small">mdi-account-group</v-icon>
                        {{ capacityLabel }}
                    </v-chip>
                </div>
                <h1 class="text-h4 mb-2">{{ event.title }}</h1>
                <div class="text-subtitle-1 text-medium-emphasis mb-1">
                    <v-icon size="small" class="mr-1">mdi-calendar</v-icon>
                    {{ dateLine }}
                </div>
                <div v-if="event.locationLabel" class="text-subtitle-1 text-medium-emphasis mb-4">
                    <v-icon size="small" class="mr-1">mdi-map-marker</v-icon>
                    {{ event.locationLabel }}
                </div>

                <p v-if="event.description" class="text-body-1 mb-4" style="white-space: pre-wrap">
                    {{ event.description }}
                </p>

                <div class="d-flex flex-wrap ga-2 mb-2">
                    <v-btn v-if="event.hasSpectatorTiers" color="primary" size="large"
                        :to="`/BuySpectator/${event.id}`">
                        <v-icon start>mdi-ticket-confirmation</v-icon>
                        Buy spectator pass
                    </v-btn>
                    <v-btn v-if="event.hasRaceEntryTiers" color="primary" size="large"
                        :to="`/BuyTicket/${event.id}`">
                        <v-icon start>mdi-flag-checkered</v-icon>
                        Register to race
                    </v-btn>
                    <v-btn v-if="hasEligiblePasses" variant="tonal" size="large" :to="`/BuyTicket/${event.id}`">
                        <v-icon start>mdi-card-account-details</v-icon>
                        Buy a day pass
                    </v-btn>
                </div>
                <p v-if="priceFromLabel" class="text-caption text-medium-emphasis">{{ priceFromLabel }}</p>

                <v-divider class="my-5"></v-divider>

                <div class="text-subtitle-2 mb-2">Share this event</div>
                <SocialShare :url="shareUrl" :title="shareTitle" :text="shareText" />
            </v-card-text>
        </v-card>

        <v-skeleton-loader v-if="!event && !loadError" type="card-avatar, article, actions"></v-skeleton-loader>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import { EventService, type EventDto } from '@/services/EventService'
import { branding } from '@/stores/branding'
import SocialShare from '@/components/SocialShare.vue'

const route = useRoute()
const service = new EventService()
const event = ref<EventDto | null>(null)
const loadError = ref('')

const shareUrl = computed(() => window.location.href)
const shareTitle = computed(() =>
    event.value ? `${event.value.title} — ${branding.displayName}` : 'Event')
const shareText = computed(() => {
    if (!event.value) return ''
    const date = dayjs.utc(event.value.startsAtUtc).tz(branding.timezone || 'UTC').format('MMM D, YYYY')
    return `${event.value.title} on ${date} at ${branding.displayName}.`
})

const dateLine = computed(() => {
    if (!event.value) return ''
    const tz = branding.timezone || 'UTC'
    const start = dayjs.utc(event.value.startsAtUtc).tz(tz)
    const end = dayjs.utc(event.value.endsAtUtc).tz(tz)
    if (event.value.allDay) {
        return start.isSame(end, 'day')
            ? start.format('dddd, MMM D, YYYY')
            : `${start.format('MMM D')} – ${end.format('MMM D, YYYY')}`
    }
    if (start.isSame(end, 'day')) {
        return `${start.format('dddd, MMM D, YYYY')} · ${start.format('h:mm A')} – ${end.format('h:mm A')}`
    }
    return `${start.format('MMM D, h:mm A')} – ${end.format('MMM D, YYYY h:mm A')}`
})

const capacityLabel = computed(() => {
    if (!event.value?.capacity) return ''
    const reserved = event.value.spotsReserved ?? 0
    const remaining = Math.max(0, event.value.capacity - reserved)
    return remaining > 0 ? `${remaining} spots left` : 'Sold out'
})

const hasEligiblePasses = computed(() =>
    !!event.value?.eligiblePasses && event.value.eligiblePasses.length > 0)

const priceFromLabel = computed(() => {
    const cents = event.value?.minTicketPriceCents
    if (!cents) return ''
    return `Tickets from $${(cents / 100).toFixed(2)}`
})

onMounted(async () => {
    try {
        const r = await service.getPublic(route.params.id as string)
        event.value = (r.data as any).data
    } catch (err: any) {
        loadError.value = err.response?.data?.error || 'This event is not available.'
    }
})
</script>
