<!--
    Connect a distributor account so the catalog keeps itself current.

    The point of this screen is that nobody types manufacturer data by hand. Connect once, and a
    nightly job refreshes names, part numbers, barcodes and dealer cost.

    Two things it is careful about, both visible in the copy:
      * Credentials are the SHOP's own. Content feeds are licensed per dealer, so each shop supplies
        the key their distributor issued them.
      * The sync updates identity and cost, never the shop's retail prices and never stock levels.
        A shop needs to believe that before handing over a login.
-->
<template>
    <div>
        <p class="text-body-2 text-medium-emphasis mb-4">
            Connect your distributor account and your catalog keeps itself current: product names,
            part numbers, barcodes and your dealer cost refresh automatically every night. Your own
            retail prices and stock counts are never changed by a sync.
        </p>

        <div v-if="loading" class="py-6 text-center">
            <v-progress-circular indeterminate size="24"></v-progress-circular>
        </div>

        <div v-else-if="loadError" class="text-error text-body-2">{{ loadError }}</div>

        <v-card v-for="d in distributors" :key="d.slug" class="pa-4 mb-3" max-width="720">
            <div class="d-flex align-center ga-3 flex-wrap mb-2">
                <div class="text-subtitle-1">{{ d.displayName }}</div>
                <v-chip v-if="d.connected && d.isEnabled" size="x-small" color="success">Connected</v-chip>
                <v-chip v-else-if="d.connected" size="x-small">Paused</v-chip>
                <v-spacer></v-spacer>
                <div v-if="d.lastSyncAtUtc" class="text-caption text-medium-emphasis">
                    Last sync {{ formatLocal(d.lastSyncAtUtc) }}
                </div>
            </div>

            <!-- Said up front rather than after they've entered a key and waited a night for
                 nothing to happen. -->
            <v-alert v-if="!d.isAvailable" type="info" variant="tonal" density="compact" class="mb-3">
                Automatic syncing with {{ d.displayName }} isn't switched on yet. You can still keep
                the catalog current by exporting it from your distributor and uploading the CSV under
                <router-link to="/Admin/BikeShop">Inventory &rarr; Import CSV</router-link>.
            </v-alert>

            <v-alert v-else-if="d.lastStatus === 'error' && d.lastError" type="error" variant="tonal"
                density="compact" class="mb-3">
                Last sync failed: {{ d.lastError }}
            </v-alert>

            <v-alert v-else-if="d.lastStatus === 'ok'" type="success" variant="tonal" density="compact" class="mb-3">
                Last sync brought in {{ d.lastProductsSeen }} products and updated
                {{ d.lastVariantsUpdated }} {{ d.lastVariantsUpdated === 1 ? 'variant' : 'variants' }}.
            </v-alert>

            <p class="text-caption text-medium-emphasis mb-3">
                Use the credentials {{ d.displayName }} issued to your shop. Catalog feeds are
                licensed per dealer, so these have to be your own account's.
            </p>

            <v-text-field v-model="forms[d.slug].accountNumber" label="Dealer account number"
                density="compact"></v-text-field>
            <v-text-field v-model="forms[d.slug].username" label="Login name" density="compact"
                class="mt-4"></v-text-field>
            <v-text-field v-model="forms[d.slug].password" type="password" density="compact" class="mt-4"
                label="Password" autocomplete="new-password"
                :placeholder="d.hasPassword ? 'Saved. Leave blank to keep it.' : ''"
                persistent-placeholder></v-text-field>
            <v-text-field v-model="forms[d.slug].apiKey" type="password" density="compact" class="mt-4"
                label="Content licensing (CLS) API key" autocomplete="new-password"
                :placeholder="d.hasApiKey ? 'Saved. Leave blank to keep it.' : ''"
                persistent-placeholder
                hint="The key your distributor issues for product content. Stored encrypted."
                persistent-hint></v-text-field>

            <v-switch v-model="forms[d.slug].isEnabled" color="primary" density="compact" hide-details
                class="mt-2" label="Sync this account automatically"></v-switch>

            <div class="d-flex ga-3 flex-wrap mt-4">
                <v-btn color="primary" :loading="busy[d.slug] === 'save'" @click="save(d)">
                    {{ d.connected ? 'Save' : 'Connect' }}
                </v-btn>
                <v-btn v-if="d.connected" variant="tonal" :disabled="!d.isAvailable"
                    :loading="busy[d.slug] === 'test'" @click="test(d)">Test connection</v-btn>
                <v-btn v-if="d.connected" variant="tonal" :disabled="!d.isAvailable"
                    :loading="busy[d.slug] === 'sync'" @click="syncNow(d)">Sync now</v-btn>
                <v-spacer></v-spacer>
                <v-btn v-if="d.connected" variant="text" color="error"
                    :loading="busy[d.slug] === 'delete'" @click="disconnect(d)">Disconnect</v-btn>
            </div>

            <div v-if="errors[d.slug]" class="text-error text-caption mt-2">{{ errors[d.slug] }}</div>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackColor" :timeout="4000">{{ snackText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import dayjs from 'dayjs'
