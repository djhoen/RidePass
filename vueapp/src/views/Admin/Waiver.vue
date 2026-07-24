<template>
    <v-container>
        <div class="d-flex align-center mb-4 flex-wrap ga-3">
            <h1 class="text-h4">Waivers</h1>
            <v-spacer></v-spacer>
            <v-switch v-model="showInactive" color="primary" density="compact" hide-details
                label="Show inactive / expired" style="flex: 0 0 auto"></v-switch>
            <v-btn variant="text" @click="load">Refresh</v-btn>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">Add Waiver</v-btn>
        </div>

        <p class="text-caption text-medium-emphasis mb-4">
            Define multiple waivers (e.g. General, Race Day, Minor) and attach them to specific events.
            Riders sign each waiver once — separate waivers are tracked separately.
        </p>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Title</th>
                        <th style="width: 90px">Version</th>
                        <th style="width: 130px">Expires</th>
                        <th style="width: 90px">Active</th>
                        <th style="width: 180px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="row in visibleRows" :key="row.id">
                        <td><strong>{{ row.name }}</strong></td>
                        <td>{{ row.title }}</td>
                        <td>v{{ row.version }}</td>
                        <td>
                            <span v-if="!row.expiresAtUtc" class="text-medium-emphasis">—</span>
                            <span v-else :class="isExpired(row) ? 'text-error' : 'text-medium-emphasis'">
                                <v-icon v-if="isExpired(row)" size="small" class="mr-1">mdi-clock-alert-outline</v-icon>
                                {{ formatExpires(row.expiresAtUtc) }}
                            </span>
                        </td>
                        <td>
                            <v-icon v-if="row.isActive" color="success">mdi-check</v-icon>
                            <v-icon v-else color="grey">mdi-close</v-icon>
                        </td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" @click="openEdit(row)">Edit</v-btn>
                            <v-btn variant="text" size="small" @click="openDuplicate(row)">Duplicate</v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && visibleRows.length === 0">
                        <td colspan="6" class="text-center text-medium-emphasis py-8">
                            <span v-if="rows.length === 0">No waivers yet. Click "Add Waiver" to create one.</span>
                            <span v-else>Nothing currently active. Toggle "Show inactive / expired" to see all.</span>
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="dialog" max-width="900">
            <v-card class="d-flex flex-column" style="max-height: 90vh">
                <v-card-title class="d-flex align-center">
                    <span>{{ editing ? 'Edit Waiver' : 'New Waiver' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="dialog = false"></v-btn>
                </v-card-title>
                <v-tabs v-model="activeTab" color="primary" grow style="flex: 0 0 auto">
                    <v-tab value="waiver">Waiver</v-tab>
                    <v-tab value="events">Associated Events</v-tab>
                </v-tabs>
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                    <v-window v-model="activeTab" class="mt-4">
                        <v-window-item value="waiver">
                            <v-row>
                                <v-col cols="12" md="6">
                                    <v-text-field v-model="form.name" label="Name (admin label)"
                                        density="compact" hint="e.g. Race Day, Minor, General"
                                        persistent-hint maxlength="120"></v-text-field>
                                </v-col>
                                <v-col cols="12" md="6">
                                    <v-text-field v-model="form.title" label="Title (shown to riders)"
                                        density="compact" maxlength="200"></v-text-field>
                                </v-col>
                            </v-row>
                            <v-row>
                                <v-col cols="12" md="6">
                                    <v-text-field v-model="expiresAtDate" type="date"
                                        label="Stops being usable on (optional)" density="compact"
                                        hint="After this date the waiver hides from event-attachment selectors. Existing signatures stay valid."
                                        persistent-hint clearable></v-text-field>
                                </v-col>
                                <v-col cols="12" md="6" class="d-flex align-center">
                                    <v-switch v-model="form.isActive" color="primary" density="compact"
                                        label="Active" hide-details></v-switch>
                                </v-col>
                            </v-row>
                            <label class="text-subtitle-2 d-block mb-1 mt-4">Body</label>
                            <RichTextEditor v-model="form.body" />

                            <v-alert v-if="editing" type="info" variant="tonal" density="compact" class="mt-4">
                                Editing changes the legal text on this waiver in place. Riders who already signed are still recorded
                                as having signed. If you've changed the substance of the agreement, create a new waiver instead so
                                signatures stay tied to the version they actually saw.
                            </v-alert>
                        </v-window-item>

                        <v-window-item value="events">
                            <p v-if="!editing" class="text-medium-emphasis py-4">
                                Save the waiver first, then re-open it to associate events.
                            </p>
                            <template v-else>
                                <p class="text-caption text-medium-emphasis mb-3">
                                    Events that point at this waiver. For each one, check whether the waiver
                                    covers riders, spectators, or both. Removing an event detaches the waiver —
                                    that event then falls back to the tenant's default waiver (if any).
                                </p>
                                <v-table density="compact">
                                    <thead>
                                        <tr>
                                            <th>Event</th>
                                            <th style="width: 100px" class="text-center">Rider</th>
                                            <th style="width: 110px" class="text-center">Spectator</th>
                                            <th style="width: 70px"></th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        <tr v-for="row in associatedEvents" :key="row.id">
                                            <td>
                                                <div>{{ row.title }}</div>
                                                <div class="text-caption text-medium-emphasis">
                                                    {{ formatAssocDate(row.startsAtUtc) }}
                                                </div>
                                            </td>
                                            <td class="text-center">
                                                <v-checkbox :model-value="row.asRider"
                                                    @update:model-value="persistRole(row, $event === true, row.asSpectator)"
                                                    :disabled="eventsBusy"
                                                    density="compact" hide-details color="primary"
                                                    class="d-inline-flex"></v-checkbox>
                                            </td>
                                            <td class="text-center">
                                                <v-checkbox :model-value="row.asSpectator"
                                                    @update:model-value="persistRole(row, row.asRider, $event === true)"
                                                    :disabled="eventsBusy"
                                                    density="compact" hide-details color="primary"
                                                    class="d-inline-flex"></v-checkbox>
                                            </td>
                                            <td>
                                                <v-btn icon="mdi-close" variant="text" size="small"
                                                    :disabled="eventsBusy"
                                                    @click="removeEventAssociation(row)"></v-btn>
                                            </td>
                                        </tr>
                                        <tr v-if="associatedEvents.length === 0">
                                            <td colspan="4" class="text-center text-medium-emphasis py-4">
                                                No events use this waiver yet. Pick one below to attach it.
                                            </td>
                                        </tr>
                                    </tbody>
                                </v-table>

                                <div class="d-flex align-end ga-3 mt-4">
                                    <v-autocomplete v-model="newEventId"
                                        :items="availableEvents" item-title="label" item-value="id"
                                        label="Add an event" placeholder="Start typing…"
                                        density="compact" hide-details clearable
                                        style="flex: 1 1 auto"></v-autocomplete>
                                    <v-btn color="primary" :disabled="!newEventId || eventsBusy"
                                        :loading="eventsBusy" @click="addEventAssociation">
                                        Add
                                    </v-btn>
                                </div>
                            </template>
                        </v-window-item>
                    </v-window>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="dialog = false">{{ activeTab === 'events' ? 'Close' : 'Cancel' }}</v-btn>
                    <v-btn v-if="activeTab === 'waiver'" color="primary"
                        :loading="saving" :disabled="!canSave" @click="save">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import dayjs from 'dayjs'
import { formatTenantDate, formatTenantDateTime } from '@/helpers/TenantTime'
import { WaiverService, type WaiverDto, type WaiverEventAssociation } from '@/services/WaiverService'
import { EventService, type EventDto } from '@/services/EventService'
import { branding } from '@/stores/branding'
import RichTextEditor from '@/components/RichTextEditor.vue'

const service = new WaiverService()
const eventService = new EventService()

const rows = ref<WaiverDto[]>([])
const loading = ref(false)
const showInactive = ref(false)
// Mirror the Add-ons admin pattern: default to "currently usable" — active and
// not past its expiration. Toggle reveals everything.
const visibleRows = computed(() => showInactive.value
    ? rows.value
    : rows.value.filter(r => r.isActive && !isExpired(r)))

const dialog = ref(false)
const editing = ref<WaiverDto | null>(null)
const saving = ref(false)
const form = ref({
    name: '',
    title: '',
    body: '',
    isActive: true,
    expiresAtUtc: null as string | null,
})

const expiresAtDate = computed<string | null>({
    get: () => form.value.expiresAtUtc
        ? dayjs.utc(form.value.expiresAtUtc).tz(branding.timezone || 'UTC').format('YYYY-MM-DD') : null,
    set: (v: string | null) => {
        if (!v) { form.value.expiresAtUtc = null; return }
        // End of the selected day in the tenant's timezone, stored as UTC.
        form.value.expiresAtUtc = dayjs.tz(`${v}T23:59:59`, branding.timezone || 'UTC').utc().toISOString()
    },
})

// A waiver with empty legal text is worthless; require body content (stripped of the
// rich-text wrapper markup, so an empty "<p></p>" doesn't count).
const bodyText = computed(() => (form.value.body || '').replace(/<[^>]*>/g, '').replace(/&nbsp;/g, ' ').trim())
const canSave = computed(() => form.value.name.trim().length > 0
    && form.value.title.trim().length > 0
    && bodyText.value.length > 0)

// Tabs inside the dialog: "Waiver" carries the existing form, "Associated
// Events" lists the events that use this waiver and lets the admin attach
// new ones. The events tab is empty when there's no waiver id yet (create
// / duplicate) — the admin saves first, then re-opens to manage events.
const activeTab = ref<'waiver' | 'events'>('waiver')
const associatedEvents = ref<WaiverEventAssociation[]>([])
const allEvents = ref<EventDto[]>([])
const eventsLoaded = ref(false)
const newEventId = ref<string | null>(null)
const eventsBusy = ref(false)

// Events not yet attached to this waiver — what the picker offers.
const availableEvents = computed(() => {
    const attached = new Set(associatedEvents.value.map(e => e.id))
    return allEvents.value
        .filter(e => !attached.has(e.id))
        .map(e => ({
            id: e.id,
            label: `${e.title} — ${formatTenantDate(e.startsAtUtc, 'MMM D, YYYY')}`,
            startsAtUtc: e.startsAtUtc,
        }))
        .sort((a, b) => a.startsAtUtc.localeCompare(b.startsAtUtc))
})

function formatAssocDate(iso: string): string {
    return formatTenantDateTime(iso, 'MMM D, YYYY · h:mm A')
}

async function loadAssociatedEvents() {
    if (!editing.value) {
        associatedEvents.value = []
        return
    }
    try {
        const r = await service.listAssociatedEvents(editing.value.id)
        associatedEvents.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load associated events.', 'error')
    }
}

async function loadAllEvents() {
    if (eventsLoaded.value) return
    // Pick a wide range — admins reorganizing waivers may need to attach to
    // historical events for retroactive cleanup as well as upcoming ones.
    const from = dayjs().subtract(2, 'year').utc().toISOString()
    const to = dayjs().add(2, 'year').utc().toISOString()
    try {
        const r = await eventService.list(from, to)
        allEvents.value = (r.data as any).data
        eventsLoaded.value = true
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load events.', 'error')
    }
}

watch(activeTab, async (tab) => {
    if (tab === 'events' && editing.value) {
        await Promise.all([loadAssociatedEvents(), loadAllEvents()])
    }
})

async function persistRole(row: WaiverEventAssociation, asRider: boolean, asSpectator: boolean) {
    if (!editing.value) return
    eventsBusy.value = true
    try {
        await service.setEventRole(editing.value.id, row.id, asRider, asSpectator)
        // Reload so the row drops out of the list when both roles are false,
        // and the picker refreshes too.
        await loadAssociatedEvents()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to update.', 'error')
        await loadAssociatedEvents()
    } finally {
        eventsBusy.value = false
    }
}

async function addEventAssociation() {
    if (!editing.value || !newEventId.value) return
    eventsBusy.value = true
    try {
        // Default new associations to rider-only — most common case. Admin can
        // tick spectator afterward if the same waiver covers both audiences.
        await service.setEventRole(editing.value.id, newEventId.value, true, false)
        newEventId.value = null
        await loadAssociatedEvents()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to add event.', 'error')
    } finally {
        eventsBusy.value = false
    }
}

async function removeEventAssociation(row: WaiverEventAssociation) {
    await persistRole(row, false, false)
}

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(load)

async function load() {
    loading.value = true
    try {
        const r = await service.listAdmin()
        rows.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load.', 'error')
    } finally {
        loading.value = false
    }
}

function resetDialogState() {
    activeTab.value = 'waiver'
    associatedEvents.value = []
    newEventId.value = null
}

function openCreate() {
    editing.value = null
    form.value = {
        name: '',
        title: 'Waiver & Release of Liability',
        body: '',
        isActive: true,
        expiresAtUtc: null,
    }
    resetDialogState()
    dialog.value = true
}

function openEdit(row: WaiverDto) {
    editing.value = row
    form.value = {
        name: row.name,
        title: row.title,
        body: row.body,
        isActive: row.isActive,
        expiresAtUtc: row.expiresAtUtc,
    }
    resetDialogState()
    dialog.value = true
}

// Duplicate: open the same dialog in create mode (editing=null) prefilled with
// the source row's fields. The admin can tweak any field — name, title, body,
// active/expiry — before Save, which creates a brand-new waiver row. Event
// associations live on the events themselves (event.racer_waiver_id /
// spectator_waiver_id), so the new waiver naturally starts with zero attached
// events; the Associated Events tab shows the "save first" empty state.
function openDuplicate(row: WaiverDto) {
    editing.value = null
    form.value = {
        name: `Copy of ${row.name}`,
        title: row.title,
        body: row.body,
        isActive: row.isActive,
        expiresAtUtc: row.expiresAtUtc,
    }
    resetDialogState()
    dialog.value = true
}

async function save() {
    if (!canSave.value) return
    saving.value = true
    try {
        const body = {
            name: form.value.name.trim(),
            title: form.value.title.trim(),
            body: form.value.body,
            isActive: form.value.isActive,
            expiresAtUtc: form.value.expiresAtUtc,
        }
        if (editing.value) await service.update(editing.value.id, body)
        else await service.create(body)
        await load()
        dialog.value = false
        flash('Waiver saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

function formatExpires(iso: string | null): string {
    if (!iso) return ''
    const d = new Date(iso)
    if (isNaN(d.getTime())) return ''
    return formatTenantDate(iso)
}
function isExpired(row: WaiverDto): boolean {
    if (!row.expiresAtUtc) return false
    const d = new Date(row.expiresAtUtc).getTime()
    return !isNaN(d) && d <= Date.now()
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
