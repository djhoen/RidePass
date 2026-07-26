<template>
    <v-container style="max-width: 900px">
        <div class="d-flex align-center flex-wrap ga-2 mb-1">
            <h1 class="text-h4">Discounts</h1>
            <v-spacer></v-spacer>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openNew">New discount</v-btn>
        </div>
        <p class="text-body-2 text-medium-emphasis mb-4">
            Discounts your staff can apply at a counter, like a military or club-member rate.
            Choose where each one applies and whether taking it needs a manager's PIN.
        </p>

        <v-alert v-if="loadError" type="error" variant="tonal" density="compact" class="mb-3">
            {{ loadError }}
        </v-alert>

        <v-card>
            <v-table density="compact">
                <thead>
                    <tr>
                        <th>Name</th>
                        <th style="width: 120px">Amount</th>
                        <th>Applies to</th>
                        <th style="width: 110px">Manager</th>
                        <th style="width: 90px">Status</th>
                        <th style="width: 96px"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="d in discounts" :key="d.id" :class="{ 'text-medium-emphasis': !d.isActive }">
                        <td>{{ d.name }}</td>
                        <td class="text-no-wrap">{{ d.label }}</td>
                        <td>
                            <div class="d-flex flex-wrap ga-1 py-1">
                                <v-chip v-for="s in d.surfaces" :key="s" size="x-small" variant="tonal">
                                    {{ surfaceTitle(s) }}
                                </v-chip>
                            </div>
                        </td>
                        <td>
                            <v-chip v-if="d.requiresManager" size="x-small" color="warning" variant="tonal"
                                prepend-icon="mdi-shield-key">PIN</v-chip>
                            <span v-else class="text-medium-emphasis text-caption">Any staff</span>
                        </td>
                        <td>
                            <v-chip size="x-small" :color="d.isActive ? 'success' : undefined" variant="tonal">
                                {{ d.isActive ? 'Active' : 'Off' }}
                            </v-chip>
                        </td>
                        <td class="text-no-wrap">
                            <v-btn icon="mdi-pencil" size="x-small" variant="text" @click="openEdit(d)"></v-btn>
                            <v-btn icon="mdi-delete" size="x-small" variant="text" @click="confirmRemove(d)"></v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && discounts.length === 0">
                        <td colspan="6" class="text-center text-medium-emphasis py-8">
                            No discounts yet. Add one and it appears at whichever counters you choose.
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="dialog" max-width="560">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ editingId ? 'Edit discount' : 'New discount' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="dialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.name" label="Name" density="compact" variant="outlined"
                        placeholder="Military" hide-details></v-text-field>

                    <div class="d-flex ga-3 mt-4">
                        <v-select v-model="form.kind" :items="kindItems" label="Type" density="compact"
                            variant="outlined" hide-details style="max-width: 180px"></v-select>
                        <v-text-field v-model.number="form.amount" type="number" min="0"
                            :step="form.kind === 'percent' ? 0.5 : 0.01"
                            :label="form.kind === 'percent' ? 'Percent off' : 'Dollars off'"
                            :prefix="form.kind === 'amount' ? '$' : undefined"
                            :suffix="form.kind === 'percent' ? '%' : undefined"
                            density="compact" variant="outlined" hide-details></v-text-field>
                    </div>

                    <div class="text-body-2 font-weight-medium mt-4 mb-1">Applies to</div>
                    <div class="text-caption text-medium-emphasis mb-2">
                        The counters that may apply it. Pick at least one.
                    </div>
                    <div class="d-flex flex-wrap ga-2">
                        <v-chip v-for="s in surfaceOptions" :key="s.value"
                            :color="form.surfaces.includes(s.value) ? 'primary' : undefined"
                            :variant="form.surfaces.includes(s.value) ? 'flat' : 'outlined'"
                            size="small" @click="toggleSurface(s.value)">
                            {{ s.title }}
                        </v-chip>
                    </div>

                    <v-switch v-model="form.requiresManager" color="primary" hide-details inset class="mt-4"
                        :label="form.requiresManager
                            ? 'A manager PIN is needed to apply this'
                            : 'Any staff member can apply this'"></v-switch>
                    <v-switch v-model="form.isActive" color="primary" hide-details inset
                        :label="form.isActive ? 'Active' : 'Off (hidden at every counter)'"></v-switch>

                    <div v-if="formError" class="text-error text-body-2 mt-3">{{ formError }}</div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="saving" @click="dialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="save">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { DiscountService, DISCOUNT_SURFACES, type DiscountPreset, type DiscountSurface } from '@/services/DiscountService'