import { BikeShopService, type DistributorConnection } from '@/services/BikeShopService'
import { useConfirm } from '@/composables/useConfirm'

const service = new BikeShopService()
const confirm = useConfirm()

const distributors = ref<DistributorConnection[]>([])
const loading = ref(true)
const loadError = ref('')
const busy = reactive<Record<string, string | null>>({})
const errors = reactive<Record<string, string>>({})

// One form per distributor. Secrets start EMPTY and stay empty: the server never sends a stored
// key back, and a blank field means "keep what's saved" rather than "clear it".
const forms = reactive<Record<string, {
    accountNumber: string; username: string; password: string; apiKey: string; isEnabled: boolean
}>>({})

const snackbar = ref(false)
const snackText = ref('')
const snackColor = ref<'success' | 'error'>('success')
function flash(text: string, color: 'success' | 'error' = 'success') {
    snackText.value = text; snackColor.value = color; snackbar.value = true
}

function formatLocal(utc: string) { return dayjs(utc).format('MMM D, YYYY h:mm A') }

function hydrate(list: DistributorConnection[]) {
    distributors.value = list
    for (const d of list) {
        forms[d.slug] = {
            accountNumber: d.accountNumber ?? '',
            username: d.username ?? '',
            password: '',
            apiKey: '',
            isEnabled: d.connected ? d.isEnabled : true,
        }
        busy[d.slug] = null
        errors[d.slug] = ''
    }
}

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        hydrate((await service.listDistributors()).data.data)
    } catch (e: any) {
        loadError.value = e.response?.data?.error
            || 'Could not load your distributor connections. Refresh to try again.'
    } finally {
        loading.value = false
    }
}

async function save(d: DistributorConnection) {
    busy[d.slug] = 'save'
    errors[d.slug] = ''
    try {
        const f = forms[d.slug]
        const res = await service.connectDistributor({
            distributor: d.slug,
            accountNumber: f.accountNumber.trim() || null,
            username: f.username.trim() || null,
            // Blank means keep the stored secret, so send null rather than an empty string.
            password: f.password.trim() || null,
            apiKey: f.apiKey.trim() || null,
            isEnabled: f.isEnabled,
        })
        hydrate(res.data.data)
        flash(`${d.displayName} saved.`, 'success')
    } catch (e: any) {
        errors[d.slug] = e.response?.data?.error
            || `Could not save your ${d.displayName} connection. Check the details and try again.`
    } finally {
        busy[d.slug] = null
    }
}

async function test(d: DistributorConnection) {
    busy[d.slug] = 'test'
    errors[d.slug] = ''
    try {
        const res = (await service.testDistributor(d.slug)).data.data
        if (res.ok) flash(`${d.displayName} credentials work.`, 'success')
        else errors[d.slug] = res.error || `${d.displayName} rejected those credentials.`
    } catch (e: any) {
        errors[d.slug] = e.response?.data?.error
            || `Could not reach ${d.displayName} to test the connection. Try again shortly.`
    } finally {
        busy[d.slug] = null
    }
}

async function syncNow(d: DistributorConnection) {
    busy[d.slug] = 'sync'
    errors[d.slug] = ''
    try {
        const res = (await service.syncDistributor(d.slug)).data.data
        flash(`Synced ${res.productsSeen} products: ${res.variantsCreated} added, `
            + `${res.variantsUpdated} updated.`, 'success')
        await load()
    } catch (e: any) {
        errors[d.slug] = e.response?.data?.error
            || `The ${d.displayName} sync did not finish. Check the credentials and try again.`
    } finally {
        busy[d.slug] = null
    }
}

async function disconnect(d: DistributorConnection) {
    const ok = await confirm({
        title: `Disconnect ${d.displayName}?`,
        message: 'Your products stay exactly as they are. They just stop refreshing automatically, '
            + 'and the saved credentials are deleted.',
        confirmText: 'Disconnect',
        confirmColor: 'error',
    })
    if (!ok) return
    busy[d.slug] = 'delete'
    errors[d.slug] = ''
    try {
        hydrate((await service.disconnectDistributor(d.slug)).data.data)
        flash(`${d.displayName} disconnected.`, 'success')
    } catch (e: any) {
        errors[d.slug] = e.response?.data?.error
            || `Could not disconnect ${d.displayName}. Try again.`
    } finally {
        busy[d.slug] = null
    }
}

onMounted(load)
</script>
