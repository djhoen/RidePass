<template>
    <v-container fluid>
        <div class="d-flex align-center mb-2 flex-wrap ga-2">
            <span class="text-body-2 text-medium-emphasis">
                Who an email goes to. An audience is a set of rules, checked live every time it is used:
                buy a season pass tomorrow and you are in "Season pass holders" tomorrow.
            </span>
            <v-spacer />
            <v-btn variant="text" prepend-icon="mdi-refresh" :loading="loading" @click="load">Refresh</v-btn>
            <v-btn variant="tonal" prepend-icon="mdi-auto-fix" :loading="seeding" @click="addSamples">Add sample audiences</v-btn>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openNew">New audience</v-btn>
        </div>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <v-card variant="outlined" :loading="loading">
            <v-table density="compact">
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Who is in it</th>
                        <th class="text-right">People</th>
                        <th class="text-right">Used by</th>
                        <th class="text-right">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-if="!loading && items.length === 0">
                        <td colspan="5" class="text-center text-medium-emphasis py-6">
                            No audiences yet. Add the samples to start with season pass holders, abandoned
                            carts, and a few more, or build your own.
                        </td>
                    </tr>
                    <tr v-for="a in items" :key="a.id">
                        <td>
                            <div class="font-weight-medium">{{ a.name }}</div>
                            <div v-if="a.description" class="text-caption text-medium-emphasis">{{ a.description }}</div>
                        </td>
                        <td class="text-medium-emphasis">{{ a.summary }}</td>
                        <td class="text-right">{{ a.memberCount }}</td>
                        <td class="text-right text-no-wrap">
                            <span v-if="a.usedByCampaigns === 0 && a.usedByAutomations === 0" class="text-medium-emphasis">-</span>
                            <template v-else>
                                <span v-if="a.usedByCampaigns">{{ a.usedByCampaigns }} campaign{{ a.usedByCampaigns === 1 ? '' : 's' }}</span>
                                <span v-if="a.usedByCampaigns && a.usedByAutomations">, </span>
                                <span v-if="a.usedByAutomations">{{ a.usedByAutomations }} automation{{ a.usedByAutomations === 1 ? '' : 's' }}</span>
                            </template>
                        </td>
                        <td class="text-right text-no-wrap">
                            <v-tooltip text="Send a campaign to this audience">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" icon="mdi-email-fast-outline" variant="text" size="small"
                                        :to="{ path: '/Admin/Email/Campaigns', query: { audience: a.id } }" />
                                </template>
                            </v-tooltip>
                            <v-tooltip text="Edit">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" icon="mdi-pencil" variant="text" size="small" @click="openEdit(a)" />
                                </template>
                            </v-tooltip>
                            <v-tooltip text="Delete">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" icon="mdi-delete" variant="text" size="small" color="error" @click="remove(a)" />
                                </template>
                            </v-tooltip>
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>
        <div class="text-caption text-medium-emphasis mt-2">
            People counts refresh every hour and whenever an audience is saved. The builder shows the live number.
        </div>

        <!-- Builder -->
        <v-dialog v-model="editorOpen" max-width="900" scrollable>
            <v-card>
                <v-card-title class="d-flex align-center">
                    {{ editingId ? 'Edit audience' : 'New audience' }}
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="editorOpen = false" />
                </v-card-title>
                <v-divider />
                <v-card-text>
                    <v-alert v-if="editorError" type="error" variant="tonal" density="compact" class="mb-4">{{ editorError }}</v-alert>

                    <v-text-field v-model="form.name" label="Name" density="compact" placeholder="Season pass holders in NH" />
                    <v-text-field v-model="form.description" label="Description (optional)" density="compact" class="mt-4" />

                    <div class="text-subtitle-2 mt-6 mb-2">Start from</div>
                    <v-select v-model="form.definition.base" :items="baseItems" item-title="title" item-value="value"
                        density="compact" hide-details />

                    <div class="d-flex align-center mt-6 mb-2">
                        <span class="text-subtitle-2">Then keep people who match</span>
                        <v-btn-toggle v-model="form.definition.match" mandatory density="compact" variant="outlined" class="ml-3">
                            <v-btn value="all" size="small">all of these</v-btn>
                            <v-btn value="any" size="small">any of these</v-btn>
                        </v-btn-toggle>
                    </div>

                    <v-card v-for="(r, i) in form.definition.rules" :key="i" variant="outlined" class="pa-3 mb-3">
                        <div class="d-flex flex-wrap align-center ga-2">
                            <v-select :model-value="r.negate ? 'not' : 'is'" :items="negateItems" item-title="title" item-value="value"
                                density="compact" hide-details style="max-width: 110px"
                                @update:model-value="(v: string) => r.negate = v === 'not'" />
                            <v-select v-model="r.kind" :items="kindItems" item-title="title" item-value="value"
                                density="compact" hide-details style="max-width: 300px" @update:model-value="onKindChanged(r)" />
                            <v-spacer />
                            <v-btn icon="mdi-delete" variant="text" size="small" color="error" @click="form.definition.rules.splice(i, 1)" />
                        </div>

                        <!-- Per-kind inputs -->
                        <template v-if="r.kind === 'event_purchased'">
                            <v-autocomplete v-model="r.ids" :items="eventItems" item-title="title" item-value="id" multiple chips closable-chips
                                label="Events (leave empty for any event)" density="compact" class="mt-4" hide-details :loading="!options" />
                            <v-row dense class="mt-2">
                                <v-col cols="12" sm="6">
                                    <v-text-field :model-value="toLocalDate(r.fromUtc)" type="date" label="Events starting from (optional)"
                                        density="compact" hide-details @update:model-value="(v: string) => r.fromUtc = fromLocalDate(v, false)" />
                                </v-col>
                                <v-col cols="12" sm="6">
                                    <v-text-field :model-value="toLocalDate(r.toUtc, true)" type="date" label="Events starting through (optional)"
                                        density="compact" hide-details @update:model-value="(v: string) => r.toUtc = fromLocalDate(v, true)" />
                                </v-col>
                            </v-row>
                        </template>
                        <template v-else-if="r.kind === 'event_type_purchased'">
                            <v-autocomplete v-model="r.ids" :items="options?.eventTypes ?? []" item-title="name" item-value="id" multiple chips closable-chips
                                label="Event types" density="compact" class="mt-4" hide-details :loading="!options" />
                            <v-row dense class="mt-2">
                                <v-col cols="12" sm="6">
                                    <v-text-field :model-value="toLocalDate(r.fromUtc)" type="date" label="Events starting from (optional)"
                                        density="compact" hide-details @update:model-value="(v: string) => r.fromUtc = fromLocalDate(v, false)" />
                                </v-col>
                                <v-col cols="12" sm="6">
                                    <v-text-field :model-value="toLocalDate(r.toUtc, true)" type="date" label="Events starting through (optional)"
                                        density="compact" hide-details @update:model-value="(v: string) => r.toUtc = fromLocalDate(v, true)" />
                                </v-col>
                            </v-row>
                        </template>
                        <template v-else-if="r.kind === 'pass_holder'">
                            <v-autocomplete v-model="r.ids" :items="options?.passProducts ?? []" item-title="name" item-value="id" multiple chips closable-chips
                                label="Pass products (leave empty for any pass)" density="compact" class="mt-4" hide-details :loading="!options" />
                            <v-checkbox v-model="r.activeOnly" density="compact" hide-details label="Only passes that are still valid today" />
                        </template>
                        <template v-else-if="r.kind === 'pass_expiring'">
                            <v-text-field v-model.number="r.days" type="number" min="0" max="3650" label="Pass ends within this many days"
                                density="compact" class="mt-4" hide-details style="max-width: 280px" />
                        </template>
                        <template v-else-if="r.kind === 'abandoned_cart'">
                            <v-text-field v-model.number="r.days" type="number" min="1" max="365" label="Looking back this many days"
                                density="compact" class="mt-4" style="max-width: 280px"
                                hint="1 = yesterday. Today is never counted; a checkout from an hour ago may still finish." persistent-hint />
                        </template>
                        <template v-else-if="r.kind === 'postal_code'">
                            <v-combobox v-model="r.values" multiple chips closable-chips label="ZIP codes" density="compact" class="mt-4"
                                hint="Full or partial: 03053, or 030 for every ZIP starting with 030. Press Enter after each." persistent-hint />
                        </template>
                        <template v-else-if="r.kind === 'state'">
                            <v-combobox v-model="r.values" multiple chips closable-chips label="States (two-letter codes)" density="compact" class="mt-4"
                                hint="NH, VT, MA. Press Enter after each." persistent-hint />
                        </template>
                        <template v-else-if="r.kind === 'city'">
                            <v-combobox v-model="r.values" multiple chips closable-chips label="Cities" density="compact" class="mt-4"
                                hint="Press Enter after each." persistent-hint />
                        </template>
                    </v-card>
                    <v-btn variant="text" prepend-icon="mdi-plus" @click="form.definition.rules.push(emptyRule())">Add a filter</v-btn>

                    <v-divider class="my-4" />
                    <div class="text-caption">
                        <span v-if="previewLoading" class="text-medium-emphasis">Counting...</span>
                        <span v-else-if="previewError" class="text-error">{{ previewError }}</span>
                        <template v-else-if="preview">
                            <div class="text-success">
                                {{ preview.count }} {{ preview.count === 1 ? 'person' : 'people' }} right now: {{ preview.summary }}<template
                                    v-if="preview.suppressed"> ({{ preview.suppressed }} on the suppression list will be skipped)</template>
                            </div>
                            <div v-if="preview.sample.length" class="text-medium-emphasis mt-1">
                                For example: {{ preview.sample.map(p => p.name ? `${p.name} (${p.email})` : p.email).join(', ') }}
                            </div>
                        </template>
                    </div>
                    <div class="text-caption text-medium-emphasis mt-2">
                        Address filters use the address on the rider's account. Riders without one on file are left out of an
                        "is" address filter and included by an "is not" one.
                    </div>
                </v-card-text>
                <v-divider />
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="editorOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="save">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="toast" :timeout="6000" :color="toastColor" location="top">{{ toastText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import dayjs from 'dayjs'
import { useConfirm } from '@/composables/useConfirm'
import { branding } from '@/stores/branding'
import type { CampaignAudienceOptions } from '@/services/CampaignService'
import {
    AudienceService, emptyRule,
    type AudienceItem, type AudienceRule, type AudienceRuleKind, type AudiencePreview, type UpsertAudienceRequest,
} from '@/services/AudienceService'

