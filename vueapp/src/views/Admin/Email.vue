<template>
    <div>
        <v-container fluid class="pb-0">
            <div class="rp-pagehead">
                <div>
                    <div class="rp-pagehead-title">Campaigns</div>
                    <div class="rp-pagehead-sub">
                        Send once goes to an audience on a date you pick, by email, text, or both. Send automatically
                        keeps running: each rider gets the message at their own moment, timed off their purchase, the
                        event, or the day they join an audience. Audiences are the saved, live-updating lists both send to.
                    </div>
                </div>
            </div>
            <!-- Page-level tabs, routed so each half keeps its own URL and deep links still work. -->
            <v-tabs color="primary" density="comfortable">
                <v-tab to="/Admin/Email/Campaigns" prepend-icon="mdi-email-newsletter">Send once</v-tab>
                <v-tab to="/Admin/Email/Automations" prepend-icon="mdi-robot-outline">Send automatically</v-tab>
                <v-tab to="/Admin/Email/Audiences" prepend-icon="mdi-account-group-outline">Audiences</v-tab>
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
