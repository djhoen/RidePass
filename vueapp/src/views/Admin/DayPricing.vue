<template>
    <v-container style="max-width: 880px">
        <div class="rp-pagehead">
            <div>
                <div class="rp-pagehead-title">Peak days</div>
                <div class="rp-pagehead-sub">Days when a day pass costs more, online and at the gate. A rule for a date beats the
                    weekday rules, and the biggest increase wins: rules never add up. Race entries and free tickets are never
                    raised. Walk-up prices and Non-refundable are set on each ticket type in the event.</div>
            </div>
        </div>

        <v-card class="pa-4 mb-4">
            <div class="text-subtitle-1 mb-2">Add a peak day</div>
            <v-btn-toggle v-model="form.when" mandatory density="compact" variant="outlined" divided class="mb-3">
                <v-btn value="weekday">Every week</v-btn>
                <v-btn value="date">One date</v-btn>
            </v-btn-toggle>
            <v-row dense>
                <v-col cols="12" sm="4">
                    <v-select v-if="form.when === 'weekday'" v-model="form.weekday" :items="weekdayItems"
                        label="Day" density="compact" hide-details></v-select>
                    <v-text-field v-else v-model="form.onDate" type="date" label="Date" density="compact" hide-details></v-text-field>
                </v-col>
                <v-col cols="12" sm="4">
                    <v-text-field v-model="form.label" label="Name (e.g. Saturday, July 4th)" density="compact" hide-details></v-text-field>
                </v-col>
                <v-col cols="12" sm="4">
                    <v-select v-model="form.eventTypeId" :items="eventTypeItems" label="Event type" density="compact"
                        hide-details></v-select>
                </v-col>
                <v-col cols="12" sm="4">
                    <v-select v-model="form.adjustKind" :items="[{ title: 'Add dollars', value: 'amount' }, { title: 'Add percent', value: 'percent' }]"
                        label="Increase" density="compact" hide-details></v-select>
                </v-col>
                <v-col cols="12" sm="4">
                    <v-text-field v-model.number="form.amount" type="number" min="0" step="0.5" density="compact" hide-details
                        :prefix="form.adjustKind === 'amount' ? '$' : undefined" :suffix="form.adjustKind === 'percent' ? '%' : undefined"
                        :label="form.adjustKind === 'amount' ? 'Dollars more' : 'Percent more'"></v-text-field>
                </v-col>
                <v-col cols="12" sm="4" class="d-flex align-center">
                    <v-btn color="primary" :loading="saving" :disabled="!canAdd" block @click="add">Add</v-btn>
                </v-col>
            </v-row>
            <p class="text-caption text-medium-emphasis mt-2 mb-0">Example: a $40 day pass with "Saturday, add $10" costs $50 on
                Saturdays. With a walk-up price of $45 it costs $55 at the gate.</p>
        </v-card>

        <v-card class="pa-2">
            <div v-if="loading" class="text-center pa-6"><v-progress-circular indeterminate></v-progress-circular></div>
            <div v-else-if="rules.length === 0" class="text-medium-emphasis pa-4">No peak days yet: every day costs the same.</div>
            <v-table v-else density="comfortable">
                <thead><tr><th>When</th><th>Name</th><th>Event type</th><th>Increase</th><th>On</th><th></th></tr></thead>
                <tbody>
                    <tr v-for="r in rules" :key="r.id" :class="{ 'text-medium-emphasis': !r.isActive }">
                        <td>{{ r.onDate ? formatDate(r.onDate) : 'Every ' + weekdayName(r.weekday) }}</td>
                        <td>{{ r.label }}</td>
                        <td>{{ typeName(r.eventTypeId) }}</td>
                        <td>{{ r.adjustKind === 'amount' ? '+$' + (r.adjustValue / 100).toFixed(2) : '+' + (r.adjustValue / 100) + '%' }}</td>
                        <td><v-switch :model-value="r.isActive" density="compact" hide-details color="primary"
                                @update:model-value="(v: boolean | null) => toggle(r, !!v)"></v-switch></td>
                        <td class="text-right"><v-btn size="small" variant="text" color="error" @click="remove(r)">Remove</v-btn></td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { DayPricingService, type DayPriceRule } from '@/services/DayPricingService'
import { EventTypeService, type EventType } from '@/services/EventTypeService'
import { useConfirm } from '@/composables/useConfirm'

const svc = new DayPricingService()
const eventTypeService = new EventTypeService()
const confirm = useConfirm()

const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']
const weekdayItems = WEEKDAYS.map((title, value) => ({ title, value }))
const weekdayName = (d: number | null) => (d == null ? '' : WEEKDAYS[d])
const formatDate = (d: string) => dayjs(d).format('ddd, MMM D, YYYY')

const loading = ref(true)
const saving = ref(false)
const rules = ref<DayPriceRule[]>([])
const eventTypes = ref<EventType[]>([])
const eventTypeItems = computed(() => [{ title: 'Every event type', value: null as string | null },
    ...eventTypes.value.map(t => ({ title: t.name, value: t.id as string | null }))])
const typeName = (id: string | null) => id ? (eventTypes.value.find(t => t.id === id)?.name ?? 'Removed type') : 'Every event type'

const form = ref({ when: 'weekday' as 'weekday' | 'date', weekday: 6, onDate: '', label: 'Saturday',
    eventTypeId: null as string | null, adjustKind: 'amount' as 'amount' | 'percent', amount: 10 })
const canAdd = computed(() => form.value.label.trim().length > 0 && form.value.amount > 0
    && (form.value.when === 'weekday' || !!form.value.onDate))

async function load() {
    const [r, t] = await Promise.all([svc.list(), eventTypeService.list()])
    rules.value = r.data.data
    eventTypes.value = t.data.data
}

onMounted(async () => {
    try { await load() } catch (e: any) { flash(e?.response?.data?.error || 'Could not load peak days.', 'error') }
    finally { loading.value = false }
})

async function add() {
    saving.value = true
    try {
        await svc.create({
            label: form.value.label.trim(),
            weekday: form.value.when === 'weekday' ? form.value.weekday : null,
            onDate: form.value.when === 'date' ? form.value.onDate : null,
            eventTypeId: form.value.eventTypeId,
            adjustKind: form.value.adjustKind,
            // Dollars to cents, or percent to basis points: both are x100.
            adjustValue: Math.round(form.value.amount * 100),
        })
        await load()
        flash('Peak day added.', 'success')
    } catch (e: any) {
        flash(e?.response?.data?.error || 'Could not add the peak day.', 'error')
    } finally {
        saving.value = false
    }
}

async function toggle(r: DayPriceRule, isActive: boolean) {
    try {
        await svc.setActive(r.id, isActive)
        r.isActive = isActive
    } catch (e: any) {
        flash(e?.response?.data?.error || 'Could not change it.', 'error')
    }
}

async function remove(r: DayPriceRule) {
    const ok = await confirm({ title: `Remove "${r.label}"?`, message: 'Day passes go back to the normal price that day.', confirmText: 'Remove' })
    if (!ok) return
    try {
        await svc.remove(r.id)
        rules.value = rules.value.filter(x => x.id !== r.id)
    } catch (e: any) {
        flash(e?.response?.data?.error || 'Could not remove it.', 'error')
    }
}

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')
function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
