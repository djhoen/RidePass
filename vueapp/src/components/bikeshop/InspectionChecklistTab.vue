<template>
    <div>
        <p class="text-caption text-medium-emphasis mb-4">
            What your mechanics check on an inspection. Keep more than one if you need it — a quick
            pre-ride check alongside a full service, or separate lists if you work on both dirt bikes
            and mountain bikes. Editing a list changes future inspections only; past ones keep exactly
            what was recorded.
        </p>

        <div v-if="loading" class="text-center py-6">
            <v-progress-circular indeterminate></v-progress-circular>
        </div>

        <v-row v-else dense>
            <!-- Checklists -->
            <v-col cols="12" md="4">
                <div class="d-flex align-center mb-2">
                    <span class="text-subtitle-2">Checklists</span>
                    <v-spacer></v-spacer>
                    <v-btn size="small" variant="text" prepend-icon="mdi-plus" @click="openNewTemplate">New</v-btn>
                </div>
                <v-card variant="outlined">
                    <v-list density="compact" nav>
                        <v-list-item v-for="t in templates" :key="t.id"
                            :active="t.id === selectedId" @click="selectedId = t.id">
                            <v-list-item-title class="text-body-2">{{ t.name }}</v-list-item-title>
                            <v-list-item-subtitle class="text-caption">
                                {{ t.items.length }} point{{ t.items.length === 1 ? '' : 's' }}
                            </v-list-item-subtitle>
                            <template #append>
                                <v-chip v-if="t.isDefault" size="x-small" color="primary">Default</v-chip>
                                <v-chip v-else-if="!t.isActive" size="x-small" color="warning">Off</v-chip>
                            </template>
                        </v-list-item>
                    </v-list>
                </v-card>
            </v-col>

            <!-- Selected checklist -->
            <v-col cols="12" md="8">
                <div v-if="!selected" class="text-medium-emphasis pa-4">Pick a checklist to edit.</div>
                <template v-else>
                    <div class="d-flex align-center ga-2 mb-3 flex-wrap">
                        <v-text-field v-model="selected.name" density="compact" hide-details
                            label="Checklist name" style="max-width: 300px"
                            @blur="saveTemplate"></v-text-field>
                        <v-switch v-model="selected.isActive" color="primary" density="compact"
                            hide-details label="Active" @update:model-value="saveTemplate"></v-switch>
                        <v-btn v-if="!selected.isDefault" size="small" variant="tonal"
                            @click="makeDefault">Make default</v-btn>
                        <v-chip v-else size="small" color="primary" variant="tonal">Shop default</v-chip>
                    </div>

                    <v-alert v-if="selected.items.length === 0" type="info" variant="tonal"
                        density="compact" class="mb-3">
                        This checklist has no points yet. An inspection can't be started from it until
                        it does.
                    </v-alert>

                    <!-- Grouped rows. Group is a plain text field on each row, so renaming a group
                         is just editing its rows — no separate group entity to keep in sync. -->
                    <div v-for="g in groups" :key="g.name" class="mb-4">
                        <div class="d-flex align-center ga-2 mb-1">
                            <v-icon size="16" color="primary">mdi-folder-outline</v-icon>
                            <strong class="text-body-2">{{ g.name }}</strong>
                            <span class="text-caption text-medium-emphasis">{{ g.items.length }}</span>
                            <v-spacer></v-spacer>
                            <v-btn size="x-small" variant="text" prepend-icon="mdi-plus"
                                @click="addItem(g.name)">Add point</v-btn>
                        </div>
                        <draggable :list="g.items" item-key="id" handle=".drag-handle"
                            :animation="180" ghost-class="drag-ghost" @end="onDragEnd">
                            <template #item="{ element: it }">
                                <div class="d-flex align-center ga-2 py-1">
                                    <v-tooltip text="Drag to reorder" location="top">
                                        <template #activator="{ props }">
                                            <v-icon v-bind="props" size="20" class="drag-handle"
                                                style="cursor: grab">mdi-drag-vertical</v-icon>
                                        </template>
                                    </v-tooltip>
                                    <v-text-field v-model="it.label" density="compact" hide-details
                                        placeholder="What gets checked" @blur="saveItem(it)"></v-text-field>
                                    <v-tooltip text="Remove" location="top">
                                        <template #activator="{ props }">
                                            <v-btn v-bind="props" size="x-small" variant="text" icon="mdi-close"
                                                color="error" @click="removeItem(it)"></v-btn>
                                        </template>
                                    </v-tooltip>
                                </div>
                            </template>
                        </draggable>
                    </div>

                    <v-divider class="my-3"></v-divider>
                    <div class="d-flex ga-2 align-center">
                        <v-text-field v-model="newGroupName" density="compact" hide-details
                            label="New group (e.g. Suspension)" style="max-width: 280px"
                            @keyup.enter="addGroup"></v-text-field>
                        <v-btn variant="tonal" :disabled="!newGroupName.trim()" @click="addGroup">Add group</v-btn>
                    </div>
                </template>
            </v-col>
        </v-row>

        <!-- New checklist -->
        <v-dialog v-model="newOpen" max-width="420">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>New checklist</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="newOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="newName" label="Name" density="compact"
                        placeholder="e.g. Quick pre-ride check" hide-details
                        @keyup.enter="createTemplate"></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="newOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" :disabled="!newName.trim()"
                        @click="createTemplate">Create</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snack.show" :color="snack.color" :timeout="3500">{{ snack.text }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import draggable from 'vuedraggable'
