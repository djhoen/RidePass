<template>
    <v-container>
        <h1 class="text-h4 mb-2">Profit Centers</h1>
        <p class="text-body-2 text-medium-emphasis mb-6">
            Name the parts of your business the way you report on them, and choose which revenue
            streams roll up into each. Reports and the QuickBooks account mapping group revenue by
            these buckets. Changes apply going forward; journal entries already posted to
            QuickBooks are not rewritten.
        </p>

        <div v-if="loading" class="d-flex align-center ga-3 py-4">
            <v-progress-circular indeterminate size="20" />
            <span class="text-body-2">Loading profit centers…</span>
        </div>

        <template v-else>
            <!-- ── Empty state: built-in grouping in force ─────────────────────── -->
            <v-card v-if="usingDefaults" class="pa-4 mb-6">
                <v-card-text class="px-0 pt-0">
                    <p class="text-body-2 mb-4">
                        You're using the standard groups: Tickets &amp; Passes, Training Center,
                        Food &amp; Beverage, Bike Shop and Other. Start from those and rename them,
                        or build your own from scratch.
                    </p>
                    <div class="d-flex ga-2 flex-wrap">
                        <v-btn color="primary" :loading="seeding" @click="seedDefaults">
                            Start from the standard groups
                        </v-btn>
                        <v-btn variant="tonal" @click="addCenter">Add a profit center</v-btn>
                    </div>
                </v-card-text>
            </v-card>

            <template v-else>
                <!-- ── Centers with draggable stream chips ─────────────────────── -->
                <draggable v-model="centers" item-key="id" handle=".center-drag-handle"
                    :animation="150" @end="persistOrder">
                    <template #item="{ element: center }">
                        <v-card class="mb-4">
                            <!-- The center's color is carried on the card edge as well as the
                                 swatch, so the page reads the same way the reports do. -->
                            <div class="center-stripe" :style="{ background: shown(center.color) }"></div>
                            <v-card-title class="d-flex align-center py-2">
                                <v-icon class="center-drag-handle mr-2" style="cursor: grab"
                                    size="small">mdi-drag</v-icon>
                                <template v-if="editingId === center.id">
                                    <ProfitCenterColorPicker v-model="editingColor" :swatches="palette.swatches"
                                        :total-series-color="palette.totalSeriesColor"
                                        :used-by="othersFor(center.id)" class="mr-2" />
                                    <v-text-field v-model="editingName" density="compact" variant="outlined"
                                        hide-details autofocus class="mr-2" style="max-width: 280px"
                                        @keyup.enter="saveEdit(center)" @keyup.esc="editingId = null" />
                                    <v-btn size="small" color="primary" variant="tonal" :loading="renaming"
                                        @click="saveEdit(center)">Save</v-btn>
                                    <v-btn size="small" variant="text" @click="editingId = null">Cancel</v-btn>
                                </template>
                                <template v-else>
                                    <span class="swatch mr-2" :style="{ background: shown(center.color) }"></span>
                                    <span class="text-subtitle-1">{{ center.name }}</span>
                                    <v-btn icon="mdi-pencil" size="x-small" variant="text" class="ml-1"
                                        @click="startEdit(center)" />
                                </template>
                                <v-spacer />
                                <v-btn icon="mdi-delete-outline" size="small" variant="text" color="error"
                                    @click="removeCenter(center)" />
                            </v-card-title>
                            <v-card-text>
                                <draggable v-model="center.streams" item-key="key" group="streams"
                                    class="stream-drop d-flex flex-wrap ga-2 pa-2" :animation="150"
                                    @change="persistAssignments">
                                    <template #item="{ element: stream }">
                                        <!-- The chip wears its center's color so a stream's home is
                                             readable at a glance and matches the reports. -->
                                        <v-chip variant="flat" style="cursor: grab"
                                            :style="{ background: shown(center.color), color: inkOn(shown(center.color)) }">
                                            <v-icon start size="x-small">mdi-drag</v-icon>
                                            {{ stream.label }}
                                        </v-chip>
                                    </template>
                                    <template #footer>
                                        <span v-if="center.streams.length === 0"
                                            class="text-caption text-medium-emphasis align-self-center px-2 py-1">
                                            Drag revenue streams here.
                                        </span>
                                    </template>
                                </draggable>
                            </v-card-text>
                        </v-card>
                    </template>
                </draggable>

                <!-- ── Ungrouped streams ───────────────────────────────────────── -->
                <v-card class="mb-4" variant="outlined">
                    <v-card-title class="text-subtitle-1 py-2">Ungrouped streams</v-card-title>
                    <v-card-subtitle class="text-caption">
                        These report under their standard group until you drag them into one of your
                        profit centers.
                    </v-card-subtitle>
                    <v-card-text>
                        <draggable v-model="unassigned" item-key="key" group="streams"
                            class="stream-drop d-flex flex-wrap ga-2 pa-2" :animation="150"
                            @change="persistAssignments">
                            <template #item="{ element: stream }">
                                <v-tooltip :text="`Currently reports under ${stream.defaultCenterLabel}`"
                                    location="top">
                                    <template #activator="{ props }">
                                        <v-chip v-bind="props" variant="outlined" style="cursor: grab">
                                            <v-icon start size="x-small">mdi-drag</v-icon>
                                            {{ stream.label }}
                                        </v-chip>
                                    </template>
                                </v-tooltip>
                            </template>
                            <template #footer>
                                <span v-if="unassigned.length === 0"
                                    class="text-caption text-medium-emphasis align-self-center px-2 py-1">
                                    Every stream has a home. Drag one here to send it back to the
                                    standard groups.
                                </span>
                            </template>
                        </draggable>
                    </v-card-text>
                </v-card>

                <v-btn variant="tonal" prepend-icon="mdi-plus" class="mb-6" @click="addCenter">
                    Add a profit center
                </v-btn>
            </template>

            <!-- ── Event routing ──────────────────────────────────────────────── -->
            <v-card class="mb-4">
                <v-card-title class="text-subtitle-1">Event revenue routing</v-card-title>
                <v-card-subtitle class="text-caption">
                    Lift days, races, camps and clinics are all events, so their ticket revenue is
                    split by event type. Choose which stream each type's sales post to; the stream's
                    profit center above decides where it reports.
                </v-card-subtitle>
                <v-card-text>
                    <v-row v-for="et in eventTypes" :key="et.id" class="align-center" dense>
                        <v-col cols="6" sm="4">{{ et.name }}</v-col>
                        <v-col cols="6" sm="5" md="4">
                            <v-select :model-value="et.revenueKey ?? DEFAULT_ROUTING"
                                :items="routingItems" item-title="label" item-value="key"
                                density="compact" variant="outlined" hide-details
                                :loading="routingSaving === et.id"
                                @update:model-value="v => saveRouting(et, v)" />
                        </v-col>
                        <v-col cols="12" sm="3" md="4" class="text-caption text-medium-emphasis">
                            reports under {{ routingCenterLabel(et) }}
                        </v-col>
                    </v-row>
                    <p v-if="eventTypes.length === 0" class="text-medium-emphasis text-body-2 mb-0">
                        No event types yet.
                    </p>
                </v-card-text>
            </v-card>
        </template>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="5000" location="top">
            {{ snackbarText }}
        </v-snackbar>

        <!-- New center dialog -->
        <v-dialog v-model="addDialog" max-width="420">
            <v-card>
                <v-card-title class="d-flex align-center">
                    New profit center
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="addDialog = false" />
                </v-card-title>
                <v-card-text>
                    <div class="d-flex align-center ga-2">
                        <ProfitCenterColorPicker v-model="newColor" :swatches="palette.swatches"
                            :total-series-color="palette.totalSeriesColor" :used-by="allUsedBy" />
                        <v-text-field v-model="newName" label="Name" density="compact" variant="outlined"
                            hide-details autofocus placeholder="e.g. Corp Tickets"
                            @keyup.enter="saveNewCenter" />
                    </div>
                    <div class="text-caption text-medium-emphasis mt-2">
                        This color identifies the center on every report and chart.
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="addDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="creating" @click="saveNewCenter">Create</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>
    </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import draggable from 'vuedraggable'