const service = new AudienceService()
const confirm = useConfirm()
const tz = () => branding.timezone || 'UTC'

const items = ref<AudienceItem[]>([])
const loading = ref(false)
const loadError = ref('')
const seeding = ref(false)
const options = ref<CampaignAudienceOptions | null>(null)

const editorOpen = ref(false)
const editingId = ref<string | null>(null)
const editorError = ref('')
const saving = ref(false)
const form = ref<UpsertAudienceRequest>(emptyForm())

const preview = ref<AudiencePreview | null>(null)
const previewLoading = ref(false)
const previewError = ref('')

const toast = ref(false)
const toastText = ref('')
const toastColor = ref<'success' | 'error'>('success')

const baseItems = [
    { title: 'Everyone we have an email for (subscribers, buyers, unfinished checkouts)', value: 'everyone' },
    { title: 'Newsletter subscribers', value: 'subscribers' },
    { title: 'Customers (paid for a ticket or a pass)', value: 'customers' },
]
const negateItems = [
    { title: 'is', value: 'is' },
    { title: 'is not', value: 'not' },
]
const kindItems: { title: string; value: AudienceRuleKind }[] = [
    { title: 'Bought a ticket to an event', value: 'event_purchased' },
    { title: 'Bought a ticket to an event type', value: 'event_type_purchased' },
    { title: 'Holds a pass', value: 'pass_holder' },
    { title: 'Pass ends soon', value: 'pass_expiring' },
    { title: 'Left a checkout unfinished', value: 'abandoned_cart' },
    { title: 'ZIP code', value: 'postal_code' },
    { title: 'State', value: 'state' },
    { title: 'City', value: 'city' },
]
const eventItems = computed(() => (options.value?.events ?? []).map(e => ({
    id: e.id,
    title: `${e.title} (${dayjs(e.startsAtUtc).tz(tz()).format('MMM D, YYYY')}${e.status === 'cancelled' ? ', cancelled' : ''})`,
})))

