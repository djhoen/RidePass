<template>
    <v-container>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <h1 class="text-h4">Events</h1>
            <v-spacer></v-spacer>
            <v-text-field v-model="rangeFrom" type="date" label="From" density="compact" hide-details style="max-width: 180px"></v-text-field>
            <v-text-field v-model="rangeTo" type="date" label="To" density="compact" hide-details style="max-width: 180px"></v-text-field>
            <v-btn variant="text" @click="load">Refresh</v-btn>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">Add Event</v-btn>
        </div>

        <p class="text-caption text-medium-emphasis mb-2">
            Times displayed in tenant timezone: <strong>{{ branding.timezone }}</strong>. Input fields are interpreted in that zone too.
        </p>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th style="width: 180px">Start</th>
                        <th style="width: 180px">End</th>
                        <th>Title</th>
                        <th style="width: 160px">Type</th>
                        <th style="width: 90px">Capacity</th>
                        <th style="width: 120px">Reserved</th>
                        <th style="width: 110px">Status</th>
                        <th style="width: 220px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="row in rows" :key="row.id">
                        <td>{{ formatInTenant(row.startsAtUtc) }}</td>
                        <td>{{ formatInTenant(row.endsAtUtc) }}</td>
                        <td>
                            <div>{{ row.title }}</div>
                            <div v-if="row.locationLabel" class="text-caption text-medium-emphasis">{{ row.locationLabel }}</div>
                        </td>
                        <td>
                            <v-chip size="small" :style="{ backgroundColor: row.eventTypeColor, color: '#fff' }">
                                {{ row.eventTypeName }}
                            </v-chip>
                        </td>
                        <td>{{ row.capacity ?? '—' }}</td>
                        <td>
                            <template v-if="row.capacity">
                                <v-chip size="small" :color="reservedChipColor(row)" variant="tonal">
                                    {{ row.spotsReserved ?? 0 }} / {{ row.capacity }}
                                </v-chip>
                            </template>
                            <span v-else class="text-medium-emphasis">—</span>
                        </td>
                        <td>{{ row.status }}</td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" @click="openTiers(row)">Tickets</v-btn>
                            <v-btn variant="text" size="small" @click="openEdit(row)">Edit</v-btn>
                            <v-btn variant="text" size="small" @click="dup(row)">Duplicate</v-btn>
                            <v-btn variant="text" size="small" color="error" @click="remove(row)">Delete</v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && rows.length === 0">
                        <td colspan="8" class="text-center text-medium-emphasis py-8">No events in this range.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="dialog" max-width="640">
            <v-card>
                <v-card-title>{{ editing ? 'Edit Event' : 'Add Event' }}</v-card-title>
                <v-card-text>
                    <v-row>
                        <v-col cols="12" md="8">
                            <v-text-field v-model="form.title" label="Title" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="4">
                            <v-select v-model="form.eventTypeId" :items="typeOptions" item-title="name" item-value="id"
                                label="Type" density="compact"></v-select>
                        </v-col>
                    </v-row>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="form.startsLocal" type="datetime-local" label="Starts" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="form.endsLocal" type="datetime-local" label="Ends" density="compact"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-row>
                        <v-col cols="12" md="4">
                            <v-checkbox v-model="form.allDay" label="All day"></v-checkbox>
                        </v-col>
                        <v-col cols="12" md="4">
                            <v-text-field v-model.number="form.capacity" type="number" label="Capacity (blank = unlimited)" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="4">
                            <v-select v-model="form.status" :items="['scheduled','cancelled']" label="Status" density="compact"></v-select>
                        </v-col>
                    </v-row>
                    <v-text-field v-model="form.locationLabel" label="Location" density="compact"></v-text-field>
                    <v-textarea v-model="form.description" label="Description" rows="3" density="compact"></v-textarea>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="dialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="save">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <TicketTiersDialog v-model="tiersDialog" :event-id="tiersEventId" :event-title="tiersEventTitle" />

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import dayjs from 'dayjs'
import { EventService, type EventDto } from '@/services/EventService'
import { EventTypeService, type EventType } from '@/services/EventTypeService'
import { branding } from '@/stores/branding'
import TicketTiersDialog from '@/components/TicketTiersDialog.vue'

const eventService = new EventService()
const eventTypeService = new EventTypeService()

const today = dayjs()
const rangeFrom = ref(today.startOf('month').format('YYYY-MM-DD'))
const rangeTo = ref(today.endOf('month').add(1, 'day').format('YYYY-MM-DD'))

