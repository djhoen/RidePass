<template>
    <v-container style="max-width: 640px" class="py-8">
        <div class="rp-pagehead">
            <div>
                <div class="rp-pagehead-title">Your lesson</div>
                <div class="rp-pagehead-sub">Move your booking to another date{{ weatherHint }}.</div>
            </div>
        </div>
        <v-card class="pa-4">
            <LessonChangePanel :ticket-id="ticketId" :token="token" />
        </v-card>
        <p class="text-caption text-medium-emphasis mt-3">
            Signed in? Your bookings are also under <router-link to="/User/MyPasses">My passes</router-link>.
        </p>
    </v-container>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import LessonChangePanel from '@/components/LessonChangePanel.vue'

// The page a weather-cancellation (or any lesson) email links to (Script0319). The link carries the
// ticket's token, so it works without signing in.
const route = useRoute()
const ticketId = computed(() => String(route.params.id))
const token = computed(() => (typeof route.query.t === 'string' ? route.query.t : null))
const weatherHint = computed(() => (route.query.weather ? ', or take a refund' : ''))
</script>
