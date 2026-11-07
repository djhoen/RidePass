<template>
    <!-- Today's kiosk sign-ups (Script0310), newest first, refreshed every 15 s while shown. Gate Sale passes
         "selectable" to pick one as the customer; Scan Tickets shows it compact and read-only. -->
    <v-card variant="outlined" class="pa-3 kiosk-signups">
        <div class="d-flex align-center mb-2">
            <v-icon class="mr-2" size="20">mdi-tablet-cellphone</v-icon>
            <div class="text-subtitle-2">Kiosk sign-ups today</div>
            <v-chip v-if="waitingCount > 0" size="x-small" color="primary" class="ml-2">{{ waitingCount }} waiting</v-chip>
            <v-spacer></v-spacer>
            <v-btn v-if="canManage" size="small" variant="text" to="/Admin/Kiosks">Manage kiosks</v-btn>
            <v-btn size="small" variant="text" icon="mdi-refresh" :loading="loading" aria-label="Refresh" @click="load"></v-btn>
        </div>
        <div v-if="error" class="text-caption text-error mb-1">{{ error }}</div>
        <div v-if="shown.length === 0 && !loading" class="text-caption text-medium-emphasis">
            {{ items.length === 0 ? 'Nobody has signed up at a kiosk today.' : 'Everyone from the kiosk has been served.' }}
        </div>
        <div v-for="s in shown" :key="s.id" class="kiosk-signup d-flex align-center ga-3 py-2"
            :class="{ 'kiosk-signup--done': s.handledAtUtc }">
            <GateRiderPhoto :source="s.photoId ? 'rider' : null" :rider-photo-id="s.photoId" :version="s.photoTakenAtUtc"
                :size="compact ? 40 : 52" :alt="`${s.firstName} ${s.lastName}`" />
            <div class="flex-grow-1" style="min-width: 0">
                <div class="text-body-2">
                    <strong>{{ s.firstName }} {{ s.lastName }}</strong>
                    <v-chip size="x-small" :color="s.isMinor ? 'warning' : undefined" variant="tonal" class="ml-1">
                        {{ s.isMinor ? `Under 18${s.age != null ? ` (${s.age})` : ''}` : s.age != null ? `${s.age}` : 'Adult' }}
                    </v-chip>
                    <v-chip size="x-small" :color="s.waiverSigned ? 'success' : 'error'" variant="tonal" class="ml-1">
                        {{ s.waiverSigned ? 'Waiver signed' : 'No waiver' }}
                    </v-chip>
                    <v-chip v-if="s.handledAtUtc" size="x-small" variant="tonal" class="ml-1">Sold</v-chip>
                </div>
                <div class="text-caption text-medium-emphasis">
                    <span v-if="s.guardianName">Guardian {{ s.guardianName }} · </span>{{ s.kioskName || 'Kiosk' }} · {{ time(s.createdAtUtc) }}
                </div>
            </div>
            <template v-if="selectable && !s.handledAtUtc">
                <v-btn size="small" color="primary" variant="tonal" @click="emit('select', s)">Select</v-btn>
                <v-btn size="small" variant="text" icon="mdi-close" aria-label="Dismiss" @click="dismiss(s)"></v-btn>
            </template>
        </div>
        <div v-if="hiddenHandled > 0" class="text-caption mt-1">
            <a href="#" @click.prevent="showHandled = !showHandled">{{ showHandled ? 'Hide' : 'Show' }} {{ hiddenHandled }} already served</a>
        </div>
    </v-card>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import GateRiderPhoto from '@/components/GateRiderPhoto.vue'
import { KioskAdminService, type KioskSignupItem } from '@/services/KioskService'
import { formatTenantDateTime } from '@/helpers/TenantTime'
import authHelper from '@/helpers/AuthHelper'
import { Perm } from '@/helpers/TenantPermissions'

const props = withDefaults(defineProps<{ selectable?: boolean; compact?: boolean; limit?: number }>(),
    { selectable: false, compact: false, limit: 20 })
const emit = defineEmits<{ (e: 'select', s: KioskSignupItem): void }>()

const service = new KioskAdminService()
const items = ref<KioskSignupItem[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const showHandled = ref(false)
const canManage = computed(() => authHelper.hasPermission(Perm.SettingsManage))
let timer: ReturnType<typeof setInterval> | null = null

const visible = computed(() => items.value.filter(s => !s.dismissedAtUtc))
const shown = computed(() => visible.value.filter(s => showHandled.value || !s.handledAtUtc).slice(0, props.limit))
const hiddenHandled = computed(() => visible.value.filter(s => s.handledAtUtc).length)
const waitingCount = computed(() => visible.value.filter(s => !s.handledAtUtc).length)

function time(utc: string) { return formatTenantDateTime(utc, 'h:mm A') }

async function load() {
    loading.value = true
    try {
        items.value = (await service.todaysSignups()).data.data
        error.value = null
    } catch (err: any) {
        // Keep the last list; say it is stale rather than showing "nobody signed up".
        error.value = err.response?.data?.error || 'Couldn’t refresh kiosk sign-ups.'
    } finally {
        loading.value = false
    }
}

async function dismiss(s: KioskSignupItem) {
    try { await service.dismissSignup(s.id); await load() }
    catch (err: any) { error.value = err.response?.data?.error || 'Couldn’t dismiss that sign-up.' }
}

defineExpose({ load })
onMounted(() => {
    void load()
    timer = setInterval(() => { void load() }, 15000)
})
onBeforeUnmount(() => { if (timer) clearInterval(timer) })
</script>

<style scoped>
.kiosk-signup + .kiosk-signup { border-top: 1px solid var(--rp-line); }
.kiosk-signup--done { opacity: 0.6; }
</style>
