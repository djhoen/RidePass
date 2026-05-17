<template>
    <v-container>
        <div class="d-flex align-center mb-6">
            <h1 class="text-h4">Reward Programs</h1>
            <v-spacer></v-spacer>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">New Program</v-btn>
        </div>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th>Name</th>
                        <th style="width: 180px">Rule</th>
                        <th style="width: 110px">Reward</th>
                        <th style="width: 110px">Enrollment</th>
                        <th style="width: 130px">Proximity email</th>
                        <th style="width: 90px">Active</th>
                        <th style="width: 160px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="p in programs" :key="p.id">
                        <td>
                            <strong>{{ p.name }}</strong>
                            <div v-if="p.description" class="text-caption text-medium-emphasis">{{ p.description }}</div>
                        </td>
                        <td>Buy {{ p.requirementCount }} {{ kindLabel(p.requirementKind) }}{{ p.requirementCount === 1 ? '' : 's' }}</td>
                        <td>{{ p.rewardPercentOff === 100 ? 'Free' : `${p.rewardPercentOff}% off` }}</td>
                        <td>{{ p.enrollmentMode === 'auto' ? 'Automatic' : 'Opt-in' }}</td>
                        <td>
                            <span v-if="p.proximityEmailThreshold !== null">
                                {{ p.proximityEmailThreshold }} away
                            </span>
                            <span v-else class="text-medium-emphasis">—</span>
                        </td>
                        <td>
                            <v-icon v-if="p.isActive" color="success">mdi-check</v-icon>
                            <v-icon v-else color="grey">mdi-close</v-icon>
                        </td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" @click="openEdit(p)">Edit</v-btn>
                            <v-btn variant="text" size="small" color="error" @click="remove(p)">Delete</v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && programs.length === 0">
                        <td colspan="7" class="text-center text-medium-emphasis py-8">No reward programs yet.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="dialog" max-width="640" persistent>
            <v-card>
                <v-card-title>{{ editing ? 'Edit Program' : 'New Program' }}</v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.name" label="Name" density="compact"></v-text-field>
                    <v-textarea v-model="form.description" label="Description (optional)" rows="2" density="compact"></v-textarea>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-select v-model="form.requirementKind"
                                :items="[{ title: 'Any purchase', value: 'any' }, { title: 'Day passes only', value: 'pass' }, { title: 'Event tickets only', value: 'event_ticket' }]"
                                item-title="title" item-value="value" label="Counts toward reward" density="compact"></v-select>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model.number="form.requirementCount" type="number" min="1"
                                label="How many to earn one reward" density="compact"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model.number="form.rewardPercentOff" type="number" min="1" max="100"
                                label="Reward (% off next purchase)" suffix="%" density="compact"
                                hint="100% = free" persistent-hint></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-select v-model="form.enrollmentMode"
                                :items="[{ title: 'Automatic (every rider)', value: 'auto' }, { title: 'Opt-in', value: 'opt_in' }]"
                                item-title="title" item-value="value" label="Enrollment" density="compact"></v-select>
                        </v-col>
                    </v-row>
                    <v-text-field v-model.number="form.proximityEmailThreshold" type="number" min="1"
                        label="Email when rider is X away (blank to disable)" density="compact" clearable></v-text-field>
                    <v-switch v-model="form.isActive" label="Active" hide-details color="primary"></v-switch>
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
import { RewardService, type RewardProgram, type UpsertRewardProgram } from '@/services/RewardService'

const service = new RewardService()

const programs = ref<RewardProgram[]>([])
const loading = ref(false)
const dialog = ref(false)
const editing = ref<RewardProgram | null>(null)
const saving = ref(false)

const form = ref<UpsertRewardProgram>({
    name: '',
    description: '',
    enrollmentMode: 'auto',
    requirementKind: 'any',
    requirementCount: 5,
    rewardPercentOff: 100,
    proximityEmailThreshold: 1,
    isActive: true,
})

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

function kindLabel(k: string): string {
    if (k === 'pass') return 'pass'
    if (k === 'event_ticket') return 'event ticket'
    return 'purchase'
}

onMounted(load)

async function load() {
    loading.value = true
    try {
        const r = await service.listProgramsAdmin()
        programs.value = (r.data as any).data
    } finally {
        loading.value = false
    }
}

function openCreate() {
    editing.value = null
    form.value = { name: '', description: '', enrollmentMode: 'auto', requirementKind: 'any',
        requirementCount: 5, rewardPercentOff: 100, proximityEmailThreshold: 1, isActive: true }
    dialog.value = true
}

function openEdit(p: RewardProgram) {
    editing.value = p
    form.value = {
        name: p.name,
        description: p.description ?? '',
        enrollmentMode: p.enrollmentMode,
        requirementKind: p.requirementKind,
        requirementCount: p.requirementCount,
        rewardPercentOff: p.rewardPercentOff,
        proximityEmailThreshold: p.proximityEmailThreshold,
        isActive: p.isActive,
    }
    dialog.value = true
}

async function save() {
    try {
        saving.value = true
        const body: UpsertRewardProgram = {
            ...form.value,
            description: form.value.description && form.value.description.trim().length > 0 ? form.value.description : null,
            proximityEmailThreshold: form.value.proximityEmailThreshold && form.value.proximityEmailThreshold > 0 ? form.value.proximityEmailThreshold : null,
        }
        if (editing.value) {
            await service.updateProgram(editing.value.id, body)
        } else {
            await service.createProgram(body)
        }
        dialog.value = false
        await load()
        flash('Program saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function remove(p: RewardProgram) {
    if (!confirm(`Delete "${p.name}"? This removes all enrollments and unredeemed vouchers.`)) return
    try {
        await service.deleteProgram(p.id)
        await load()
        flash('Program deleted.', 'success')
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
