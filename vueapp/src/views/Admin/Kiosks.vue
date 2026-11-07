<template>
    <v-container style="max-width: 880px">
        <div class="rp-pagehead">
            <div>
                <div class="rp-pagehead-title">Kiosks</div>
                <div class="rp-pagehead-sub">Front-desk tablets where riders create their profile, sign the waiver and take their photo.
                    Sign-ups show on Gate Sale and Scan Tickets.</div>
            </div>
        </div>

        <v-card class="pa-4 mb-4">
            <div class="text-subtitle-1 mb-2">Add a kiosk</div>
            <div class="d-flex ga-2 align-center flex-wrap">
                <v-text-field v-model="newName" label="Name (e.g. Gate kiosk 1)" density="compact" hide-details style="max-width: 360px"
                    @keyup.enter="create"></v-text-field>
                <v-btn color="primary" :loading="creating" :disabled="!newName.trim()" @click="create">Add kiosk</v-btn>
            </div>
            <p class="text-caption text-medium-emphasis mt-2">
                On the tablet, open <code>{{ kioskUrl }}</code> and type the pairing code. The tablet gets its own kiosk key;
                no staff login stays on it.
            </p>
        </v-card>

        <v-alert v-if="pairing" type="info" variant="tonal" class="mb-4">
            <div class="text-subtitle-1">Pairing code for <strong>{{ pairing.name }}</strong></div>
            <div class="kiosk-code my-2">{{ pairing.code }}</div>
            <div class="text-caption">Type it on the tablet at {{ kioskUrl }}. Works once, until {{ formatTime(pairing.expiresAtUtc) }}.</div>
        </v-alert>

        <v-card class="pa-2">
            <div v-if="loading" class="text-center pa-6"><v-progress-circular indeterminate></v-progress-circular></div>
            <div v-else-if="devices.length === 0" class="text-medium-emphasis pa-4">No kiosks yet.</div>
            <v-table v-else density="comfortable">
                <thead>
                    <tr><th>Name</th><th>Status</th><th>Last seen</th><th></th></tr>
                </thead>
                <tbody>
                    <tr v-for="d in devices" :key="d.id" :class="{ 'text-medium-emphasis': d.revokedAtUtc }">
                        <td>
                            <template v-if="editingId === d.id">
                                <v-text-field v-model="editName" density="compact" hide-details style="min-width: 180px"
                                    @keyup.enter="saveRename(d)"></v-text-field>
                            </template>
                            <template v-else>{{ d.name }}</template>
                        </td>
                        <td>
                            <v-chip v-if="d.revokedAtUtc" size="small" variant="tonal">Removed</v-chip>
                            <v-chip v-else-if="d.paired" size="small" color="success" variant="tonal">Paired</v-chip>
                            <v-chip v-else-if="d.pairingPending" size="small" color="warning" variant="tonal">Waiting for code</v-chip>
                            <v-chip v-else size="small" variant="tonal">Not paired</v-chip>
                        </td>
                        <td>{{ d.lastSeenAtUtc ? formatTime(d.lastSeenAtUtc) : '—' }}</td>
                        <td class="text-right" style="white-space: nowrap">
                            <template v-if="!d.revokedAtUtc">
                                <template v-if="editingId === d.id">
                                    <v-btn size="small" variant="text" @click="editingId = null">Cancel</v-btn>
                                    <v-btn size="small" color="primary" variant="tonal" @click="saveRename(d)">Save</v-btn>
                                </template>
                                <template v-else>
                                    <v-btn size="small" variant="text" @click="startRename(d)">Rename</v-btn>
                                    <v-btn size="small" variant="text" @click="repair(d)">{{ d.paired ? 'Re-pair' : 'New code' }}</v-btn>
                                    <v-btn size="small" variant="text" color="error" @click="revoke(d)">Remove</v-btn>
                                </template>
                            </template>
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>
        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { KioskAdminService, type KioskDeviceItem, type KioskPairingCode } from '@/services/KioskService'
import { formatTenantDateTime } from '@/helpers/TenantTime'
import { useConfirm } from '@/composables/useConfirm'

const service = new KioskAdminService()
const confirm = useConfirm()
const devices = ref<KioskDeviceItem[]>([])
const loading = ref(false)
const creating = ref(false)
const newName = ref('')
const pairing = ref<KioskPairingCode | null>(null)
const editingId = ref<string | null>(null)
const editName = ref('')
const kioskUrl = `${window.location.origin}/Kiosk`
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
function formatTime(utc: string) { return formatTenantDateTime(utc, 'MMM D, h:mm A') }

async function load() {
    loading.value = true
    try { devices.value = (await service.listDevices()).data.data }
    catch (err: any) { flash(err.response?.data?.error || 'Couldn’t load kiosks.', 'error') }
    finally { loading.value = false }
}

async function create() {
    creating.value = true
    try {
        pairing.value = (await service.createDevice(newName.value.trim())).data.data
        newName.value = ''
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Couldn’t add the kiosk.', 'error')
    } finally {
        creating.value = false
    }
}

async function repair(d: KioskDeviceItem) {
    if (d.paired) {
        const ok = await confirm({
            title: `Re-pair ${d.name}?`,
            message: 'A new code pairs a tablet as this kiosk. Once it is used, the tablet paired now stops working.',
            confirmText: 'New code',
        })
        if (!ok) return
    }
    try { pairing.value = (await service.newPairingCode(d.id)).data.data; await load() }
    catch (err: any) { flash(err.response?.data?.error || 'Couldn’t make a pairing code.', 'error') }
}

function startRename(d: KioskDeviceItem) { editingId.value = d.id; editName.value = d.name }
async function saveRename(d: KioskDeviceItem) {
    try { await service.rename(d.id, editName.value.trim()); editingId.value = null; await load() }
    catch (err: any) { flash(err.response?.data?.error || 'Couldn’t rename the kiosk.', 'error') }
}

async function revoke(d: KioskDeviceItem) {
    const ok = await confirm({
        title: `Remove ${d.name}?`,
        message: 'The tablet stops working as a kiosk immediately. Its past sign-ups stay on the gate lists.',
        confirmText: 'Remove', confirmColor: 'error',
    })
    if (!ok) return
    try { await service.revoke(d.id); if (pairing.value?.deviceId === d.id) pairing.value = null; await load() }
    catch (err: any) { flash(err.response?.data?.error || 'Couldn’t remove the kiosk.', 'error') }
}

onMounted(load)
</script>

<style scoped>
.kiosk-code { font-size: 2rem; font-weight: 700; letter-spacing: 0.15em; font-family: ui-monospace, monospace; }
</style>
