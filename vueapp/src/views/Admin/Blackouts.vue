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
                    <tr v-if="!loading && rows.length === 0">
                        <td colspan="5" class="text-center text-medium-emphasis py-8">No blackouts in this range.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="dialog" max-width="560">
            <v-card>
                <v-card-title>{{ editing ? 'Edit Blackout' : 'Add Blackout' }}</v-card-title>
                <v-card-text>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="form.startsLocal" type="datetime-local" label="Starts" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="form.endsLocal" type="datetime-local" label="Ends" density="compact"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-checkbox v-model="form.allDay" label="All day"></v-checkbox>
                    <v-textarea v-model="form.reason" label="Reason (optional)" rows="2" density="compact"></v-textarea>
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

const service = new BlackoutService()

const today = dayjs()
const rangeFrom = ref(today.startOf('month').format('YYYY-MM-DD'))
const rangeTo = ref(today.endOf('month').add(1, 'day').format('YYYY-MM-DD'))

const rows = ref<BlackoutDto[]>([])
const loading = ref(false)
const dialog = ref(false)
const editing = ref<BlackoutDto | null>(null)
const saving = ref(false)

const form = ref({
    startsLocal: '',
    endsLocal: '',
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
    try {
        const fromUtc = dayjs.tz(rangeFrom.value + 'T00:00', tz()).utc().toISOString()
        const toUtc = dayjs.tz(rangeTo.value + 'T00:00', tz()).utc().toISOString()
        const r = await service.list(fromUtc, toUtc)
        rows.value = (r.data as any).data
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
        allDay: true,
        reason: '',
    }
    dialog.value = true
}

function openEdit(row: BlackoutDto) {
    editing.value = row
    form.value = {
        startsLocal: utcToLocalInput(row.startsAtUtc),
        endsLocal: utcToLocalInput(row.endsAtUtc),
        allDay: row.allDay,
        reason: row.reason ?? '',
    }
    dialog.value = true
}

async function save() {
    try {
        saving.value = true
        const body = {
            startsAtUtc: localToUtc(form.value.startsLocal),
            endsAtUtc: localToUtc(form.value.endsLocal),
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
    if (!confirm('Delete this blackout?')) return
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
