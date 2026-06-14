<template>
    <v-container style="max-width: 720px">
        <h1 class="text-h4 mb-4">Buy Spectator Pass</h1>
        <BuySpectatorFlow :event-id="eventId" />
    </v-container>
</template>

<script setup lang="ts">
import { useRoute } from 'vue-router'
import BuySpectatorFlow from '@/components/BuySpectatorFlow.vue'

// Route page just resolves the event id and delegates to the reusable flow,
// which is the same component the public Event landing page mounts inline.
const route = useRoute()
const eventId = route.params.eventId as string
</script>
