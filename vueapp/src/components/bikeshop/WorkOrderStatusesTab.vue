<template>
    <div>
        <p class="text-caption text-medium-emphasis mb-4">
            The stages a repair moves through. Rename or recolor any of them, drag to reorder them, and
            add your own working stages (for example "Waiting on customer" or "Test riding"). The built-in
            stages keep their behavior: <strong>Estimate</strong> holds parts as a quote,
            <strong>Ready</strong> and <strong>Picked up</strong> and <strong>Cancelled</strong> do
            what they say. Turn on the bell to text or email the customer when a repair reaches a stage.
        </p>

        <div v-if="loading" class="text-center py-6">
            <v-progress-circular indeterminate></v-progress-circular>
        </div>

        <template v-else>
            <v-card variant="outlined">
                <draggable :list="visibleRows" item-key="id" handle=".drag-handle"
                    :animation="180" ghost-class="drag-ghost" @end="onReorderEnd">
                    <template #item="{ element: s, index: i }">
                        <div class="d-flex align-center ga-2 pa-2 flex-wrap"
                            :class="{ 'border-b': i < visibleRows.length - 1 }">
                            <v-tooltip text="Drag to reorder" location="top">
                                <template #activator="{ props }">
                                    <v-icon v-bind="props" class="drag-handle" style="cursor: grab">mdi-drag-vertical</v-icon>
                                </template>
                            </v-tooltip>

                            <!-- Color swatch + preview -->
                            <v-menu>
                                <template #activator="{ props: menuProps }">
                                    <v-tooltip text="Change color" location="top">
                                        <template #activator="{ props: tipProps }">
                                            <v-btn v-bind="mergeProps(menuProps, tipProps)" size="small"
                                                variant="tonal" :color="s.color" icon="mdi-palette"></v-btn>
                                        </template>
                                    </v-tooltip>
                                </template>
                                <v-card><v-card-text class="d-flex flex-wrap ga-1" style="max-width: 240px">
                                    <v-btn v-for="c in palette" :key="c" :color="c" size="x-small" icon
                                        :variant="s.color === c ? 'flat' : 'tonal'" @click="s.color = c"></v-btn>
                                </v-card-text></v-card>
                            </v-menu>

                            <v-text-field v-model="s.name" density="compact" hide-details style="max-width: 220px"
                                :disabled="saving === s.id"></v-text-field>

                            <v-chip size="x-small" variant="tonal">{{ behaviorLabel(s.behavior) }}</v-chip>
                            <v-chip v-if="s.isDefault" size="x-small" color="primary">Default start</v-chip>
                            <v-chip v-if="!s.isActive" size="x-small" color="warning">Off</v-chip>

                            <v-spacer></v-spacer>

                            <v-btn v-if="dirty(s)" size="small" color="primary" variant="tonal"
                                :loading="saving === s.id" @click="save(s)">Save</v-btn>

                            <!-- Notification bell + the row actions all live on the right. -->
                            <v-tooltip text="Notify the customer when a repair reaches this stage" location="top">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" size="small" variant="text"
                                        :color="s.notifyCustomer ? 'primary' : undefined"
                                        :icon="s.notifyCustomer ? 'mdi-bell' : 'mdi-bell-outline'"
                                        @click="s.notifyCustomer = !s.notifyCustomer"></v-btn>
                                </template>
                            </v-tooltip>

                            <v-tooltip v-if="!s.isDefault && s.behavior !== 'done' && s.behavior !== 'cancelled' && s.isActive"
                                text="Make this the starting stage" location="top">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" size="x-small" variant="text"
                                        icon="mdi-flag-outline" @click="makeDefault(s)"></v-btn>
                                </template>
                            </v-tooltip>

                            <!-- Built-in stages can be turned on always; custom ones can be switched off. -->
                            <v-tooltip v-if="!s.isBuiltin && !s.isDefault" :text="s.isActive ? 'Turn off' : 'Turn on'" location="top">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" size="x-small" variant="text"
                                        :icon="s.isActive ? 'mdi-eye-off-outline' : 'mdi-eye-outline'"
                                        @click="toggleActive(s)"></v-btn>
                                </template>
                            </v-tooltip>
                            <v-tooltip v-if="!s.isBuiltin" text="Delete" location="top">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" size="x-small" variant="text" color="error"
                                        icon="mdi-delete-outline" @click="remove(s)"></v-btn>
                                </template>
                            </v-tooltip>
                        </div>
                    </template>
                </draggable>
            </v-card>

            <!-- Add a custom working stage -->
            <div class="d-flex align-center ga-2 mt-3 flex-wrap">
                <v-text-field v-model="newName" label="New stage name" density="compact" hide-details
                    placeholder="e.g. Waiting on customer" style="max-width: 260px"
                    @keyup.enter="add"></v-text-field>
                <v-menu>
                    <template #activator="{ props: menuProps }">
                        <v-tooltip text="Change color" location="top">
                            <template #activator="{ props: tipProps }">
                                <v-btn v-bind="mergeProps(menuProps, tipProps)" size="small" variant="tonal"
                                    :color="newColor" icon="mdi-palette"></v-btn>
                            </template>
                        </v-tooltip>
                    </template>
                    <v-card><v-card-text class="d-flex flex-wrap ga-1" style="max-width: 240px">
                        <v-btn v-for="c in palette" :key="c" :color="c" size="x-small" icon
                            :variant="newColor === c ? 'flat' : 'tonal'" @click="newColor = c"></v-btn>
                    </v-card-text></v-card>
                </v-menu>
                <v-btn color="primary" variant="tonal" :loading="adding" :disabled="!newName.trim()"
                    @click="add">Add stage</v-btn>
            </div>
        </template>

        <v-snackbar v-model="snack.show" :color="snack.color" :timeout="3500">{{ snack.text }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted, mergeProps } from 'vue'
