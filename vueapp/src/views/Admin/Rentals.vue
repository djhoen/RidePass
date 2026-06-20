<template>
    <v-container>
        <div class="d-flex align-center mb-4 flex-wrap ga-3">
            <h1 class="text-h4">Rentals</h1>
            <v-spacer></v-spacer>
            <v-btn variant="text" @click="load">Refresh</v-btn>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">Add Rental</v-btn>
        </div>

        <v-alert v-if="!branding.rentalsEnabled" type="info" variant="tonal" class="mb-4">
            Rentals are turned off for this tenant. Enable them on
            <router-link to="/Admin/Settings/Features">Settings → Features</router-link> before riders can book.
        </v-alert>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th style="width: 36px"></th>
                        <th>Name</th>
                        <th style="width: 110px">Daily</th>
                        <th style="width: 110px">Deposit</th>
                        <th style="width: 130px">Tracking</th>
                        <th style="width: 130px">Inventory</th>
                        <th style="width: 90px">Active</th>
                        <th style="width: 90px" class="text-right"></th>
                    </tr>
                </thead>
                <draggable tag="tbody" :list="visibleRows" item-key="id" handle=".drag-handle"
                    :animation="180" ghost-class="drag-ghost" @end="onReorderEnd">
                    <template #item="{ element: row }">
                        <tr>
                            <td class="drag-handle-cell">
                                <v-icon class="drag-handle" color="grey">mdi-drag-vertical</v-icon>
                            </td>
                            <td>
                                <strong>{{ row.name }}</strong>
                                <div v-if="row.description" class="text-caption text-medium-emphasis">{{ row.description }}</div>
                            </td>
                            <td>${{ (row.dailyRateCents / 100).toFixed(2) }}</td>
                            <td>${{ (row.depositCents / 100).toFixed(2) }}</td>
                            <td>
                                <v-chip size="small" :color="row.trackingKind === 'per_item' ? 'deep-purple' : 'primary'">
                                    {{ row.trackingKind === 'per_item' ? 'Per item' : 'Pool' }}
                                </v-chip>
                            </td>
                            <td>
                                <template v-if="row.trackingKind === 'pool'">{{ row.inventoryPool }}</template>
                                <template v-else>
                                    <span>{{ row.perItemAvailable ?? 0 }} / {{ row.perItemTotal ?? 0 }}</span>
                                    <div class="text-caption text-medium-emphasis">available / total</div>
                                </template>
                            </td>
                            <td>
                                <v-icon v-if="row.isActive" color="success">mdi-check</v-icon>
                                <v-icon v-else color="grey">mdi-close</v-icon>
                            </td>
                            <td class="text-right">
                                <v-btn variant="text" size="small" @click="openEdit(row)">Edit</v-btn>
                            </td>
                        </tr>
                    </template>
                </draggable>
                <tbody v-if="!loading && rows.length === 0">
                    <tr>
                        <td colspan="8" class="text-center text-medium-emphasis py-8">
                            No rentals yet. Click "Add Rental" to create one.
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="dialog" max-width="800">
            <v-card class="d-flex flex-column" style="max-height: 90vh">
                <v-card-title class="d-flex align-center">
                    <span>{{ editing ? 'Edit Rental' : 'Add Rental' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="dialog = false"></v-btn>
                </v-card-title>
                <v-tabs v-model="activeTab" color="primary" grow style="flex: 0 0 auto">
                    <v-tab value="info">Details</v-tab>
                    <v-tab value="units" :disabled="!editing || form.trackingKind !== 'per_item'">
                        Per-item Units
                        <v-tooltip v-if="!editing" location="bottom" activator="parent">
                            Save the rental first.
                        </v-tooltip>
                        <v-tooltip v-else-if="form.trackingKind !== 'per_item'" location="bottom" activator="parent">
                            Pool inventory uses a single count, no per-item rows.
                        </v-tooltip>
                    </v-tab>
                </v-tabs>

                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                    <v-window v-model="activeTab" class="mt-4">
                        <v-window-item value="info">
                            <v-row class="mt-2">
                                <v-col cols="12" md="8">
                                    <v-text-field v-model="form.name" label="Name" density="compact"></v-text-field>
                                </v-col>
                                <v-col cols="12" md="4">
                                    <v-text-field v-model.number="form.sortOrder" type="number"
                                        label="Sort order" density="compact"></v-text-field>
                                </v-col>
                            </v-row>
                            <v-textarea v-model="form.description" label="Description (optional)" rows="2" class="mt-6"
                                density="compact"></v-textarea>
                            <v-row class="mt-2">
                                <v-col cols="12" md="4">
                                    <v-text-field v-model.number="dailyRateDollars" type="number" min="0" step="0.01"
                                        label="Daily rate ($)" density="compact"></v-text-field>
                                </v-col>
                                <v-col cols="12" md="4">
                                    <v-text-field v-model.number="depositDollars" type="number" min="0" step="0.01"
                                        label="Deposit ($)" density="compact"
                                        hint="Pre-authorized; captured only on damage." persistent-hint></v-text-field>
                                </v-col>
                                <v-col cols="12" md="4">
                                    <v-text-field v-model.number="serviceChargePercent" type="number" min="0" max="100"
                                        label="Rider-paid service-charge %" density="compact"></v-text-field>
                                </v-col>
                            </v-row>
                            <v-row>
                                <v-col cols="12" md="6">
                                    <v-select v-model="form.trackingKind"
                                        :items="trackingOptions" item-title="label" item-value="value"
                                        label="Tracking" density="compact"
                                        :disabled="!!editing"
                                        :hint="trackingHint"
                                        persistent-hint></v-select>
                                </v-col>
                                <v-col v-if="form.trackingKind === 'pool'" cols="12" md="6">
                                    <v-text-field v-model.number="form.inventoryPool" type="number" min="1" max="1000"
                                        label="Inventory (units in pool)" density="compact"></v-text-field>
                                </v-col>
                            </v-row>
                            <v-row>
                                <v-col cols="12" md="6">
                                    <v-switch v-model="form.requiresWaiver" color="primary" density="compact"
                                        label="Requires waiver" hide-details></v-switch>
                                </v-col>
                                <v-col cols="12" md="6">
                                    <v-switch v-model="form.isActive" color="primary" density="compact"
                                        label="Active (visible to riders)" hide-details></v-switch>
                                </v-col>
                            </v-row>
                        </v-window-item>

                        <v-window-item value="units">
                            <p class="text-caption text-medium-emphasis mb-2">
                                Each unit gets a label (e.g. "Bike A") plus optional serial. Set status to
                                "maintenance" to take it out of the pool temporarily, or "retired" to keep
                                history but stop bookings forever.
                            </p>
                            <v-table density="compact">
                                <thead>
                                    <tr>
                                        <th>Label</th>
                                        <th>Serial</th>
                                        <th style="width: 130px">Status</th>
                                        <th style="width: 100px" class="text-right"></th>
                                    </tr>
                                </thead>
                                <tbody>
                                    <tr v-for="u in units" :key="u.id">
                                        <td>{{ u.label }}</td>
                                        <td>{{ u.serial || '—' }}</td>
                                        <td>
                                            <v-chip size="x-small" :color="statusColor(u.status)">{{ u.status }}</v-chip>
                                        </td>
                                        <td class="text-right">
                                            <v-btn variant="text" size="small" @click="openEditUnit(u)">Edit</v-btn>
                                        </td>
                                    </tr>
                                    <tr v-if="units.length === 0">
                                        <td colspan="4" class="text-medium-emphasis text-center py-4">
                                            No units yet — add one below.
                                        </td>
                                    </tr>
                                </tbody>
                            </v-table>
                            <v-btn class="mt-3" prepend-icon="mdi-plus" variant="tonal" @click="openCreateUnit">
                                Add Unit
                            </v-btn>
                        </v-window-item>
                    </v-window>
                </v-card-text>
                <v-card-actions>
                    <v-btn v-if="editing" variant="text" color="error" @click="remove">Delete</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn @click="dialog = false">Close</v-btn>
                    <v-btn v-if="activeTab === 'info'" color="primary" :loading="saving" @click="save">
                        {{ editing ? 'Save' : 'Save & Continue' }}
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="unitDialog" max-width="560">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ editingUnit ? 'Edit Unit' : 'Add Unit' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="unitDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="unitForm.label" label="Label (e.g. Bike A)" density="compact"></v-text-field>
                    <v-text-field v-model="unitForm.serial" label="Serial (optional)" density="compact" class="mt-6"></v-text-field>
                    <v-textarea v-model="unitForm.notes" label="Notes (optional)" rows="2" density="compact" class="mt-6"></v-textarea>
                    <v-select v-model="unitForm.status" class="mt-6"
                        :items="['available','maintenance','retired']"
                        label="Status" density="compact"
                        hint="Use 'maintenance' for an indefinite hold; for a specific date window add a Maintenance entry below."
                        persistent-hint></v-select>

                    <template v-if="editingUnit">
                        <v-divider class="my-4"></v-divider>
                        <div class="d-flex align-center mb-2">
                            <strong>Scheduled maintenance</strong>
                            <v-spacer></v-spacer>
                            <v-btn size="small" variant="tonal" prepend-icon="mdi-plus"
                                @click="openMaintenanceDialog(null)">
                                Schedule
                            </v-btn>
                        </div>
                        <div v-if="maintenanceWindows.length === 0" class="text-caption text-medium-emphasis">
                            No maintenance windows. Bookings can be made for any date in this unit's range.
                        </div>
                        <v-table v-else density="compact">
                            <tbody>
                                <tr v-for="m in maintenanceWindows" :key="m.id">
                                    <td>{{ formatDate(m.startsAtDate) }} → {{ formatDate(m.endsAtDate) }}</td>
                                    <td>
                                        <span v-if="m.reason">{{ m.reason }}</span>
                                        <span v-else class="text-medium-emphasis">—</span>
                                    </td>
                                    <td class="text-right" style="width: 110px">
                                        <v-btn size="x-small" variant="text" @click="openMaintenanceDialog(m)">Edit</v-btn>
                                    </td>
                                </tr>
                            </tbody>
                        </v-table>
                    </template>
                </v-card-text>
                <v-card-actions>
                    <v-btn v-if="editingUnit" variant="text" color="error" @click="removeUnit">Delete</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn @click="unitDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="savingUnit" @click="saveUnit">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="maintenanceDialog" max-width="500">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ editingMaintenance ? 'Edit maintenance window' : 'Schedule maintenance' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="maintenanceDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="maintenanceForm.startsAtDate" type="date"
                                label="Starts" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="maintenanceForm.endsAtDate" type="date"
                                label="Ends" density="compact" :min="maintenanceForm.startsAtDate"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-text-field v-model="maintenanceForm.reason" label="Reason (optional)"
                        placeholder="Tire change, top-end rebuild, etc." density="compact" class="mt-6"></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-btn v-if="editingMaintenance" variant="text" color="error"
                        @click="deleteMaintenanceWindow">Delete</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn @click="maintenanceDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="savingMaintenance" @click="saveMaintenance">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import draggable from 'vuedraggable'
import dayjs from 'dayjs'
import { useDragReorder } from '@/composables/useDragReorder'
import { RentalService, type RentalProduct, type RentalItem, type MaintenanceWindow } from '@/services/RentalService'
import { branding } from '@/stores/branding'
import { useConfirm } from '@/composables/useConfirm'

const service = new RentalService()
const confirm = useConfirm()

const rows = ref<RentalProduct[]>([])
const loading = ref(false)
const { visibleRows, onReorderEnd } = useDragReorder<RentalProduct>({
    rows,
    save: items => service.reorderProducts(items),
    onSuccess: () => flash('Order saved.', 'success'),
    onError: async err => {
        flash((err as any)?.response?.data?.error || 'Failed to save order — refreshing.', 'error')
        await load()
    },
})

const dialog = ref(false)
const editing = ref<RentalProduct | null>(null)
const saving = ref(false)
const activeTab = ref<'info' | 'units'>('info')

const form = ref({
    name: '',
    description: '' as string | null,
    imageUrl: null as string | null,
    dailyRateCents: 0,
    depositCents: 0,
    trackingKind: 'pool' as 'pool' | 'per_item',
    inventoryPool: 1 as number | null,
    requiresWaiver: true,
    riderPaidServiceChargeBps: 10000,
    isActive: true,
    sortOrder: 100,
})

const trackingOptions = [
    { value: 'pool',     label: 'Pool — count of identical units' },
    { value: 'per_item', label: 'Per item — distinct units with serial' },
]

const trackingHint = computed(() => editing.value
    ? "Tracking kind can't change after a rental is created."
    : 'Pool: one count of identical units. Per-item: distinct units with serial / condition.')

const dailyRateDollars = computed({
    get: () => form.value.dailyRateCents / 100,
    set: (v: number) => { form.value.dailyRateCents = Math.round((v || 0) * 100) },
})
const depositDollars = computed({
    get: () => form.value.depositCents / 100,
    set: (v: number) => { form.value.depositCents = Math.round((v || 0) * 100) },
})
const serviceChargePercent = computed({
    get: () => Math.round((form.value.riderPaidServiceChargeBps / 100)),
    set: (v: number) => { form.value.riderPaidServiceChargeBps = Math.max(0, Math.min(10000, Math.round((v || 0) * 100))) },
})

// Per-item units (loaded when editing a per_item product).
const units = ref<RentalItem[]>([])
const unitDialog = ref(false)
const editingUnit = ref<RentalItem | null>(null)
const savingUnit = ref(false)
const unitForm = ref({
    label: '',
    serial: '' as string | null,
    notes: '' as string | null,
    status: 'available' as 'available' | 'maintenance' | 'retired',
})

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(load)

async function load() {
    loading.value = true
    try {
        const r = await service.listForAdmin()
        rows.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load rentals.', 'error')
    } finally {
        loading.value = false
    }
}

function openCreate() {
    editing.value = null
    activeTab.value = 'info'
    form.value = {
        name: '',
        description: '',
        imageUrl: null,
        dailyRateCents: 2000,
        depositCents: 0,
        trackingKind: 'pool',
        inventoryPool: 1,
        requiresWaiver: true,
        riderPaidServiceChargeBps: 10000,
        isActive: true,
        sortOrder: 100,
    }
    units.value = []
    dialog.value = true
}

async function openEdit(row: RentalProduct) {
    editing.value = row
    activeTab.value = 'info'
    form.value = {
        name: row.name,
        description: row.description ?? '',
        imageUrl: row.imageUrl,
        dailyRateCents: row.dailyRateCents,
        depositCents: row.depositCents,
        trackingKind: row.trackingKind,
        inventoryPool: row.inventoryPool,
        requiresWaiver: row.requiresWaiver,
        riderPaidServiceChargeBps: row.riderPaidServiceChargeBps,
        isActive: row.isActive,
        sortOrder: row.sortOrder,
    }
    if (row.trackingKind === 'per_item') {
        await loadUnits(row.id)
    } else {
        units.value = []
    }
    dialog.value = true
}

async function save() {
    try {
        saving.value = true
        const body = {
            name: form.value.name.trim(),
            description: form.value.description?.trim() || null,
            imageUrl: form.value.imageUrl?.trim() || null,
            dailyRateCents: form.value.dailyRateCents,
            depositCents: form.value.depositCents,
            trackingKind: form.value.trackingKind,
            inventoryPool: form.value.trackingKind === 'pool' ? (form.value.inventoryPool || 1) : null,
            requiresWaiver: form.value.requiresWaiver,
            riderPaidServiceChargeBps: form.value.riderPaidServiceChargeBps,
            isActive: form.value.isActive,
            sortOrder: form.value.sortOrder,
        }
        if (editing.value) {
            await service.updateProduct(editing.value.id, body)
            await load()
            flash('Rental saved.', 'success')
            dialog.value = false
        } else {
            const r = await service.createProduct(body)
            const created = (r.data as any).data as RentalProduct
            editing.value = created
            await load()
            if (created.trackingKind === 'per_item') {
                activeTab.value = 'units'
                await loadUnits(created.id)
                flash('Rental saved — add per-item units below.', 'success')
            } else {
                dialog.value = false
                flash('Rental saved.', 'success')
            }
        }
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function remove() {
    if (!editing.value) return
    if (!await confirm({ message: `Delete "${editing.value.name}"? This permanently removes the rental.`, confirmText: 'Delete', confirmColor: 'error' })) return
    try {
        await service.deleteProduct(editing.value.id)
        dialog.value = false
        await load()
        flash('Rental deleted.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    }
}

async function loadUnits(productId: string) {
    try {
        const r = await service.listItems(productId)
        units.value = (r.data as any).data
    } catch (err: any) {
        units.value = []
        flash(err.response?.data?.error ?? 'Couldn’t load rental units. Try reopening this rental.', 'error')
    }
}

function openCreateUnit() {
    editingUnit.value = null
    unitForm.value = { label: '', serial: '', notes: '', status: 'available' }
    maintenanceWindows.value = []
    unitDialog.value = true
}

async function openEditUnit(u: RentalItem) {
    editingUnit.value = u
    unitForm.value = {
        label: u.label,
        serial: u.serial ?? '',
        notes: u.notes ?? '',
        status: u.status,
    }
    await loadMaintenance(u.id)
    unitDialog.value = true
}

// ── Maintenance windows ──────────────────────────────────────────────────
const maintenanceWindows = ref<MaintenanceWindow[]>([])
const maintenanceDialog = ref(false)
const editingMaintenance = ref<MaintenanceWindow | null>(null)
const savingMaintenance = ref(false)
const maintenanceForm = ref({
    startsAtDate: dayjs().format('YYYY-MM-DD'),
    endsAtDate: dayjs().format('YYYY-MM-DD'),
    reason: '' as string | null,
})

function formatDate(d: string): string { return dayjs(d).format('MMM D, YYYY') }

async function loadMaintenance(itemId: string) {
    try {
        const r = await service.listMaintenance(itemId)
        maintenanceWindows.value = (r.data as any).data
    } catch (err: any) {
        maintenanceWindows.value = []
        flash(err.response?.data?.error ?? 'Couldn’t load maintenance windows. Try reopening this unit.', 'error')
    }
}

function openMaintenanceDialog(m: MaintenanceWindow | null) {
    editingMaintenance.value = m
    if (m) {
        maintenanceForm.value = {
            startsAtDate: dayjs(m.startsAtDate).format('YYYY-MM-DD'),
            endsAtDate: dayjs(m.endsAtDate).format('YYYY-MM-DD'),
            reason: m.reason || '',
        }
    } else {
        maintenanceForm.value = {
            startsAtDate: dayjs().format('YYYY-MM-DD'),
            endsAtDate: dayjs().format('YYYY-MM-DD'),
            reason: '',
        }
    }
    maintenanceDialog.value = true
}

async function saveMaintenance() {
    if (!editingUnit.value) return
    if (dayjs(maintenanceForm.value.endsAtDate).isBefore(dayjs(maintenanceForm.value.startsAtDate))) {
        flash('End date must be on or after start date.', 'error')
        return
    }
    savingMaintenance.value = true
    try {
        const body = {
            startsAtDate: maintenanceForm.value.startsAtDate,
            endsAtDate: maintenanceForm.value.endsAtDate,
            reason: maintenanceForm.value.reason?.trim() || null,
        }
        if (editingMaintenance.value) {
            await service.updateMaintenance(editingMaintenance.value.id, body)
        } else {
            await service.addMaintenance(editingUnit.value.id, body)
        }
        await loadMaintenance(editingUnit.value.id)
        maintenanceDialog.value = false
        flash('Maintenance window saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        savingMaintenance.value = false
    }
}

async function deleteMaintenanceWindow() {
    if (!editingMaintenance.value || !editingUnit.value) return
    if (!await confirm({ message: `Delete this maintenance window?`, confirmText: 'Delete', confirmColor: 'error' })) return
    try {
        await service.deleteMaintenance(editingMaintenance.value.id)
        await loadMaintenance(editingUnit.value.id)
        maintenanceDialog.value = false
        flash('Window deleted.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    }
}

async function saveUnit() {
    if (!editing.value) return
    try {
        savingUnit.value = true
        const body = {
            label: unitForm.value.label.trim(),
            serial: unitForm.value.serial?.trim() || null,
            notes: unitForm.value.notes?.trim() || null,
            status: unitForm.value.status,
        }
        if (editingUnit.value) {
            await service.updateItem(editingUnit.value.id, body)
        } else {
            await service.createItem(editing.value.id, body)
        }
        await loadUnits(editing.value.id)
        await load()  // refresh per-item counts on the row
        unitDialog.value = false
        flash('Unit saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        savingUnit.value = false
    }
}

async function removeUnit() {
    if (!editingUnit.value || !editing.value) return
    if (!await confirm({ message: `Delete "${editingUnit.value.label}"?`, confirmText: 'Delete', confirmColor: 'error' })) return
    try {
        await service.deleteItem(editingUnit.value.id)
        await loadUnits(editing.value.id)
        await load()
        unitDialog.value = false
        flash('Unit deleted.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    }
}

function statusColor(s: string): string {
    if (s === 'available') return 'success'
    if (s === 'maintenance') return 'warning'
    return 'grey'
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>

<style scoped>
.drag-handle-cell { padding-left: 4px !important; padding-right: 0 !important; }
.drag-handle { cursor: grab; }
.drag-handle:active { cursor: grabbing; }
.drag-ghost { opacity: 0.35; background: rgba(25, 118, 210, 0.08); }
</style>
