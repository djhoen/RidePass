<template>
    <v-container>
        <div class="d-flex align-center ga-3 mb-4">
            <h1 class="text-h4">Misc settings</h1>
            <v-spacer></v-spacer>
            <v-btn v-if="loaded" color="primary" size="large" :loading="saving" @click="save">
                Save changes
            </v-btn>
        </div>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <div v-if="loading" class="text-center my-12">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <template v-else-if="loaded">
            <v-card class="mb-4">
                <v-card-title>Global embed origins</v-card-title>
                <v-card-text>
                    <v-alert type="info" variant="tonal" density="comfortable" class="mb-4">
                        Sites listed here may embed <strong>any</strong> tenant's widgets, without each
                        track adding them. Use this for our own properties (e.g. loampassmx.com,
                        ridepass.io). Per-track sites still go on each tenant's own allow-list.
                        Global origins are always permitted, even when a tenant has third-party
                        embedding turned off.
                    </v-alert>

                    <v-textarea v-model="originsText" label="Allowed origins (one per line)"
                        :placeholder="placeholder" rows="6" auto-grow density="compact"
                        hint="One per line. A bare domain like xyz.com is accepted and expanded to cover https://xyz.com and https://www.xyz.com. A single wildcard label is allowed (https://*.loampassmx.com). Scheme defaults to https; paths are dropped. The saved list shows exactly what was stored."
                        persistent-hint></v-textarea>
                </v-card-text>
            </v-card>

            <!-- Platform Stripe key check: no-op Stripe call with the configured platform
                 secret key. One-click verification after a key cutover — proves the key is
                 valid and shows whether it's LIVE or TEST, without making a charge. -->
            <v-card class="mb-4">
                <v-card-title>Platform Stripe</v-card-title>
                <v-card-text>
                    <p class="text-body-2 text-medium-emphasis mb-3">
                        Round-trips a no-op call with the configured platform secret key (the account
                        platform-mode charges land on). Use after rotating keys to confirm the key works
                        and is in the mode you expect. Direct-charge tracks have their own per-tenant
                        test in the tenant dialog.
                    </p>
                    <v-btn color="primary" prepend-icon="mdi-credit-card-check-outline"
                        :loading="stripeTesting" @click="testPlatformStripe">
                        Test platform Stripe
                    </v-btn>

                    <v-alert v-if="stripeTestError" type="error" variant="tonal" density="compact" class="mt-3">
                        {{ stripeTestError }}
                    </v-alert>
                    <v-alert v-else-if="stripeTest" :type="stripeTest.chargesEnabled ? 'success' : 'warning'"
                        variant="tonal" density="compact" class="mt-3">
                        <div class="d-flex align-center ga-2 flex-wrap">
                            <v-chip size="small" label :color="stripeTest.liveMode ? 'success' : 'warning'">
                                {{ stripeTest.liveMode ? 'LIVE mode' : 'TEST mode' }}
                            </v-chip>
                            <span>{{ stripeTest.accountId }}</span>
                        </div>
                        <div class="text-body-2 mt-1">
                            Charges {{ stripeTest.chargesEnabled ? 'enabled' : 'DISABLED' }} ·
                            payouts {{ stripeTest.payoutsEnabled ? 'enabled' : 'DISABLED' }} ·
                            balance {{ fmtCents(stripeTest.availableCents) }} available,
                            {{ fmtCents(stripeTest.pendingCents) }} pending {{ stripeTest.currency }}
                        </div>
                    </v-alert>
                </v-card-text>
            </v-card>

            <!-- Staging-only: copy production down to staging. Rendered only when the
                 server reports it's the staging environment with the feature enabled. -->
            <v-card v-if="stageMirror?.available" class="mb-4">
                <v-card-title class="d-flex align-center">
                    <span>Staging data</span>
                    <v-chip v-if="stageMirror.state !== 'idle'" size="small" label class="ml-3"
                        :color="mirrorColor">{{ stageMirror.state }}</v-chip>
                </v-card-title>
                <v-card-text>
                    <v-alert type="warning" variant="tonal" density="comfortable" class="mb-4">
                        Wipes this <strong>staging</strong> database and reloads it from a fresh copy of
                        production, then scrubs PII and clears payment/SMS credentials. Production is read
                        from a read-only connection and is never modified. This can take a few minutes.
                    </v-alert>
                    <v-btn color="primary" prepend-icon="mdi-database-sync"
                        :loading="mirrorRunning" :disabled="mirrorRunning" @click="refreshStage">
                        Refresh staging from production
                    </v-btn>
                    <div v-if="stageMirror.startedBy || stageMirror.startedAtUtc"
                        class="text-caption text-medium-emphasis mt-2">
                        <span v-if="stageMirror.startedBy">Started by {{ stageMirror.startedBy }}</span>
                        <span v-if="stageMirror.startedAtUtc"> at {{ formatTime(stageMirror.startedAtUtc) }}</span>
                        <span v-if="stageMirror.finishedAtUtc"> · finished {{ formatTime(stageMirror.finishedAtUtc) }}</span>
                    </div>
                    <pre v-if="stageMirror.log" class="mirror-log mt-3">{{ stageMirror.log }}</pre>
                </v-card-text>
            </v-card>
        </template>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { SuperAdminService, type StageMirrorStatus } from '@/services/SuperAdminService'