import draggable from 'vuedraggable'
import { BikeShopService, type ShopWorkOrderStatusDef } from '@/services/BikeShopService'
import { useDragReorder } from '@/composables/useDragReorder'
import { useConfirm } from '@/composables/useConfirm'

const service = new BikeShopService()
const confirm = useConfirm()

const statuses = ref<ShopWorkOrderStatusDef[]>([])
const original = ref<Record<string, string>>({})   // id -> serialized presentation, for dirty check
const loading = ref(false)
const saving = ref<string | null>(null)
const adding = ref(false)
const newName = ref('')
const newColor = ref('blue')

// Vuetify color tokens that render as chip colors.
const palette = ['grey', 'blue-grey', 'blue', 'indigo', 'deep-purple', 'purple', 'teal', 'cyan',
    'green', 'success', 'lime', 'amber', 'orange', 'warning', 'deep-orange', 'red', 'error', 'brown', 'primary']

// Drag-drop reorder: persist the new order via the bulk endpoint, then rebaseline the dirty
// snapshots (sort_order is part of the snapshot) so a reorder doesn't leave every row "dirty".
const { visibleRows, onReorderEnd } = useDragReorder<ShopWorkOrderStatusDef>({
    rows: statuses,
    save: items => service.reorderWorkOrderStatuses(items),
    onSuccess: () => {
        for (const s of statuses.value) original.value[s.id] = snapshot(s)
        flash('Order saved.')
    },
    onError: () => load(),
})

const snack = ref({ show: false, text: '', color: 'success' as 'success' | 'error' })
function flash(text: string, color: 'success' | 'error' = 'success') { snack.value = { show: true, text, color } }

function behaviorLabel(b: string): string {
    return b === 'estimate' ? 'Quote' : b === 'ready' ? 'Ready' : b === 'done' ? 'Done'
        : b === 'cancelled' ? 'Cancelled' : 'Working'
}
// A row is dirty when its editable fields differ from what was loaded/last saved.
function snapshot(s: ShopWorkOrderStatusDef): string { return `${s.name}|${s.color}|${s.notifyCustomer}|${s.sortOrder}|${s.isActive}` }
function dirty(s: ShopWorkOrderStatusDef): boolean { return original.value[s.id] !== snapshot(s) }

async function load() {
    loading.value = true
    try {
        const r = await service.listWorkOrderStatuses()
        statuses.value = r.data.data
        original.value = Object.fromEntries(statuses.value.map(s => [s.id, snapshot(s)]))
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not load the statuses. Refresh to try again.', 'error')
    } finally {
        loading.value = false
    }
}

async function save(s: ShopWorkOrderStatusDef) {
    if (!s.name.trim()) { flash('Give the stage a name.', 'error'); return }
    saving.value = s.id
    try {
        await service.updateWorkOrderStatus(s.id, {
            name: s.name.trim(), color: s.color, notifyCustomer: s.notifyCustomer,
            sortOrder: s.sortOrder, isActive: s.isActive,
        })
        original.value[s.id] = snapshot(s)
        flash('Saved.')
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not save that stage.', 'error')
        await load()
    } finally {
        saving.value = null
    }
}

async function makeDefault(s: ShopWorkOrderStatusDef) {
    try {
        await service.setDefaultWorkOrderStatus(s.id)
        await load()
        flash('New repairs will start here.')
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not make that the default.', 'error')
    }
}

async function toggleActive(s: ShopWorkOrderStatusDef) {
    const next = !s.isActive
    try {
        await service.updateWorkOrderStatus(s.id, {
            name: s.name.trim(), color: s.color, notifyCustomer: s.notifyCustomer,
            sortOrder: s.sortOrder, isActive: next,
        })
        await load()
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not change that.', 'error')
    }
}

async function remove(s: ShopWorkOrderStatusDef) {
    if (!await confirm({ title: 'Delete stage', message: `Delete "${s.name}"? This can't be undone.`, confirmText: 'Delete', confirmColor: 'error' })) return
    try {
        await service.deleteWorkOrderStatus(s.id)
        await load()
        flash('Stage deleted.')
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not delete that stage.', 'error')
    }
}

async function add() {
    const name = newName.value.trim()
    if (!name) return
    adding.value = true
    try {
        await service.createWorkOrderStatus({ name, color: newColor.value, notifyCustomer: false })
        newName.value = ''
        newColor.value = 'blue'
        await load()
        flash('Stage added.')
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not add that stage.', 'error')
    } finally {
        adding.value = false
    }
}

onMounted(load)
</script>

<style scoped>
.border-b {
    border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
}
.drag-ghost {
    opacity: 0.5;
}
</style>
