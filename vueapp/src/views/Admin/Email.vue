<template>
    <div>
        <v-container fluid class="pb-0">
            <div class="d-flex align-center flex-wrap ga-3 mb-2">
                <h1 class="text-h5">Email</h1>
            </div>
            <p class="text-body-2 text-medium-emphasis mb-3">
                Send once goes to a list on a date you pick. Send automatically keeps running: each
                rider gets the email at their own moment, timed off their purchase or the event.
            </p>
            <!-- Page-level tabs, routed so each half keeps its own URL and deep links still work. -->
            <v-tabs color="primary" density="comfortable">
                <v-tab to="/Admin/Email/Campaigns" prepend-icon="mdi-email-newsletter">Send once</v-tab>
                <v-tab to="/Admin/Email/Automations" prepend-icon="mdi-robot-outline">Send automatically</v-tab>
            </v-tabs>
            <v-divider />
        </v-container>
        <router-view />
    </div>
</template>

<script setup lang="ts">
// Campaigns (one send, one list, one date) and Automations (a rule that keeps sending) share a
// page so the choice between them is explained where it is made. Each tab is its own view.
</script>
