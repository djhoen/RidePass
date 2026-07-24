<template>
    <v-container fluid>
        <div class="d-flex align-center mb-4 ga-2 flex-wrap">
            <h1 class="text-h5">Packages</h1>
            <v-spacer />
            <v-btn variant="text" prepend-icon="mdi-refresh" @click="load">Refresh</v-btn>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openNew">New package</v-btn>
        </div>
        <p class="text-body-2 text-medium-emphasis mb-4">
            Bundled products (like Find Your Ride): a day pass, a coached session, a bike, and gear,
            sold at day-type tiers with a landing page.
        </p>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <v-card variant="outlined">
            <v-table density="compact">
                <thead>
                    <tr><th>Name</th><th>Options</th><th>Includes</th><th>Landing</th><th>Active</th><th class="text-right">Actions</th></tr>
                </thead>
                <tbody>
                    <tr v-for="p in packages" :key="p.id" class="row-click" @click="openEdit(p.id)">
                        <td>{{ p.name }}<div v-if="p.slug" class="text-caption text-medium-emphasis">/{{ p.slug }}</div></td>
                        <td>{{ p.tiers.map(t => t.name).join(', ') || '-' }}</td>
                        <td class="text-no-wrap">
                            <v-chip v-if="p.includesDayTicket" size="x-small" class="mr-1">Day pass</v-chip>
                            <v-chip v-if="p.coachingMinutes" size="x-small" class="mr-1">Coaching</v-chip>
                            <v-chip v-for="it in p.items" :key="it.id" size="x-small" class="mr-1">{{ it.itemType }}</v-chip>
                        </td>
                        <td><v-chip size="x-small" :color="p.landingPublished ? 'success' : 'grey'">{{ p.landingPublished ? 'Published' : 'Draft' }}</v-chip></td>
                        <td><v-icon :icon="p.isActive ? 'mdi-check' : 'mdi-close'" :color="p.isActive ? 'success' : 'grey'" size="18" /></td>
                        <td class="text-right text-no-wrap">
                            <v-btn icon="mdi-open-in-new" size="x-small" variant="text" :href="`/Packages/${p.slug || p.id}`" target="_blank" @click.stop />
                            <v-btn icon="mdi-delete" size="x-small" variant="text" color="error" @click.stop="remove(p)" />
                        </td>
                    </tr>
                    <tr v-if="!loading && packages.length === 0">
                        <td colspan="6" class="text-center text-medium-emphasis py-6">No packages yet. Create one with New package.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <!-- Editor -->
        <v-dialog v-model="editOpen" max-width="820" scrollable>
            <v-card class="d-flex flex-column" style="max-height: 92vh">
                <v-card-title class="d-flex align-center" style="flex:0 0 auto">
                    {{ editing.id ? 'Edit package' : 'New package' }}
                    <v-spacer /><v-btn icon="mdi-close" variant="text" size="small" @click="editOpen = false" />
                </v-card-title>
                <v-card-text style="flex:1 1 auto; overflow-y:auto">
                    <v-alert v-if="editError" type="error" variant="tonal" density="compact" class="mb-4">{{ editError }}</v-alert>

                    <div class="text-subtitle-2 mb-2">Details</div>
                    <v-text-field v-model="editing.name" label="Name" density="compact" />
                    <v-text-field v-model="editing.slug" label="Slug (landing URL)" density="compact" class="mt-4"
                        hint="Lowercase, e.g. find-your-ride" persistent-hint />
                    <v-text-field v-model="editing.summary" label="Summary (one line)" density="compact" class="mt-4" />
                    <v-textarea v-model="editing.description" label="Landing content (HTML)" density="compact" rows="3" class="mt-4" />
                    <v-text-field v-model="editing.heroImageUrl" label="Hero image URL" density="compact" class="mt-4" />
                    <div class="d-flex ga-4 mt-2 flex-wrap">
                        <v-switch v-model="editing.isActive" label="Active" color="primary" hide-details density="compact" />
                        <v-switch v-model="editing.landingPublished" label="Landing published" color="primary" hide-details density="compact" />
                        <v-switch v-model="editing.includesDayTicket" label="Includes day pass" color="primary" hide-details density="compact" />
                    </div>

                    <v-divider class="my-4" />
                    <div class="text-subtitle-2 mb-2">Coaching (optional)</div>
                    <div class="d-flex ga-3 flex-wrap">
                        <v-text-field v-model.number="editing.coachingMinutes" label="Session minutes" type="number" density="compact" style="max-width:170px" hide-details />
                        <v-text-field v-model="editing.coachingLabel" label="Session label" density="compact" style="max-width:260px" hide-details placeholder="Park Ready session" />
                    </div>

                    <v-divider class="my-4" />
                    <div class="d-flex align-center mb-2"><div class="text-subtitle-2">Price options (tiers)</div><v-spacer /><v-btn size="x-small" variant="tonal" @click="addTier">Add</v-btn></div>
                    <div v-for="(t, i) in editing.tiers" :key="i" class="d-flex ga-2 align-center mb-2 flex-wrap">
                        <v-text-field v-model="t.name" label="Name" density="compact" hide-details style="max-width:150px" />
                        <v-text-field v-model.number="t.priceDollars" label="Price $" type="number" density="compact" hide-details style="max-width:110px" />
                        <v-select v-model="t.dayScope" :items="dayScopes" label="Days" density="compact" hide-details style="max-width:130px" />
                        <v-checkbox v-model="t.afternoonOnly" label="PM only" density="compact" hide-details />
                        <v-text-field v-model.number="t.sessionCount" label="Sessions" type="number" density="compact" hide-details style="max-width:100px" />
                        <v-btn icon="mdi-close" size="x-small" variant="text" @click="editing.tiers.splice(i,1)" />
                    </div>

                    <v-divider class="my-4" />
                    <div class="d-flex align-center mb-2"><div class="text-subtitle-2">Coached session times</div><v-spacer /><v-btn size="x-small" variant="tonal" @click="addSlot">Add</v-btn></div>
                    <div v-for="(s, i) in editing.slots" :key="i" class="d-flex ga-2 align-center mb-2 flex-wrap">
                        <v-select v-model="s.dayScope" :items="dayScopes" label="Days" density="compact" hide-details style="max-width:130px" />
                        <input type="time" v-model="s.startTime" class="pkg-time" />
                        <v-checkbox v-model="s.isAfternoon" label="PM" density="compact" hide-details />
                        <v-text-field v-model.number="s.capacity" label="Capacity" type="number" density="compact" hide-details style="max-width:110px" />
                        <v-select v-model="s.instructorId" :items="instructorItems" item-title="title" item-value="id" label="Instructor" density="compact" hide-details clearable style="max-width:180px" />
                        <v-btn icon="mdi-close" size="x-small" variant="text" @click="editing.slots.splice(i,1)" />
                    </div>

                    <v-divider class="my-4" />
                    <div class="d-flex align-center mb-2"><div class="text-subtitle-2">Included bike &amp; gear</div><v-spacer /><v-btn size="x-small" variant="tonal" @click="addItem">Add</v-btn></div>
                    <div v-for="(it, i) in editing.items" :key="i" class="d-flex ga-2 align-center mb-2 flex-wrap">
                        <v-select v-model="it.itemType" :items="itemTypes" label="Type" density="compact" hide-details style="max-width:120px" />
                        <v-select v-model="it.variantId" :items="variantItems" item-title="title" item-value="id" label="Rental item" density="compact" hide-details style="max-width:320px" />
                        <v-text-field v-model.number="it.quantity" label="Qty" type="number" density="compact" hide-details style="max-width:90px" />
                        <v-btn icon="mdi-close" size="x-small" variant="text" @click="editing.items.splice(i,1)" />
                    </div>
                </v-card-text>
                <v-card-actions style="flex:0 0 auto">
                    <v-spacer />
                    <v-btn variant="text" @click="editOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="save">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snack" :timeout="3000" color="success">{{ snackText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { PackageService, type PackageProduct, type UpsertPackageRequest } from '@/services/PackageService'
import { BikeShopService } from '@/services/BikeShopService'
import { InstructorService } from '@/services/InstructorService'
import { useConfirm } from '@/composables/useConfirm'

const service = new PackageService()
const confirm = useConfirm()

const packages = ref<PackageProduct[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
const snack = ref(false)
const snackText = ref('')

const dayScopes = [{ title: 'Any day', value: 'any' }, { title: 'Weekdays', value: 'weekday' }, { title: 'Weekends', value: 'weekend' }]
const itemTypes = [{ title: 'Bike', value: 'bike' }, { title: 'Gear', value: 'gear' }]

const variantItems = ref<{ id: string; title: string }[]>([])
const instructorItems = ref<{ id: string; title: string }[]>([])

interface EditTier { name: string; priceDollars: number; dayScope: string; afternoonOnly: boolean; sessionCount: number }
interface EditSlot { dayScope: string; startTime: string; isAfternoon: boolean; capacity: number; instructorId: string | null }
interface EditItem { itemType: 'bike' | 'gear'; variantId: string | null; quantity: number }
interface Editing {
    id: string | null; name: string; slug: string; summary: string; description: string; heroImageUrl: string
    isActive: boolean; landingPublished: boolean; includesDayTicket: boolean
    coachingMinutes: number | null; coachingLabel: string
    tiers: EditTier[]; slots: EditSlot[]; items: EditItem[]
}
const editOpen = ref(false)
const editError = ref<string | null>(null)
const saving = ref(false)
const editing = ref<Editing>(blank())

function blank(): Editing {
    return {
        id: null, name: '', slug: '', summary: '', description: '', heroImageUrl: '',
        isActive: true, landingPublished: false, includesDayTicket: true,
        coachingMinutes: null, coachingLabel: '', tiers: [], slots: [], items: [],
    }
}

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const r = await service.listAdmin()
        packages.value = r.data.data
    } catch (err: any) {
        loadError.value = err.response?.data?.error ?? 'Could not load packages. Try Refresh.'
    } finally {
        loading.value = false
    }
}

async function loadPickers() {
    try {
        const cat = await new BikeShopService().rentalCatalog()
        variantItems.value = cat.data.data.products.flatMap(p => p.variants.map(v => ({
            id: v.id,
            title: `${p.name}${v.size ? ' · ' + v.size : ''} ($${(v.dailyRateCents / 100).toFixed(0)}/day)`,
        })))
    } catch { /* variant picker stays empty; save still validates server-side */ }
    try {
        const inst = await new InstructorService().listActive()
        instructorItems.value = (inst.data.data ?? []).map((i: any) => ({ id: i.id, title: i.name }))
    } catch { /* optional */ }
}

function openNew() { editing.value = blank(); editError.value = null; editOpen.value = true }

async function openEdit(id: string) {
    editError.value = null
    editOpen.value = true
    try {
        const r = await service.getAdmin(id)
        const p = r.data.data
        editing.value = {
            id: p.id, name: p.name, slug: p.slug ?? '', summary: p.summary ?? '', description: p.description ?? '',
            heroImageUrl: p.heroImageUrl ?? '', isActive: p.isActive, landingPublished: p.landingPublished,
            includesDayTicket: p.includesDayTicket, coachingMinutes: p.coachingMinutes, coachingLabel: p.coachingLabel ?? '',
            tiers: p.tiers.map(t => ({ name: t.name, priceDollars: t.priceCents / 100, dayScope: t.dayScope, afternoonOnly: t.afternoonOnly, sessionCount: t.sessionCount })),
            slots: p.slots.map(s => ({ dayScope: s.dayScope, startTime: s.startTime, isAfternoon: s.isAfternoon, capacity: s.capacity, instructorId: s.instructorId })),
            items: p.items.map(it => ({ itemType: it.itemType, variantId: it.variantId, quantity: it.quantity })),
        }
    } catch (err: any) {
        editError.value = err.response?.data?.error ?? 'Could not load this package.'
    }
}

function addTier() { editing.value.tiers.push({ name: '', priceDollars: 0, dayScope: 'any', afternoonOnly: false, sessionCount: 1 }) }
function addSlot() { editing.value.slots.push({ dayScope: 'any', startTime: '09:00', isAfternoon: false, capacity: 8, instructorId: null }) }
function addItem() { editing.value.items.push({ itemType: 'gear', variantId: null, quantity: 1 }) }

function toRequest(e: Editing): UpsertPackageRequest {
    return {
        name: e.name.trim(), slug: e.slug.trim() || null, summary: e.summary || null, description: e.description || null,
        heroImageUrl: e.heroImageUrl || null, landingPublished: e.landingPublished, includesDayTicket: e.includesDayTicket,
        dayTicketEventTypeCode: 'open_ride', coachingMinutes: e.coachingMinutes || null, coachingLabel: e.coachingLabel || null,
        isActive: e.isActive, sortOrder: 0,
        tiers: e.tiers.map((t, i) => ({ name: t.name, priceCents: Math.round((t.priceDollars || 0) * 100), dayScope: t.dayScope as any, afternoonOnly: t.afternoonOnly, sessionCount: Math.max(1, t.sessionCount || 1), sortOrder: i })),
        slots: e.slots.map((s, i) => ({ dayScope: s.dayScope as any, startTime: s.startTime, isAfternoon: s.isAfternoon, capacity: Math.max(1, s.capacity || 1), instructorId: s.instructorId, sortOrder: i })),
        items: e.items.filter(it => it.variantId).map((it, i) => ({ itemType: it.itemType, variantId: it.variantId!, quantity: Math.max(1, it.quantity || 1), sortOrder: i })),
    }
}

async function save() {
    if (!editing.value.name.trim()) { editError.value = 'A name is required.'; return }
    saving.value = true
    editError.value = null
    try {
        const req = toRequest(editing.value)
        if (editing.value.id) await service.update(editing.value.id, req)
        else await service.create(req)
        editOpen.value = false
        snackText.value = 'Package saved'
        snack.value = true
        load()
    } catch (err: any) {
        editError.value = err.response?.data?.error ?? 'Could not save the package. Check the fields and try again.'
    } finally {
        saving.value = false
    }
}

async function remove(p: PackageProduct) {
    const ok = await confirm({ title: 'Delete package?', message: `Delete "${p.name}"? This can't be undone.`, confirmText: 'Delete', confirmColor: 'error' })
    if (!ok) return
    try {
        await service.remove(p.id)
        snackText.value = 'Package deleted'
        snack.value = true
        load()
    } catch (err: any) {
        loadError.value = err.response?.data?.error ?? 'Could not delete the package.'
    }
}

onMounted(() => { load(); loadPickers() })
</script>

<style scoped>
.row-click { cursor: pointer; }
.pkg-time { border: 1px solid rgba(var(--v-theme-on-surface), 0.3); border-radius: 6px; padding: 6px 8px; font-size: 14px; }
</style>
