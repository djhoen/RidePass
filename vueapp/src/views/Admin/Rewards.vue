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
                        <td>
                            <template v-if="p.rewardKind === 'credit_rate'">{{ creditKindLabel(p.creditQualifyingKind) }}</template>
                            <template v-else>Buy {{ p.requirementCount }} {{ kindLabel(p.requirementKind) }}{{ p.requirementCount === 1 ? '' : 's' }}</template>
                        </td>
                        <td>
                            <template v-if="p.rewardKind === 'credit_rate'">{{ ((p.creditRateBps ?? 0) / 100) }}% back in credit</template>
                            <template v-else>{{ p.rewardPercentOff === 100 ? 'Free' : `${p.rewardPercentOff}% off` }}</template>
                        </td>
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
                    <tr v-if="!loading && !loadError && programs.length === 0">
                        <td colspan="7" class="text-center text-medium-emphasis py-8">No reward programs yet.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="dialog" max-width="640" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ editing ? 'Edit Program' : 'New Program' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="dialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.name" label="Name" density="compact"></v-text-field>
                    <v-textarea v-model="form.description" label="Description (optional)" rows="2" density="compact" class="mt-4"></v-textarea>
                    <v-select v-model="form.rewardKind" class="mt-4"
                        :items="[{ title: 'Voucher: % off after N purchases', value: 'percent_off' },
                                 { title: 'Store credit back on every purchase', value: 'credit_rate' }]"
                        item-title="title" item-value="value" label="Reward type" density="compact" hide-details></v-select>

                    <template v-if="form.rewardKind === 'percent_off'">
                        <v-row class="mt-1">
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
                            label="Email when rider is X away (blank to disable)" density="compact" clearable class="mt-4"></v-text-field>
                    </template>

                    <template v-else>
                        <v-row class="mt-1">
                            <v-col cols="12" md="6">
                                <v-text-field v-model.number="creditRatePercent" type="number" min="0.1" max="100" step="0.5"
                                    label="Earn rate (% of spend back)" suffix="%" density="compact"
                                    hint="e.g. 5 = $5 credit per $100 spent" persistent-hint></v-text-field>
                            </v-col>
                            <v-col cols="12" md="6">
                                <v-select v-model="form.creditQualifyingKind"
                                    :items="[{ title: 'Every purchase', value: 'any' }, { title: 'Event tickets & gate', value: 'event_ticket' },
                                             { title: 'Food & beverage', value: 'concession' }, { title: 'Bike shop', value: 'shop_sale' }]"
                                    item-title="title" item-value="value" label="Qualifying spend" density="compact"></v-select>
                            </v-col>
                        </v-row>
                        <v-select v-model="form.enrollmentMode" class="mt-1"
                            :items="[{ title: 'Automatic (every customer, walk-ins included)', value: 'auto' }, { title: 'Opt-in (signed-in riders who join)', value: 'opt_in' }]"
                            item-title="title" item-value="value" label="Enrollment" density="compact"></v-select>
                        <p class="text-caption text-medium-emphasis mt-1">
                            Credit lands on the customer's store credit account after each qualifying
                            purchase, based on the money they actually paid. They spend it at any
                            register or online checkout.
                        </p>
                    </template>

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
import { useConfirm } from '@/composables/useConfirm'

const service = new RewardService()
const confirm = useConfirm()

const programs = ref<RewardProgram[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
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
    rewardKind: 'percent_off',
    creditRateBps: null,
    creditQualifyingKind: 'any',
    proximityEmailThreshold: 1,
    isActive: true,
})
// The rate edits in percent (5 = 5%) and stores in basis points (500).
const creditRatePercent = ref<number | null>(null)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

function kindLabel(k: string): string {
    if (k === 'pass') return 'pass'
    if (k === 'event_ticket') return 'event ticket'
    return 'purchase'
}
function creditKindLabel(k: string | null | undefined): string {
    if (k === 'event_ticket') return 'Event & gate spend'
    if (k === 'concession') return 'Food & beverage spend'
    if (k === 'shop_sale') return 'Bike shop spend'
    return 'Every purchase'
}

onMounted(load)

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const r = await service.listProgramsAdmin()
        programs.value = (r.data as any).data
    } catch (err: any) {
        const msg = err.response?.data?.error ?? 'Couldn’t load reward programs. Refresh to try again.'
        loadError.value = msg
        flash(msg, 'error')
    } finally {
        loading.value = false
    }
}

function openCreate() {
    editing.value = null
    form.value = { name: '', description: '', enrollmentMode: 'auto', requirementKind: 'any',
        requirementCount: 5, rewardPercentOff: 100, rewardKind: 'percent_off',
        creditRateBps: null, creditQualifyingKind: 'any', proximityEmailThreshold: 1, isActive: true }
    creditRatePercent.value = 5
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
        rewardKind: p.rewardKind ?? 'percent_off',
        creditRateBps: p.creditRateBps ?? null,
        creditQualifyingKind: p.creditQualifyingKind ?? 'any',
        proximityEmailThreshold: p.proximityEmailThreshold,
        isActive: p.isActive,
    }
    creditRatePercent.value = p.creditRateBps != null ? p.creditRateBps / 100 : 5
    dialog.value = true
}

async function save() {
    try {
        saving.value = true
        if (form.value.rewardKind === 'credit_rate'
            && (creditRatePercent.value == null || isNaN(creditRatePercent.value) || creditRatePercent.value <= 0)) {
            flash('Enter the earn rate (percent of spend that comes back as credit).', 'error')
            return
        }
        const body: UpsertRewardProgram = {
            ...form.value,
            creditRateBps: form.value.rewardKind === 'credit_rate'
                ? Math.round((creditRatePercent.value ?? 0) * 100) : null,
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
    if (!await confirm({ message: `Delete "${p.name}"? This removes all enrollments and unredeemed vouchers.`, confirmText: 'Delete', confirmColor: 'error' })) return
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