import { BikeShopService, type ShopInspectionTemplate, type ShopInspectionTemplateItem } from '@/services/BikeShopService'

const service = new BikeShopService()

const templates = ref<ShopInspectionTemplate[]>([])
const selectedId = ref<string | null>(null)
const loading = ref(false)
const saving = ref(false)
const newOpen = ref(false)
const newName = ref('')
const newGroupName = ref('')

const snack = ref({ show: false, text: '', color: 'success' as 'success' | 'error' })
function flash(text: string, color: 'success' | 'error' = 'success') {
    snack.value = { show: true, text, color }
}

const selected = computed(() => templates.value.find(t => t.id === selectedId.value) ?? null)

// Group is just a label on each row, so the grouping is derived rather than stored separately.
// Order follows the first row of each group, so moving rows moves groups predictably.
const groups = computed(() => {
    const out: { name: string; items: ShopInspectionTemplateItem[] }[] = []
    for (const it of [...(selected.value?.items ?? [])].sort((a, b) => a.sortOrder - b.sortOrder)) {
        let g = out.find(x => x.name === it.groupLabel)
        if (!g) { g = { name: it.groupLabel, items: [] }; out.push(g) }
        g.items.push(it)
    }
    return out
})

async function load() {
    loading.value = true
    try {
        const r = await service.listInspectionTemplates()
        templates.value = r.data.data
        if (!selectedId.value || !templates.value.some(t => t.id === selectedId.value)) {
            selectedId.value = templates.value.find(t => t.isDefault)?.id ?? templates.value[0]?.id ?? null
        }
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not load the checklists. Refresh to try again.', 'error')
    } finally {
        loading.value = false
    }
}

function openNewTemplate() { newName.value = ''; newOpen.value = true }

async function createTemplate() {
    if (!newName.value.trim()) return
    saving.value = true
    try {
        const r = await service.createInspectionTemplate({ name: newName.value.trim() })
        newOpen.value = false
        await load()
        selectedId.value = r.data.data.id
        flash('Checklist created.')
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not create the checklist.', 'error')
    } finally {
        saving.value = false
    }
}

async function saveTemplate() {
    const t = selected.value
    if (!t || !t.name.trim()) return
    try {
        await service.updateInspectionTemplate(t.id, { name: t.name.trim(), isActive: t.isActive })
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not save the checklist.', 'error')
        await load()
    }
}

async function makeDefault() {
    const t = selected.value
    if (!t) return
    try {
        await service.updateInspectionTemplate(t.id, { name: t.name.trim(), isActive: t.isActive, makeDefault: true })
        await load()
        flash('New inspections will start from this checklist.')
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not make that the default.', 'error')
    }
}

// New rows go to the end of their group. Sort orders are spaced by 10 so a later insert or a
// move doesn't force renumbering everything.
function nextSortOrder(groupName: string): number {
    const g = groups.value.find(x => x.name === groupName)
    if (g && g.items.length > 0) return Math.max(...g.items.map(i => i.sortOrder)) + 10
    const all = selected.value?.items ?? []
    return all.length > 0 ? Math.max(...all.map(i => i.sortOrder)) + 100 : 10
}

async function addItem(groupName: string) {
    const t = selected.value
    if (!t) return
    try {
        await service.upsertInspectionItem(t.id, {
            groupLabel: groupName, label: 'New check', sortOrder: nextSortOrder(groupName), isActive: true,
        })
        await load()
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not add that point.', 'error')
    }
}

async function addGroup() {
    const name = newGroupName.value.trim()
    if (!name) return
    await addItem(name)
    newGroupName.value = ''
}

async function saveItem(it: ShopInspectionTemplateItem) {
    if (!selected.value || !it.label.trim()) return
    try {
        await service.upsertInspectionItem(selected.value.id, {
            id: it.id, groupLabel: it.groupLabel, label: it.label.trim(),
            sortOrder: it.sortOrder, isActive: it.isActive,
        })
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not save that point.', 'error')
        await load()
    }
}

async function removeItem(it: ShopInspectionTemplateItem) {
    try {
        await service.deleteInspectionItem(it.id)
        await load()
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not remove that point.', 'error')
    }
}

// Drag-drop reorder within a group. vuedraggable has already spliced the dragged group's items
// into their new order (in the `groups` computed's cached value, since its dep — selected.items —
// hasn't changed yet). Renumber every item across all groups in that visual order (spaced by 10),
// then persist the ones whose sort_order actually moved. Setting sortOrder mutates selected.items,
// which re-runs the computed so the render stays consistent with what was persisted.
async function onDragEnd() {
    const t = selected.value
    if (!t) return
    let order = 0
    const changed: ShopInspectionTemplateItem[] = []
    for (const g of groups.value) {
        for (const it of g.items) {
            const ns = (order += 10)
            if (it.sortOrder !== ns) { it.sortOrder = ns; changed.push(it) }
        }
    }
    if (changed.length === 0) return
    try {
        await Promise.all(changed.map(it => service.upsertInspectionItem(t.id, {
            id: it.id, groupLabel: it.groupLabel, label: it.label.trim(),
            sortOrder: it.sortOrder, isActive: it.isActive,
        })))
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not save the new order.', 'error')
        await load()
    }
}

onMounted(load)
</script>