const rows = ref<EventDto[]>([])
const typeOptions = ref<EventType[]>([])
const loading = ref(false)
const dialog = ref(false)
const editing = ref<EventDto | null>(null)
const saving = ref(false)

const form = ref({
    eventTypeId: '',
    title: '',
    description: '' as string | null,
    startsLocal: '',
    endsLocal: '',
    allDay: false,
    capacity: null as number | null,
    locationLabel: '' as string | null,
    status: 'scheduled' as 'scheduled' | 'cancelled',
})

const tiersDialog = ref(false)
const tiersEventId = ref<string | null>(null)
const tiersEventTitle = ref<string>('')

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(async () => {
    const types = await eventTypeService.list()
    typeOptions.value = (types.data as any).data
    if (typeOptions.value.length > 0 && !form.value.eventTypeId) {
        form.value.eventTypeId = typeOptions.value[0].id
    }
    await load()
})

function tz(): string { return branding.timezone || 'UTC' }

function formatInTenant(utc: string): string {
    return dayjs.utc(utc).tz(tz()).format('YYYY-MM-DD HH:mm')
}

function localToUtc(localValue: string): string {
    // localValue is "YYYY-MM-DDTHH:mm"; treat as tenant-timezone and convert to UTC ISO
    return dayjs.tz(localValue, tz()).utc().toISOString()
}

function utcToLocalInput(utc: string): string {
    return dayjs.utc(utc).tz(tz()).format('YYYY-MM-DDTHH:mm')
}

async function load() {
    loading.value = true
    try {
        const fromUtc = dayjs.tz(rangeFrom.value + 'T00:00', tz()).utc().toISOString()
        const toUtc = dayjs.tz(rangeTo.value + 'T00:00', tz()).utc().toISOString()
        const r = await eventService.list(fromUtc, toUtc)
        rows.value = (r.data as any).data
    } finally {
        loading.value = false
    }
}

function openCreate() {
    editing.value = null
    const start = dayjs().tz(tz()).startOf('hour').add(1, 'hour')
    form.value = {
        eventTypeId: typeOptions.value[0]?.id ?? '',
        title: '',
        description: '',
        startsLocal: start.format('YYYY-MM-DDTHH:mm'),
        endsLocal: start.add(2, 'hour').format('YYYY-MM-DDTHH:mm'),
        allDay: false,
        capacity: null,
        locationLabel: '',
        status: 'scheduled',
    }
    dialog.value = true
}

function openEdit(row: EventDto) {
    editing.value = row
    form.value = {
        eventTypeId: row.eventTypeId,
        title: row.title,
        description: row.description ?? '',
        startsLocal: utcToLocalInput(row.startsAtUtc),
        endsLocal: utcToLocalInput(row.endsAtUtc),
        allDay: row.allDay,
        capacity: row.capacity,
        locationLabel: row.locationLabel ?? '',
        status: row.status,
    }
    dialog.value = true
}

async function save() {
    try {
        saving.value = true
        const body = {
            eventTypeId: form.value.eventTypeId,
            title: form.value.title.trim(),
            description: form.value.description && form.value.description.trim().length > 0 ? form.value.description : null,
            startsAtUtc: localToUtc(form.value.startsLocal),
            endsAtUtc: localToUtc(form.value.endsLocal),
            allDay: form.value.allDay,
            capacity: form.value.capacity || null,
            locationLabel: form.value.locationLabel && form.value.locationLabel.trim().length > 0 ? form.value.locationLabel : null,
            status: form.value.status,
        }
        if (editing.value) {
            await eventService.update(editing.value.id, body)
        } else {
            await eventService.create(body)
        }
        dialog.value = false
        await load()
        flash('Event saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

function openTiers(row: EventDto) {
    tiersEventId.value = row.id
    tiersEventTitle.value = row.title
    tiersDialog.value = true
}

async function dup(row: EventDto) {
    try {
        await eventService.duplicate(row.id)
        await load()
        flash('Event duplicated (+7 days).', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Duplicate failed.', 'error')
    }
}

async function remove(row: EventDto) {
    if (!confirm(`Delete "${row.title}"?`)) return
    try {
        await eventService.delete(row.id)
        await load()
        flash('Event deleted.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    }
}

function reservedChipColor(row: EventDto): string {
    if (!row.capacity) return 'default'
    const used = row.spotsReserved ?? 0
    if (used >= row.capacity) return 'error'
    if (used >= row.capacity * 0.8) return 'warning'
    return 'success'
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