import { ProfitCenterService, type EventRouting, type ProfitCenterPalette, type RevenueStream } from '../../../services/ProfitCenterService'
import { useConfirm } from '../../../composables/useConfirm'
import { useTheme } from 'vuetify'
import ProfitCenterColorPicker from '../../../components/ProfitCenterColorPicker.vue'
import { inkOn, seriesColor, UNASSIGNED_COLOR } from '../../../helpers/profitCenterColor'

const DEFAULT_ROUTING = '__default__'

interface CenterVm {
    id: string
    name: string
    color: string
    streams: RevenueStream[]
}

const service = new ProfitCenterService()
const confirm = useConfirm()
const theme = useTheme()

/**
 * How a stored color is DISPLAYED in the current theme. The picker always shows raw hexes (it is
 * choosing the stored value), but everywhere the color is just identity it goes through here, so
 * a center looks the same on this page as it does on the reports and the chart.
 */
function shown(color: string): string {
    return seriesColor(color, theme.current.value.dark)
}

const loading = ref(true)
const seeding = ref(false)
const creating = ref(false)
const renaming = ref(false)
const routingSaving = ref<string | null>(null)

const usingDefaults = ref(true)
const centers = ref<CenterVm[]>([])
const unassigned = ref<RevenueStream[]>([])
const streams = ref<RevenueStream[]>([])
const eventTypes = ref<EventRouting[]>([])
const routingOptions = ref<RevenueStream[]>([])

