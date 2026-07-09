<template>
    <v-dialog :model-value="open" max-width="820" scrollable @update:model-value="v => emit('update:open', v)">
        <v-card>
            <v-card-title class="d-flex align-center">
                <span>Embedded widgets: sales &amp; install guide</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" @click="emit('update:open', false)"></v-btn>
            </v-card-title>
            <v-card-text>
                <!-- The pitch -->
                <div class="text-subtitle-2 mb-1">The pitch (30 seconds)</div>
                <v-alert variant="tonal" color="primary" density="compact" class="mb-4 text-body-2">
                    "You keep your website. Your riders never leave it. We drop your event calendar and registration
                    directly into the pages you already have, and everything from sign-up to waiver to payment happens
                    right there on your site. Setup on your end is pasting two lines of code, and if you give us access
                    to your site, we'll do even that part for you."
                </v-alert>
                <p class="text-body-2 mb-4">
                    Positioning: most tracks already have a website they like. The competition says "replace your site"
                    or "link out to our ticketing page." We say <strong>keep your site, we plug into it</strong>.
                </p>

                <!-- What it is -->
                <div class="text-subtitle-2 mb-1">The widgets</div>
                <v-table density="compact" class="mb-3">
                    <thead>
                        <tr><th>Widget</th><th>What the visitor sees</th><th>Best spot</th></tr>
                    </thead>
                    <tbody>
                        <tr>
                            <td class="text-no-wrap">Events list</td>
                            <td>Carousel of upcoming events; each card opens registration + checkout inline.</td>
                            <td>Home or "Events" page</td>
                        </tr>
                        <tr>
                            <td class="text-no-wrap">Calendar + events</td>
                            <td>Upcoming-events carousel above a month calendar; clicks check out inline.</td>
                            <td>"Schedule" page</td>
                        </tr>
                        <tr>
                            <td class="text-no-wrap">Single event</td>
                            <td>Registration + checkout for one specific event.</td>
                            <td>Race promo / landing page</td>
                        </tr>
                    </tbody>
                </v-table>
                <ul class="text-body-2 ml-4 mb-4">
                    <li>The full purchase happens on their site: entries, gate fees, add-ons, waivers (incl. minors), payment.</li>
                    <li>Widgets auto-size to their content and multiple widgets can share a page.</li>
                    <li>Always current: whatever is in the track's RidePass admin is what the widgets show.</li>
                    <li>Secure by design: only website addresses the track approves can display their widgets.</li>
                </ul>

                <!-- Give / get -->
                <div class="text-subtitle-2 mb-1">What we need from the track</div>
                <p class="text-body-2 mb-3">
                    Their website address(es), e.g. <code>xyztrack.com</code> (the <code>www.</code> version is covered
                    automatically). That's the allow-list on this tab. If we're installing for them, also site access (below).
                </p>
                <div class="text-subtitle-2 mb-1">What we give the track</div>
                <p class="text-body-2 mb-2">
                    A copy-paste snippet per widget from the builder on this tab. Two lines, for example:
                </p>
                <pre class="snippet mb-4"><code>&lt;div data-ridepass="events" data-tenant="yourtrack"&gt;&lt;/div&gt;