import { useConfirm } from '@/composables/useConfirm'

const service = new DiscountService()
const confirm = useConfirm()

const discounts = ref<DiscountPreset[]>([])
const loading = ref(false)
const loadError = ref('')
const surfaceOptions = DISCOUNT_SURFACES
const kindItems = [
    { title: 'Percent off', value: 'percent' },
    { title: 'Dollars off', value: 'amount' },
]

const dialog = ref(false)
const editingId = ref<string | null>(null)
const saving = ref(false)
const formError = ref('')
// `amount` is the human unit (percent or dollars); it converts to bps/cents on save, matching
// how the value is stored everywhere else in the app.
const form = ref({
    name: '', kind: 'percent' as 'percent' | 'amount', amount: 10,
    surfaces: [] as DiscountSurface[], requiresManager: false, isActive: true,
})

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')
function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

function surfaceTitle(s: string): string {
    return DISCOUNT_SURFACES.find(x => x.value === s)?.title ?? s
}

function toggleSurface(s: DiscountSurface) {
    const i = form.value.surfaces.indexOf(s)
    if (i >= 0) form.value.surfaces.splice(i, 1)
    else form.value.surfaces.push(s)
}

function openNew() {
    editingId.value = null
    formError.value = ''
    form.value = { name: '', kind: 'percent', amount: 10, surfaces: [], requiresManager: false, isActive: true }
    dialog.value = true
}

function openEdit(d: DiscountPreset) {
    editingId.value = d.id
    formError.value = ''
    form.value = {
        name: d.name,
        kind: d.kind,
        amount: d.kind === 'percent' ? d.value / 100 : d.value / 100,
        surfaces: [...d.surfaces],
        requiresManager: d.requiresManager,
        isActive: d.isActive,
    }
    dialog.value = true
}

async function save() {
    formError.value = ''
    const name = form.value.name.trim()
    if (!name) { formError.value = 'Give the discount a name staff will recognise.'; return }
    if (form.value.surfaces.length === 0) {
        formError.value = 'Choose at least one place this discount applies.'
        return
    }
    const amount = Number(form.value.amount)
    if (!isFinite(amount) || amount <= 0) { formError.value = 'Enter an amount greater than zero.'; return }
    if (form.value.kind === 'percent' && amount > 100) { formError.value = "A percentage can't exceed 100%."; return }

    // percent -> basis points, dollars -> cents. Both are x100, but for different reasons.
    const value = Math.round(amount * 100)

    saving.value = true
    try {
        const payload = {
            name, kind: form.value.kind, value,
            surfaces: form.value.surfaces,
            requiresManager: form.value.requiresManager,
            isActive: form.value.isActive,
            sortOrder: 0,
        }
        if (editingId.value) await service.update(editingId.value, payload)
        else await service.create(payload)
        dialog.value = false
        flash('Discount saved.', 'success')
        await load()
    } catch (err: any) {
        formError.value = err.response?.data?.error || "Couldn't save the discount. Check the values and try again."
    } finally {
        saving.value = false
    }
}

async function confirmRemove(d: DiscountPreset) {
    const ok = await confirm({
        title: 'Delete discount?',
        message: `"${d.name}" will stop appearing at every counter. Sales that already used it keep their record.`,
        confirmText: 'Delete',
        confirmColor: 'error',
    })
    if (!ok) return
    try {
        await service.remove(d.id)
        flash('Discount deleted.', 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || "Couldn't delete that discount. Try again.", 'error')
    }
}

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        const r = await service.list()
        discounts.value = r.data.data
    } catch (err: any) {
        const msg = err.response?.data?.error || "Couldn't load discounts. Check your connection and try again."
        loadError.value = msg
        discounts.value = []
    } finally {
        loading.value = false
    }
}

onMounted(load)
</script>