const palette = ref<ProfitCenterPalette>({ swatches: [], totalSeriesColor: '' })

const editingId = ref<string | null>(null)
const editingName = ref('')
const editingColor = ref(UNASSIGNED_COLOR)
const addDialog = ref(false)
const newName = ref('')
const newColor = ref(UNASSIGNED_COLOR)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

function toast(text: string, color: 'success' | 'error' = 'success') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

/** Prefer the server's own message; it names the center or stream that's wrong. */
function errText(err: any, fallback: string): string {
    return err?.response?.data?.error || fallback
}

const routingItems = computed(() => [
    { key: DEFAULT_ROUTING, label: 'Event tickets (default)' },
    ...routingOptions.value.filter(o => o.key !== 'revenue_event_ticket'),
])

/** Which bucket an event type's revenue ends up reporting under, for the hint column. */
function routingCenterLabel(et: EventRouting): string {
    const key = et.revenueKey ?? 'revenue_event_ticket'
    const center = centers.value.find(c => c.streams.some(s => s.key === key))
    if (center) return center.name
    const stream = streams.value.find(s => s.key === key)
    return stream?.defaultCenterLabel ?? 'Other'
}

async function load() {
    loading.value = true
    try {
        const resp = await service.get()
        const data = resp.data.data
        usingDefaults.value = data.usingDefaults
        streams.value = data.streams
        eventTypes.value = data.eventTypes
        routingOptions.value = data.eventRoutingOptions
        palette.value = data.palette

        const byKey = new Map(data.streams.map(s => [s.key, s]))
        centers.value = data.centers.map(c => ({
            id: c.id,
            name: c.name,
            color: c.color,
            streams: c.revenueKeys.map(k => byKey.get(k)).filter((s): s is RevenueStream => !!s),
        }))
        const assigned = new Set(data.centers.flatMap(c => c.revenueKeys))
        unassigned.value = data.streams.filter(s => !assigned.has(s.key))
    } catch (err: any) {
        toast(errText(err, 'Could not load your profit centers. Refresh the page to try again.'), 'error')
    } finally {
        loading.value = false
    }
}

async function seedDefaults() {
    seeding.value = true
    try {
        await service.seedDefaults()
        toast('Standard groups created. Rename them to match how you report.')
        await load()
    } catch (err: any) {
        toast(errText(err, 'Could not create the standard groups.'), 'error')
    } finally {
        seeding.value = false
    }
}

/** Every OTHER center's color, so the picker can flag a duplicate before it reaches a chart. */
function othersFor(id: string) {
    return centers.value.filter(c => c.id !== id).map(c => ({ name: c.name, color: c.color }))
}
const allUsedBy = computed(() => centers.value.map(c => ({ name: c.name, color: c.color })))

