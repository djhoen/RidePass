<template>
    <v-container>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <h1 class="text-h4">Blackouts</h1>
            <v-spacer></v-spacer>
            <v-text-field v-model="rangeFrom" type="date" label="From" density="compact" hide-details style="max-width: 180px"></v-text-field>
            <v-text-field v-model="rangeTo" type="date" label="To" density="compact" hide-details style="max-width: 180px"></v-text-field>
            <v-btn variant="text" @click="load">Refresh</v-btn>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">Add Blackout</v-btn>
        </div>

        <p class="text-caption text-medium-emphasis mb-2">
            Times in tenant timezone: <strong>{{ branding.timezone }}</strong>.
        </p>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th style="width: 180px">Start</th>
                        <th style="width: 180px">End</th>
                        <th>Reason</th>
                        <th style="width: 90px">All day</th>
                        <th style="width: 180px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="row in rows" :key="row.id">
                        <td>{{ formatInTenant(row.startsAtUtc) }}</td>
                        <td>{{ formatInTenant(row.endsAtUtc) }}</td>
                        <td>{{ row.reason || '—' }}</td>
                        <td>
                            <v-icon v-if="row.allDay" color="warning">mdi-check</v-icon>
                            <span v-else>—</span>
                        </td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" @click="openEdit(row)">Edit</v-btn>
                            <v-btn variant="text" size="small" color="error" @click="remove(row)">Delete</v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && !loadError && rows.length === 0">
                        <td colspan="5" class="text-center text-medium-emphasis py-8">No blackouts in this range.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="dialog" max-width="560">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ editing ? 'Edit Blackout' : 'Add Blackout' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="dialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-checkbox v-model="form.allDay" label="All day"></v-checkbox>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-if="form.allDay" v-model="form.startDate" type="date"
                                label="From (date)" density="compact"></v-text-field>
                            <v-text-field v-else v-model="form.startsLocal" type="datetime-local"
                                label="Starts" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-if="form.allDay" v-model="form.endDate" type="date"
                                label="To (date, inclusive)" density="compact"
                                hint="The blackout covers both dates entirely."></v-text-field>
                            <v-text-field v-else v-model="form.endsLocal" type="datetime-local"
                                label="Ends" density="compact"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-textarea v-model="form.reason" label="Reason (optional)" rows="2" density="compact" class="mt-4"></v-textarea>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="dialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="save">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import dayjs from 'dayjs'
import { BlackoutService, type BlackoutDto } from '@/services/BlackoutService'
import { branding } from '@/stores/branding'
import { useConfirm } from '@/composables/useConfirm'

const service = new BlackoutService()
const confirm = useConfirm()

const today = dayjs()
const rangeFrom = ref(today.startOf('month').format('YYYY-MM-DD'))
const rangeTo = ref(today.endOf('month').add(1, 'day').format('YYYY-MM-DD'))

const rows = ref<BlackoutDto[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
const dialog = ref(false)
const editing = ref<BlackoutDto | null>(null)
const saving = ref(false)

const form = ref({
    startsLocal: '',
    endsLocal: '',
    startDate: '',
    endDate: '',
    allDay: false,
    reason: '' as string | null,
})

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(load)

function tz(): string { return branding.timezone || 'UTC' }

function formatInTenant(utc: string): string {
    return dayjs.utc(utc).tz(tz()).format('YYYY-MM-DD HH:mm')
}

function localToUtc(localValue: string): string {
    return dayjs.tz(localValue, tz()).utc().toISOString()
}

function utcToLocalInput(utc: string): string {
    return dayjs.utc(utc).tz(tz()).format('YYYY-MM-DDTHH:mm')
}

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const fromUtc = dayjs.tz(rangeFrom.value + 'T00:00', tz()).utc().toISOString()
        const toUtc = dayjs.tz(rangeTo.value + 'T00:00', tz()).utc().toISOString()
        const r = await service.list(fromUtc, toUtc)
        rows.value = (r.data as any).data
    } catch (err: any) {
        const msg = err.response?.data?.error ?? 'Couldn’t load blackouts. Refresh to try again.'
        loadError.value = msg
        flash(msg, 'error')
    } finally {
        loading.value = false
    }
}

function openCreate() {
    editing.value = null
    const start = dayjs().tz(tz()).startOf('day')
    form.value = {
        startsLocal: start.format('YYYY-MM-DDTHH:mm'),
        endsLocal: start.add(1, 'day').format('YYYY-MM-DDTHH:mm'),
        startDate: start.format('YYYY-MM-DD'),
        endDate: start.format('YYYY-MM-DD'),
        allDay: true,
        reason: '',
    }
    dialog.value = true
}

function openEdit(row: BlackoutDto) {
    editing.value = row
    const startsTz = dayjs.utc(row.startsAtUtc).tz(tz())
    // For all-day rows the end_at is exclusive midnight of the day-after, so subtract a
    // day to recover the inclusive last covered date for the UI.
    const endsExclusiveTz = dayjs.utc(row.endsAtUtc).tz(tz())
    const endDateInclusive = row.allDay
        ? endsExclusiveTz.subtract(1, 'day').format('YYYY-MM-DD')
        : endsExclusiveTz.format('YYYY-MM-DD')
    form.value = {
        startsLocal: utcToLocalInput(row.startsAtUtc),
        endsLocal: utcToLocalInput(row.endsAtUtc),
        startDate: startsTz.format('YYYY-MM-DD'),
        endDate: endDateInclusive,
        allDay: row.allDay,
        reason: row.reason ?? '',
    }
    dialog.value = true
}

async function save() {
    try {
        saving.value = true
        let startsAtUtc: string
        let endsAtUtc: string
        if (form.value.allDay) {
            // Snap to local midnight start; end is the *exclusive* midnight of the day
            // after the last covered date, so the row cleanly covers full calendar days.
            const startDate = form.value.startDate
            const endDate = form.value.endDate || startDate
            if (!startDate || !endDate) {
                flash('Pick a start and end date.', 'error')
                return
            }
            if (endDate < startDate) {
                flash('End date must be on or after the start date.', 'error')
                return
            }
            startsAtUtc = dayjs.tz(startDate + 'T00:00', tz()).utc().toISOString()
            endsAtUtc = dayjs.tz(endDate + 'T00:00', tz()).add(1, 'day').utc().toISOString()
        } else {
            startsAtUtc = localToUtc(form.value.startsLocal)
            endsAtUtc = localToUtc(form.value.endsLocal)
        }
        const body = {
            startsAtUtc,
            endsAtUtc,
            allDay: form.value.allDay,
            reason: form.value.reason && form.value.reason.trim().length > 0 ? form.value.reason : null,
        }
        if (editing.value) {
            await service.update(editing.value.id, body)
        } else {
            await service.create(body)
        }
        dialog.value = false
        await load()
        flash('Blackout saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function remove(row: BlackoutDto) {
    if (!await confirm({ message: `Delete this blackout?`, confirmText: 'Delete', confirmColor: 'error' })) return
    try {
        await service.delete(row.id)
        await load()
        flash('Blackout deleted.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