function emptyForm(): UpsertAudienceRequest {
    return { name: '', description: null, definition: { base: 'everyone', match: 'all', rules: [] } }
}

function flash(text: string, color: 'success' | 'error') {
    toastText.value = text
    toastColor.value = color
    toast.value = true
}

// Date-only inputs in the track's timezone; "through" is stored as the start of the next day.
function toLocalDate(utc: string | null, exclusiveEnd = false): string {
    if (!utc) return ''
    const d = dayjs(utc).tz(tz())
    return (exclusiveEnd ? d.subtract(1, 'day') : d).format('YYYY-MM-DD')
}
function fromLocalDate(local: string, exclusiveEnd: boolean): string | null {
    if (!local) return null
    const d = dayjs.tz(local, tz())
    return (exclusiveEnd ? d.add(1, 'day') : d).utc().toISOString()
}

function onKindChanged(r: AudienceRule) {
    const fresh = emptyRule(r.kind)
    r.ids = []; r.values = []; r.days = fresh.days; r.activeOnly = fresh.activeOnly; r.fromUtc = null; r.toUtc = null
}

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        const { data } = await service.list()
        items.value = data.data
    } catch (err: any) {
        loadError.value = err.response?.data?.error || 'Could not load audiences. Refresh to try again.'
    } finally {
        loading.value = false
    }
}

