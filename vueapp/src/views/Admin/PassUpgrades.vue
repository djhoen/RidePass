<template>
    <v-container fluid>
        <div class="d-flex align-center mb-4 flex-wrap ga-2">
            <h1 class="text-h5">Pass Upgrades</h1>
            <v-spacer />
            <v-btn variant="text" prepend-icon="mdi-refresh" :loading="loading" @click="load">Refresh</v-btn>
        </div>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <p class="text-body-2 text-medium-emphasis mb-1">
            What a holder can move up to, and what it costs them. Read a row as
            "a holder of this pass can upgrade to&hellip;".
        </p>
        <!-- The consequences a tenant would otherwise learn from an angry rider. -->
        <p class="text-caption text-medium-emphasis mb-4">
            An upgrade replaces the old pass: it stops working, its QR is reissued, and it isn't
            refunded. Price the move accordingly. Unused ride credits do not carry over.
        </p>

        <v-alert v-if="!loading && products.length < 2" type="info" variant="tonal" class="mb-4">
            You need at least two season pass products before you can offer an upgrade between them.
        </v-alert>

        <v-card v-else variant="outlined" :loading="loading">
            <div class="matrix-scroll">
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th class="corner">From \ To</th>
                            <th v-for="t in products" :key="'h' + t.id" class="text-center">
                                {{ t.name }}
                                <div v-if="!t.isActive" class="text-caption text-medium-emphasis">inactive</div>
                            </th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="f in products" :key="'r' + f.id">
                            <th class="row-head">
                                {{ f.name }}
                                <div v-if="!f.isActive" class="text-caption text-medium-emphasis">inactive</div>
                            </th>
                            <td v-for="t in products" :key="f.id + t.id" class="text-center cell">
                                <!-- A pass can't upgrade to itself; the constraint blocks it too. -->
                                <span v-if="f.id === t.id" class="text-disabled">n/a</span>
                                <template v-else>
                                    <v-btn v-if="!pathFor(f.id, t.id)" variant="text" size="small"
                                        class="text-medium-emphasis" @click="openCell(f, t)">
                                        No offer
                                    </v-btn>
                                    <v-btn v-else variant="tonal" size="small"
                                        :color="pathFor(f.id, t.id)!.isActive ? 'primary' : undefined"
                                        @click="openCell(f, t)">
                                        {{ priceLabel(pathFor(f.id, t.id)!.priceCents) }}
                                        <span v-if="!pathFor(f.id, t.id)!.isActive" class="ml-1 text-caption">(off)</span>
                                    </v-btn>
                                    <div v-if="pathFor(f.id, t.id)" class="text-caption text-medium-emphasis">
                                        {{ pathFor(f.id, t.id)!.eligibleHolders }} eligible
                                    </div>
                                </template>
                            </td>
                        </tr>
                    </tbody>
                </v-table>
            </div>
        </v-card>

        <!-- Is anyone actually being told about these offers? A link that reads the same whether
             or not an automation exists would teach nothing, so this reports the current state. -->
        <v-alert v-if="canManageCampaigns" :type="panelType" variant="tonal" density="compact" class="mt-4">
            <div class="d-flex align-center flex-wrap ga-2">
                <div>
                    <v-icon size="18" class="mr-1">mdi-email-outline</v-icon>
                    <template v-if="automationsLoadError">{{ automationsLoadError }}</template>
                    <template v-else-if="activeAutomations.length">
                        <strong>{{ offerEmailLabel }}</strong>
                        {{ activeSummary }}
                    </template>
                    <template v-else-if="automations.length">
                        An offer email is written but not turned on, so nobody is being told
                        about these upgrades yet.
                    </template>
                    <template v-else>
                        No one is being told about these upgrades. Set up an offer email that goes
                        out on its own after someone buys a pass.
                    </template>
                </div>
                <v-spacer />
                <v-btn size="small" variant="tonal" prepend-icon="mdi-robot-outline" to="/Admin/Automations">
                    {{ automations.length ? 'Review offer emails' : 'Set up an offer email' }}
                </v-btn>
            </div>
        </v-alert>

        <!-- ── Edit a cell ─────────────────────────────────────────────────── -->
        <v-dialog v-model="cellOpen" max-width="480">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Upgrade offer</span>
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="cellOpen = false" />
                </v-card-title>
                <v-divider />
                <v-card-text>
                    <v-alert v-if="actionError" type="error" variant="tonal" density="compact" class="mb-4">
                        {{ actionError }}
                    </v-alert>
                    <p class="text-body-2 mb-4">
                        <strong>{{ editFrom?.name }}</strong> holders can move to
                        <strong>{{ editTo?.name }}</strong>.
                    </p>
                    <v-text-field v-model.number="editPrice" type="number" min="0" step="0.01"
                        label="Upgrade price" prefix="$" density="compact"
                        hint="0 = free. This is what they pay to move, not the difference in list price."
                        persistent-hint />
                    <v-switch v-model="editActive" label="Offer this upgrade" color="primary"
                        density="compact" hide-details class="mt-4" />
                    <div v-if="existing" class="text-caption text-medium-emphasis mt-2">
                        {{ existing.eligibleHolders }} holders could take this today.
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-btn v-if="existing" color="error" variant="text" :loading="saving"
                        @click="removeOffer">Remove</v-btn>
                    <v-spacer />
                    <v-btn variant="text" @click="cellOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="saveOffer">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="toast" :timeout="4000" color="success" location="top">{{ toastText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import authHelper from '@/helpers/AuthHelper'