function addCenter() {
    newName.value = ''
    // Open on the first palette color nobody is using yet, matching what the server would pick.
    newColor.value = palette.value.swatches.find(
        s => !centers.value.some(c => c.color.toLowerCase() === s.toLowerCase())) ?? UNASSIGNED_COLOR
    addDialog.value = true
}

async function saveNewCenter() {
    const name = newName.value.trim()
    if (!name) return
    creating.value = true
    try {
        await service.create(name, newColor.value)
        addDialog.value = false
        toast(`"${name}" created. Drag revenue streams into it.`)
        await load()
    } catch (err: any) {
        toast(errText(err, `Could not create "${name}".`), 'error')
    } finally {
        creating.value = false
    }
}

function startEdit(center: CenterVm) {
    editingId.value = center.id
    editingName.value = center.name
    editingColor.value = center.color
}

async function saveEdit(center: CenterVm) {
    const name = editingName.value.trim()
    const color = editingColor.value
    if (!name) return
    if (name === center.name && color === center.color) { editingId.value = null; return }
    renaming.value = true
    try {
        await service.update(center.id, name, color)
        center.name = name
        center.color = color
        editingId.value = null
        toast('Saved.')
    } catch (err: any) {
        toast(errText(err, `Could not save "${center.name}".`), 'error')
    } finally {
        renaming.value = false
    }
}

async function removeCenter(center: CenterVm) {
    const ok = await confirm({
        title: `Delete "${center.name}"?`,
        message: center.streams.length
            ? 'Its revenue streams go back to the standard groups; nothing already posted to QuickBooks changes.'
            : 'Nothing already posted to QuickBooks changes.',
        confirmText: 'Delete',
        confirmColor: 'error',
    })
    if (!ok) return
    try {
        await service.remove(center.id)
        toast(`"${center.name}" deleted.`)
        await load()
    } catch (err: any) {
        toast(errText(err, `Could not delete "${center.name}".`), 'error')
    }
}

async function persistOrder() {
    try {
        await service.reorder(centers.value.map((c, i) => ({ id: c.id, sortOrder: i })))
    } catch (err: any) {
        toast(errText(err, 'Could not save the new order.'), 'error')
        await load()
    }
}

// Bulk save on every drop: the full picture (each stream's center, or null when unassigned) is
// sent, so a missed intermediate event can never leave the server half-updated.
async function persistAssignments() {
    const assignments = [
        ...centers.value.flatMap(c => c.streams.map(s => ({ revenueKey: s.key, profitCenterId: c.id as string | null }))),
        ...unassigned.value.map(s => ({ revenueKey: s.key, profitCenterId: null as string | null })),
    ]
    try {
        await service.saveAssignments(assignments)
    } catch (err: any) {
        toast(errText(err, 'Could not save the stream assignment.'), 'error')
        await load()
    }
}

async function saveRouting(et: EventRouting, value: string) {
    const revenueKey = value === DEFAULT_ROUTING ? null : value
    routingSaving.value = et.id
    try {
        await service.setEventRouting(et.id, revenueKey)
        et.revenueKey = revenueKey
    } catch (err: any) {
        toast(errText(err, `Could not change where "${et.name}" revenue posts.`), 'error')
    } finally {
        routingSaving.value = null
    }
}

onMounted(load)
</script>

<script lang="ts">
export default { name: 'AdminSettingsProfitCenters' }
</script>

<style scoped>
/* A visible drop target even when empty, so "drag here" has a here. */
.stream-drop {
    min-height: 48px;
    border: 1px dashed rgba(var(--v-theme-on-surface), 0.2);
    border-radius: 8px;
}

/* The center's color along the top edge of its card: identity without tinting the whole card. */
.center-stripe {
    height: 4px;
    width: 100%;
}

.swatch {
    display: inline-block;
    width: 14px;
    height: 14px;
    border-radius: 4px;
    box-shadow: inset 0 0 0 1px rgba(var(--v-theme-on-surface), 0.2);
}
</style>