import { useConfirm } from '@/composables/useConfirm'

const service = new SuperAdminService()
const confirm = useConfirm()

const loading = ref(true)
const loaded = ref(false)
const saving = ref(false)
const loadError = ref<string | null>(null)

// Edited as text (one origin per line); converted to/from string[] at the API boundary.
const originsText = ref('')
const placeholder = 'loampassmx.com\nhttps://*.loampassmx.com'

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')
function flash(text: string, color: 'success' | 'error' = 'success') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

function toLines(arr: string[]): string {
    return (arr ?? []).join('\n')
}
const origins = computed(() =>
    originsText.value.split(/[\s,]+/).map(s => s.trim()).filter(s => s.length > 0))

// --- Platform Stripe key check --------------------------------------------------
interface PlatformStripeTest {
    accountId: string
    liveMode: boolean
    chargesEnabled: boolean
    payoutsEnabled: boolean
    availableCents: number
    pendingCents: number
    currency: string
}
const stripeTesting = ref(false)
const stripeTest = ref<PlatformStripeTest | null>(null)
const stripeTestError = ref('')

function fmtCents(cents: number): string {
    return `$${(cents / 100).toFixed(2)}`
}

async function testPlatformStripe() {
    stripeTesting.value = true
    stripeTestError.value = ''
    stripeTest.value = null
    try {
        const r = await service.testPlatformStripe()
        stripeTest.value = (r.data as any).data as PlatformStripeTest
    } catch (err: any) {
        stripeTestError.value = err.response?.data?.error
            || 'Could not reach Stripe — check that Stripe__SecretKey is set and the API can reach stripe.com.'
    } finally {
        stripeTesting.value = false
    }
}

// --- Staging mirror (copy prod down to stage) ---------------------------------
const stageMirror = ref<StageMirrorStatus | null>(null)
const mirrorRunning = computed(() => stageMirror.value?.state === 'running')
const mirrorColor = computed(() => {
    switch (stageMirror.value?.state) {
        case 'running': return 'info'
        case 'succeeded': return 'success'
        case 'failed': return 'error'
        default: return 'default'
    }
})
let pollTimer: ReturnType<typeof setInterval> | null = null

function formatTime(utc: string): string {
    try { return new Date(utc).toLocaleString() } catch { return utc }
}

async function loadMirrorStatus() {
    try {
        const r = await service.getStageMirrorStatus()
        stageMirror.value = (r.data as any).data as StageMirrorStatus
        if (stageMirror.value.state === 'running') startPolling()
        else stopPolling()
    } catch {
        // status is best-effort; ignore (e.g. not staging)
    }
}

function startPolling() {
    if (pollTimer) return
    pollTimer = setInterval(loadMirrorStatus, 3000)
}
function stopPolling() {
    if (pollTimer) { clearInterval(pollTimer); pollTimer = null }
}

async function refreshStage() {
    const ok = await confirm({
        title: 'Refresh staging from production?',
        message: 'This wipes the staging database and reloads it from a fresh production copy '
            + '(then scrubs PII and clears payment/SMS credentials). Production is not modified. '
            + 'Anyone using staging will see it reset.',
        confirmText: 'Refresh staging',
        confirmColor: 'warning',
    })
    if (!ok) return
    try {
        const r = await service.startStageMirror()
        stageMirror.value = (r.data as any).data as StageMirrorStatus
        flash('Refresh started.')
        startPolling()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not start the refresh.', 'error')
    }
}

onUnmounted(stopPolling)

onMounted(() => {
    load()
    loadMirrorStatus()
})

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const r = await service.getMiscSettings()
        const data = (r.data as any).data as { globalEmbedAllowedOrigins: string[] }
        originsText.value = toLines(data.globalEmbedAllowedOrigins)
        loaded.value = true
    } catch (err: any) {
        loadError.value = err.response?.data?.error || 'Could not load settings.'
    } finally {
        loading.value = false
    }
}

async function save() {
    saving.value = true
    try {
        const r = await service.updateMiscSettings({ globalEmbedAllowedOrigins: origins.value })
        const data = (r.data as any).data as { globalEmbedAllowedOrigins: string[] }
        // Echo back the normalized list so the admin sees exactly what was stored.
        originsText.value = toLines(data.globalEmbedAllowedOrigins)
        flash('Saved.')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not save settings.', 'error')
    } finally {
        saving.value = false
    }
}
</script>

<style scoped>
.mirror-log {
    max-height: 320px;
    overflow: auto;
    background: rgba(0, 0, 0, 0.06);
    border-radius: 4px;
    padding: 8px 12px;
    font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
    font-size: 0.78rem;
    white-space: pre-wrap;
    word-break: break-word;
}
</style>
