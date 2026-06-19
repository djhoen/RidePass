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
        </template>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { SuperAdminService } from '@/services/SuperAdminService'

const service = new SuperAdminService()

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

onMounted(load)

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const r = await service.getMiscSettings()
        const data = (r.data as any).data as { globalEmbedAllowedOrigins: string[] }
        originsText.value = toLines(data.globalEmbedAllowedOrigins)
        loaded.value = true
    } catch {
        loadError.value = 'Could not load settings.'
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
    } catch {
        flash('Could not save settings.', 'error')
    } finally {
        saving.value = false
    }
}
</script>