&lt;script src="https://ridepass.io/embed.js" async&gt;&lt;/script&gt;</code></pre>
                <p class="text-body-2 mb-4">
                    The script line is needed once per page even with several widgets. Use the live preview on this tab
                    to show their real events rendering during the call.
                </p>

                <!-- Done-for-you -->
                <div class="text-subtitle-2 mb-1">The done-for-you offer</div>
                <p class="text-body-2 mb-2">
                    "If you'd rather not touch the code at all, give us access to your website and we'll install it for
                    you. It's usually a 15-minute job on our side." Ask <strong>"what is your website built on?"</strong> and map it:
                </p>
                <v-table density="compact" class="mb-2">
                    <thead><tr><th>Their answer</th><th>What to ask for</th></tr></thead>
                    <tbody>
                        <tr><td>WordPress</td><td>A temporary admin/editor login to their dashboard</td></tr>
                        <tr><td>Wix / Squarespace / builder</td><td>A contributor or admin invite to the site</td></tr>
                        <tr><td>"Our web guy handles it"</td><td>An email intro; we send snippets + placement notes and stay on the thread</td></tr>
                        <tr><td>Custom site</td><td>Hosting/repo access, or an intro to whoever maintains it</td></tr>
                    </tbody>
                </v-table>
                <p class="text-body-2 mb-4">
                    Ground rules: ask for the least access that works, install, verify a test checkout end to end, then
                    tell the track to revoke the access. Saying that last part out loud builds trust.
                </p>

                <!-- FAQ -->
                <div class="text-subtitle-2 mb-1">Common questions</div>
                <v-expansion-panels variant="accordion" class="mb-4">
                    <v-expansion-panel title="Will it match our website's look?">
                        <v-expansion-panel-text class="text-body-2">
                            The widgets carry the track's RidePass branding (colors, logo) and size themselves to the
                            page automatically.
                        </v-expansion-panel-text>
                    </v-expansion-panel>
                    <v-expansion-panel title="Does money touch our website?">
                        <v-expansion-panel-text class="text-body-2">
                            No. Payment runs through RidePass + Stripe inside the widget; their site never sees card
                            data, which is a compliance win versus rolling their own.
                        </v-expansion-panel-text>
                    </v-expansion-panel>
                    <v-expansion-panel title="What if we redesign or move our website?">
                        <v-expansion-panel-text class="text-body-2">
                            Paste the same snippets into the new site and tell us the new address so we can approve it
                            in the allow-list. Everything else carries over.
                        </v-expansion-panel-text>
                    </v-expansion-panel>
                    <v-expansion-panel title="Can we still send people to a RidePass page directly?">
                        <v-expansion-panel-text class="text-body-2">
                            Yes. The hosted site (their-name.ridepass.io) shows the same events; email and social links
                            can go either place.
                        </v-expansion-panel-text>
                    </v-expansion-panel>
                    <v-expansion-panel title="We only want the calendar, not checkout on our site.">
                        <v-expansion-panel-text class="text-body-2">
                            Fine. Widgets are independent; start with the calendar and add the rest whenever they're ready.
                        </v-expansion-panel-text>
                    </v-expansion-panel>
                </v-expansion-panels>

                <!-- Checklist -->
                <div class="text-subtitle-2 mb-1">After the track says yes</div>
                <ol class="text-body-2 ml-4 mb-1">
                    <li>Enable embedding on this tab and enter the track's approved website addresses.</li>
                    <li>Build a snippet per widget with the agreed options; live-preview it on the call.</li>
                    <li>Send the snippets + placement notes, or collect site access and install them ourselves.</li>
                    <li>Verify on their live site: widgets render, resize, and a test registration completes.</li>
                    <li>Have the track revoke any temporary access they gave us.</li>
                </ol>
                <p class="text-caption text-medium-emphasis mt-3 mb-0">
                    If the track has no website (or hates theirs), don't pitch embedding; pitch the hosted RidePass site
                    instead. Full script: <code>docs/sales/embed-widgets-sales-script.md</code>.
                </p>
            </v-card-text>
            <v-card-actions>
                <v-spacer></v-spacer>
                <v-btn @click="emit('update:open', false)">Close</v-btn>
            </v-card-actions>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
defineProps<{ open: boolean }>()
const emit = defineEmits<{ (e: 'update:open', value: boolean): void }>()
</script>

<style scoped>
.snippet {
    background: rgba(0, 0, 0, 0.05);
    border: 1px solid rgba(0, 0, 0, 0.12);
    border-radius: 6px;
    padding: 10px 12px;
    font-size: 12px;
    overflow-x: auto;
}
</style>