async function loadOptions() {
    if (options.value) return
    try {
        const { data } = await service.options()
        options.value = data.data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load the events and passes for the filters. Reopen the audience to retry.', 'error')
    }
}

async function addSamples() {
    seeding.value = true
    try {
        const { data } = await service.addSamples()
        flash(data.data.added ? `Added ${data.data.added} sample audience${data.data.added === 1 ? '' : 's'}.` : 'You already have all the samples.', 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not add the sample audiences. Try again.', 'error')
    } finally {
        seeding.value = false
    }
}

function openNew() {
    editingId.value = null
    editorError.value = ''
    form.value = emptyForm()
    preview.value = null
    previewError.value = ''
    editorOpen.value = true
    loadOptions()
}

function openEdit(a: AudienceItem) {
    editingId.value = a.id
    editorError.value = ''
    form.value = {
        name: a.name,
        description: a.description,
        definition: {
            base: a.definition.base,
            match: a.definition.match,
            rules: a.definition.rules.map(r => ({ ...emptyRule(r.kind), ...r, ids: [...r.ids], values: [...r.values] })),
        },
    }
    preview.value = null
    previewError.value = ''
    editorOpen.value = true
    loadOptions()
}

let previewTimer: ReturnType<typeof setTimeout> | null = null
watch(() => form.value.definition, () => {
    if (!editorOpen.value) return
    if (previewTimer) clearTimeout(previewTimer)
    previewTimer = setTimeout(refreshPreview, 400)
}, { deep: true })
watch(editorOpen, open => { if (open) refreshPreview() })

async function refreshPreview() {
    previewLoading.value = true
    previewError.value = ''
    try {
        const { data } = await service.preview(form.value.definition)
        preview.value = data.data
    } catch (err: any) {
        preview.value = null
        previewError.value = err.response?.data?.error || 'Could not count this audience. Check the filters.'
    } finally {
        previewLoading.value = false
    }
}

async function save() {
    editorError.value = ''
    if (!form.value.name.trim()) { editorError.value = 'Give this audience a name.'; return }
    saving.value = true
    try {
        const req: UpsertAudienceRequest = {
            name: form.value.name.trim(),
            description: form.value.description?.trim() || null,
            definition: form.value.definition,
        }
        if (editingId.value) await service.update(editingId.value, req)
        else await service.create(req)
        editorOpen.value = false
        flash('Audience saved.', 'success')
        await load()
    } catch (err: any) {
        editorError.value = err.response?.data?.error || 'Could not save the audience. Check the filters and try again.'
    } finally {
        saving.value = false
    }
}

async function remove(a: AudienceItem) {
    if (!await confirm({ title: 'Delete audience?', message: `Delete "${a.name}"? Campaigns already sent to it keep their history.`, confirmText: 'Delete', confirmColor: 'error' })) return
    try {
        await service.delete(a.id)
        flash('Audience deleted.', 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || `Could not delete "${a.name}".`, 'error')
    }
}

onMounted(load)
</script>