import { Perm } from '@/helpers/TenantPermissions'
import {
    SeasonPassService,
    type UpgradePathItem,
    type UpgradeProductOption,
} from '@/services/SeasonPassService'
import { AutomationService, type UpgradeAutomationStatus } from '@/services/AutomationService'

const service = new SeasonPassService()
const automationService = new AutomationService()

// Catalog rights don't imply marketing rights, so don't offer a link that would bounce.
const canManageCampaigns = computed(() => authHelper.hasPermission(Perm.CampaignsManage))

const automations = ref<UpgradeAutomationStatus[]>([])
const automationsLoadError = ref('')
const activeAutomations = computed(() => automations.value.filter(a => a.isActive))
const panelType = computed(() => {
    if (automationsLoadError.value) return 'warning' as const
    return activeAutomations.value.length ? ('success' as const) : ('info' as const)
})
/** Names the one automation, or counts them, rather than picking one arbitrarily. */
const offerEmailLabel = computed(() =>
    activeAutomations.value.length === 1
        ? `"${activeAutomations.value[0].name}" is on:`
        : `${activeAutomations.value.length} offer emails are on:`)
const activeSummary = computed(() => {
    const sent = activeAutomations.value.reduce((n, a) => n + a.sent, 0)
    const conv = activeAutomations.value.reduce((n, a) => n + a.conversions, 0)
    const delay = activeAutomations.value.length === 1 ? activeAutomations.value[0].firstDelayDays : null
    const when = delay === null ? '' : delay === 0 ? 'sent as soon as someone buys, ' : `sent ${delay} days after purchase, `
    return `${when}${sent} sent, ${conv} upgrade${conv === 1 ? '' : 's'} so far.`
})

const paths = ref<UpgradePathItem[]>([])
const products = ref<UpgradeProductOption[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)

const cellOpen = ref(false)
const editFrom = ref<UpgradeProductOption | null>(null)
const editTo = ref<UpgradeProductOption | null>(null)
const editPrice = ref(0)
const editActive = ref(true)
const existing = ref<UpgradePathItem | null>(null)
const saving = ref(false)
const actionError = ref<string | null>(null)
const toast = ref(false)
const toastText = ref('')

function pathFor(fromId: string, toId: string) {
    return paths.value.find(p => p.fromProductId === fromId && p.toProductId === toId) ?? null
}
function priceLabel(cents: number) {
    return cents <= 0 ? 'Free' : `$${(cents / 100).toFixed(2)}`
}

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const { data } = await service.listUpgrades()
        paths.value = data.data.paths
        products.value = data.data.products
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            ?? 'Could not load upgrade offers. Use Refresh to try again.'
    } finally {
        loading.value = false
    }
    // The panel is secondary to the matrix, so its failure must not take the page down. It still
    // has to say something: a silent empty panel would read as "nobody is being told", which is
    // the exact claim we could not verify.
    if (canManageCampaigns.value) {
        try {
            const { data } = await automationService.forUpgrades()
            automations.value = data.data
            automationsLoadError.value = ''
        } catch (err: any) {
            automations.value = []
            automationsLoadError.value = err.response?.data?.error
                ?? 'Could not check whether an offer email is running. Open Automations to see.'
        }
    }
}

function openCell(from: UpgradeProductOption, to: UpgradeProductOption) {
    editFrom.value = from
    editTo.value = to
    existing.value = pathFor(from.id, to.id)
    editPrice.value = existing.value ? existing.value.priceCents / 100 : 0
    editActive.value = existing.value ? existing.value.isActive : true
    actionError.value = null
    cellOpen.value = true
}

async function saveOffer() {
    if (!editFrom.value || !editTo.value) return
    saving.value = true
    actionError.value = null
    try {
        await service.upsertUpgrade({
            fromProductId: editFrom.value.id,
            toProductId: editTo.value.id,
            priceCents: Math.max(0, Math.round((Number(editPrice.value) || 0) * 100)),
            isActive: editActive.value,
        })
        cellOpen.value = false
        toastText.value = 'Upgrade offer saved.'
        toast.value = true
        await load()
    } catch (err: any) {
        actionError.value = err.response?.data?.error
            ?? 'Could not save the offer. Nothing was changed; check the price and try again.'
    } finally {
        saving.value = false
    }
}

async function removeOffer() {
    if (!existing.value) return
    saving.value = true
    actionError.value = null
    try {
        await service.deleteUpgrade(existing.value.id)
        cellOpen.value = false
        toastText.value = 'Upgrade offer removed.'
        toast.value = true
        await load()
    } catch (err: any) {
        actionError.value = err.response?.data?.error
            ?? 'Could not remove the offer. Nothing was changed; try again.'
    } finally {
        saving.value = false
    }
}

onMounted(load)
</script>

<style scoped>
/* Wide matrices scroll inside the card rather than the page. */
.matrix-scroll { overflow-x: auto; }
.corner, .row-head {
    position: sticky;
    left: 0;
    background: rgb(var(--v-theme-surface));
    z-index: 1;
    text-align: left;
    white-space: nowrap;
}
.cell { min-width: 130px; }
</style>
