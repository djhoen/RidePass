<template>
    <v-dialog :model-value="open" @update:model-value="$emit('update:open', $event)" max-width="800" scrollable>
        <v-card>
            <v-card-title class="d-flex align-center">
                <span>{{ editing ? 'Edit Event' : 'Add Event' }}</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" @click="$emit('update:open', false)"></v-btn>
            </v-card-title>
            <!-- Tabs: General | Race Classes (race events only) | Add-ons | Waivers.
                 Tickets are gone — use a Gate Fee add-on for spectator admissions. -->
            <v-tabs v-model="activeTab" color="primary" grow>
                <v-tab value="info">Details</v-tab>
                <v-tab v-if="isRaceEvent" value="race" :disabled="!editing">
                    Race Classes
                    <v-tooltip v-if="!editing" location="bottom" activator="parent">
                        Save the event first, then add race classes here.
                    </v-tooltip>
                </v-tab>
                <v-tab v-if="branding.extrasEnabled" value="extras">Add-ons &amp; Gate Fees</v-tab>
                <v-tab value="waivers">Waivers</v-tab>
            </v-tabs>

            <v-card-text style="min-height: 460px">
                <v-window v-model="activeTab" class="mt-4">
                    <v-window-item value="info">
                        <p class="text-caption text-medium-emphasis mt-2 mb-0">
                            Set the basics here. Use the tabs above for
                            <template v-if="isRaceEvent">race classes, </template>
                            spectator gate fees and add-ons, and waiver requirements.
                        </p>
                        <v-row class="mt-1">
                            <v-col cols="12" md="8">
                                <v-text-field v-model="form.title" label="Title" density="compact"></v-text-field>
                            </v-col>
                            <v-col cols="12" md="4">
                                <v-select v-model="form.eventTypeId" :items="typeOptions" item-title="name" item-value="id"
                                    label="Type" density="compact"></v-select>
                            </v-col>
                        </v-row>
                        <v-row>
                            <v-col cols="12" md="6">
                                <v-text-field v-model="form.startsLocal" type="datetime-local" label="Starts" density="compact"></v-text-field>
                            </v-col>
                            <v-col cols="12" md="6">
                                <v-text-field v-model="form.endsLocal" type="datetime-local" label="Ends" density="compact"></v-text-field>
                            </v-col>
                        </v-row>
                        <v-row>
                            <v-col cols="12" :md="isRaceEvent ? 6 : 4">
                                <v-checkbox v-model="form.allDay" label="All day"></v-checkbox>
                            </v-col>
                            <v-col v-if="!isRaceEvent" cols="12" md="4">
                                <v-text-field v-model.number="form.capacity" type="number" label="Capacity (blank = unlimited)" density="compact"></v-text-field>
                            </v-col>
                            <v-col cols="12" :md="isRaceEvent ? 6 : 4">
                                <v-select v-model="form.status" :items="['scheduled','cancelled']" label="Status" density="compact"></v-select>
                            </v-col>
                        </v-row>
                        <p v-if="isRaceEvent" class="text-caption text-medium-emphasis mt-n2 mb-2">
                            Race-event capacity is set by the inventory on each race entry below.
                        </p>
                        <v-text-field v-model="form.locationLabel" label="Location" density="compact" class="mt-4"></v-text-field>

                        <!-- Day-pass eligibility (non-race events only). The product list
                             determines which passes the rider can use to reserve a spot
                             at this event. Empty = no pass reservation option. -->
                        <template v-if="!isRaceEvent">
                            <label class="text-subtitle-2 d-block mt-5 mb-1">Rider entry: accepted passes</label>
                            <p class="text-caption text-medium-emphasis mb-2">
                                Which day passes riders can use to reserve a spot. Leave all unchecked for a
                                spectator-only event.
                            </p>
                            <div v-if="passProducts.length === 0" class="text-medium-emphasis text-caption">
                                No pass products defined yet.
                                <router-link to="/Admin/Passes">Add some first.</router-link>
                            </div>
                            <div v-else class="d-flex flex-column">
                                <v-checkbox v-for="p in passProducts" :key="p.id"
                                    v-model="form.eligiblePassProductIds"
                                    :value="p.id"
                                    :label="`${p.name} — $${(p.priceCents / 100).toFixed(2)}${p.isActive ? '' : ' (inactive)'}`"
                                    density="compact" hide-details></v-checkbox>
                            </div>
                        </template>

                        <label class="text-subtitle-2 d-block mt-6 mb-1">Event details</label>
                        <p class="text-caption text-medium-emphasis mb-2">
                            Put one detail per line. Each line shows up as its own bullet point in the
                            "Event Details" list on the public event page.
                        </p>
                        <v-textarea v-model="form.description" rows="4" density="compact"
                            placeholder="Gates open at 7:00 AM&#10;Practice starts at 8:30 AM&#10;Concessions on site all day&#10;Free parking"
                            hint="Each line becomes its own bullet point." persistent-hint></v-textarea>

                        <label class="text-subtitle-2 d-block mt-4 mb-2">Event schedule (optional)</label>
                        <p class="text-caption text-medium-emphasis mb-2">
                            Shown as a schedule on the public event page. Add rows like "7:00 AM" / "Gates open &amp; check-in".
                        </p>
                        <div v-for="(row, i) in form.schedule" :key="i" class="d-flex align-center ga-2 mb-2">
                            <v-text-field v-model="row.time" label="Time" placeholder="7:00 AM"
                                density="compact" hide-details style="max-width: 150px"></v-text-field>
                            <v-text-field v-model="row.label" label="What's happening"
                                placeholder="Gates open & check-in" density="compact" hide-details></v-text-field>
                            <v-btn icon="mdi-close" variant="text" size="small"
                                @click="form.schedule.splice(i, 1)"></v-btn>
                        </div>
                        <v-btn variant="tonal" size="small" prepend-icon="mdi-plus"
                            @click="form.schedule.push({ time: '', label: '' })">Add row</v-btn>

                        <label class="text-subtitle-2 d-block mt-6 mb-2">Cover image (optional)</label>
                        <p class="text-caption text-medium-emphasis mb-2">
                            Overrides the event type's default image on the public home page.
                        </p>
                        <div class="d-flex align-center ga-3 mb-2 flex-wrap">
                            <div v-if="form.imageUrl" class="event-image-preview"
                                :style="{ backgroundImage: `url(${absoluteUrl(form.imageUrl)})` }"></div>
                            <div v-else class="event-image-preview empty">
                                <v-icon color="grey">mdi-image-off-outline</v-icon>
                            </div>
                            <div class="d-flex flex-column ga-1">
                                <v-file-input v-model="imageFile" label="Upload" accept="image/*"
                                    density="compact" prepend-icon="mdi-upload" :loading="uploadingImage"
                                    @update:model-value="onImageSelected"></v-file-input>
                                <v-btn v-if="form.imageUrl" size="small" variant="text" color="error"
                                    prepend-icon="mdi-delete" @click="form.imageUrl = null">Remove image</v-btn>
                            </div>
                        </div>
                    </v-window-item>

                    <v-window-item v-if="isRaceEvent" value="race">
                        <p class="text-caption text-medium-emphasis mb-2">
                            Race classes — riders pay to enter a class (Open, Beginner, Pro, etc.). You can attach bundled coupons that get auto-issued at purchase.
                        </p>
                        <TicketTiersList :event-id="editing ? editing.id : null" kind="race_entry" />
                    </v-window-item>

                    <v-window-item v-if="branding.extrasEnabled" value="extras">
                        <p class="text-caption text-medium-emphasis mb-2">
                            Add-ons offered at this event — Gate Fees for spectator admission, plus camping, parking,
                            pit-vehicle, merch, etc. Inventory is per-event; leave blank for unlimited.
                        </p>
                        <div v-if="extraProducts.length === 0" class="text-medium-emphasis text-caption">
                            No add-on products defined yet.
                            <router-link to="/Admin/Extras">Add some first.</router-link>
                        </div>
                        <div v-else>
                            <div v-for="p in extraProducts" :key="p.id" class="d-flex align-center ga-2 py-1">
                                <v-checkbox :model-value="extraEnabled(p.id)"
                                    @update:model-value="toggleExtra(p.id, $event)"
                                    :label="`${p.name} ($${(p.priceCents / 100).toFixed(2)})`"
                                    density="compact" hide-details
                                    style="flex: 1"></v-checkbox>
                                <v-text-field v-if="extraEnabled(p.id)"
                                    :model-value="extraInventory(p.id)"
                                    @update:model-value="setExtraInventory(p.id, $event)"
                                    type="number" min="1" max="100000"
                                    label="Inventory" placeholder="Unlimited"
                                    density="compact" hide-details
                                    style="max-width: 130px"></v-text-field>
                            </div>
                        </div>
                    </v-window-item>

                    <v-window-item value="waivers">
                        <p class="text-caption text-medium-emphasis mb-3">
                            Toggle each audience independently — racers and spectators can be required to sign
                            different waivers, or just one of them. Leave a waiver blank to fall back to the tenant default.
                        </p>

                        <v-switch v-if="isRaceEvent" v-model="form.requiresRiderWaiver"
                            label="Require Rider Signed Waiver"
                            color="primary" density="compact" hide-details class="mb-2 ml-2"></v-switch>
                        <template v-if="isRaceEvent && form.requiresRiderWaiver">
                            <v-select v-model="form.racerWaiverId"
                                :items="racerWaiverOptions" item-title="title" item-value="value"
                                label="Racer waiver" density="compact" clearable hide-details
                                hint="Signed by riders entering a race class. Leave blank for tenant default."
                                persistent-hint class="mb-3"></v-select>
                            <v-alert v-if="racerWaiverInvalid"
                                type="warning" variant="tonal" density="compact" class="mb-3">
                                The selected racer waiver expires before this event ends. Pick another or extend its expiration.
                            </v-alert>
                        </template>

                        <v-divider v-if="isRaceEvent" class="my-4"></v-divider>

                        <v-switch v-model="form.requiresSpectatorWaiver"
                            label="Require Spectator Signed Waiver"
                            color="primary" density="compact" hide-details class="mb-2 ml-2"></v-switch>
                        <template v-if="form.requiresSpectatorWaiver">
                            <v-select v-model="form.spectatorWaiverId"
                                :items="spectatorWaiverOptions" item-title="title" item-value="value"
                                label="Spectator waiver" density="compact" clearable hide-details
                                hint="Signed by Gate Fee buyers and other non-racer admissions. Leave blank for tenant default."
                                persistent-hint class="mb-3 mt-4"></v-select>
                            <v-alert v-if="spectatorWaiverInvalid"
                                type="warning" variant="tonal" density="compact" class="mb-3">
                                The selected spectator waiver expires before this event ends. Pick another or extend its expiration.
                            </v-alert>
                        </template>

                        <p v-if="(form.requiresRiderWaiver || form.requiresSpectatorWaiver) && !waivers.length"
                            class="text-caption text-medium-emphasis">
                            No waivers defined yet.
                            <router-link to="/Admin/Waiver">Add some first.</router-link>
                        </p>
                    </v-window-item>
                </v-window>
            </v-card-text>
            <v-card-actions>
                <v-btn v-if="editing" variant="text" color="error" @click="remove">Delete</v-btn>
                <v-btn v-if="editing" variant="text" prepend-icon="mdi-content-copy" @click="dup">Duplicate</v-btn>
                <v-spacer></v-spacer>
                <v-btn @click="$emit('update:open', false)">Cancel</v-btn>
                <v-btn v-if="activeTab !== 'race'" color="primary" :loading="saving" @click="save">
                    {{ saveLabel }}
                </v-btn>
            </v-card-actions>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, computed } from 'vue'
import dayjs from 'dayjs'
import { EventService, type EventDto } from '@/services/EventService'
import { EventTypeService, type EventType } from '@/services/EventTypeService'
import { PassService, type PassProduct } from '@/services/PassService'
import { ExtraService, type ExtraProduct } from '@/services/ExtraService'
import { WaiverService, type WaiverDto } from '@/services/WaiverService'
import { branding } from '@/stores/branding'
import TicketTiersList from '@/components/TicketTiersList.vue'

const props = defineProps<{
    open: boolean
    // Existing event to edit, or null for create.
    event?: EventDto | null
    // When set (and `event` is null), the dialog opens in create mode prefilled
    // from this row with start/end shifted +7 days. Saving creates a new event;
    // closing without saving creates nothing.
    duplicateFrom?: EventDto | null
}>()

const emit = defineEmits<{
    (e: 'update:open', value: boolean): void
    (e: 'saved', event: EventDto): void
    (e: 'deleted', id: string): void
    (e: 'flash', text: string, color: 'success' | 'error'): void
}>()

const eventService = new EventService()
const eventTypeService = new EventTypeService()

const typeOptions = ref<EventType[]>([])
const editing = ref<EventDto | null>(null)
const saving = ref(false)
const activeTab = ref<'info' | 'tickets' | 'race'>('info')

// Race events get their capacity from the sum of race-entry tier inventories,
// so the event-level capacity field is hidden + always saved as null.
const isRaceEvent = computed(() =>
    typeOptions.value.find(t => t.id === form.value.eventTypeId)?.code === 'race'
)

watch(isRaceEvent, (race) => {
    if (race) form.value.capacity = null
    else if (activeTab.value !== 'info') activeTab.value = 'info'
})

const saveLabel = computed(() => {
    if (editing.value) return 'Save changes'
    return isRaceEvent.value ? 'Save & add classes' : 'Create event'
})

const form = ref({
    eventTypeId: '',
    title: '',
    description: '' as string | null,
    startsLocal: '',
    endsLocal: '',
    allDay: false,
    capacity: null as number | null,
    locationLabel: '' as string | null,
    status: 'scheduled' as 'scheduled' | 'cancelled',
    requiresRiderWaiver: true,
    requiresSpectatorWaiver: false,
    spectatorWaiverId: null as string | null,
    racerWaiverId: null as string | null,
    imageUrl: null as string | null,
    eligiblePassProductIds: [] as string[],
    eligibleExtras: [] as { productId: string; inventory: number | null }[],
    schedule: [] as { time: string; label: string }[],
})

// Tenant waiver list — used by both spectator and racer dropdowns. Only active
// waivers that cover the event-end date show up; expired-before-event ones get
// hidden so admins can't pin events to soon-to-die waivers.
const waiverService = new WaiverService()
const waivers = ref<WaiverDto[]>([])
const eventEndsMs = computed(() => {
    if (!form.value.endsLocal) return null
    const ts = new Date(localToUtc(form.value.endsLocal)).getTime()
    return isNaN(ts) ? null : ts
})
function waiverCoversEventEnd(w: WaiverDto): boolean {
    if (!w.expiresAtUtc) return true
    const exp = new Date(w.expiresAtUtc).getTime()
    if (isNaN(exp)) return true
    const end = eventEndsMs.value
    if (end === null) return exp > Date.now()
    return exp >= end
}
function waiverItem(w: WaiverDto) {
    return {
        value: w.id,
        title: w.expiresAtUtc
            ? `${w.name} (expires ${new Date(w.expiresAtUtc).toLocaleDateString()})`
            : w.name,
    }
}
const eligibleWaivers = computed(() => waivers.value.filter(w => w.isActive && waiverCoversEventEnd(w)))
const spectatorWaiverOptions = computed(() => eligibleWaivers.value.map(waiverItem))
const racerWaiverOptions = computed(() => eligibleWaivers.value.map(waiverItem))
const spectatorWaiverInvalid = computed(() => {
    if (!form.value.spectatorWaiverId) return false
    const w = waivers.value.find(x => x.id === form.value.spectatorWaiverId)
    return !!w && !waiverCoversEventEnd(w)
})
const racerWaiverInvalid = computed(() => {
    if (!form.value.racerWaiverId) return false
    const w = waivers.value.find(x => x.id === form.value.racerWaiverId)
    return !!w && !waiverCoversEventEnd(w)
})

// Tenant's full active+inactive pass catalog. Loaded once on mount so the
// eligibility section can render its checkbox list without per-edit fetches.
const passService = new PassService()
const passProducts = ref<PassProduct[]>([])

const extraService = new ExtraService()
const extraProducts = ref<ExtraProduct[]>([])

function extraEnabled(productId: string): boolean {
    return form.value.eligibleExtras.some(e => e.productId === productId)
}
function extraInventory(productId: string): number | null {
    return form.value.eligibleExtras.find(e => e.productId === productId)?.inventory ?? null
}
function toggleExtra(productId: string, on: boolean | null) {
    if (on) {
        if (!extraEnabled(productId)) form.value.eligibleExtras.push({ productId, inventory: null })
    } else {
        form.value.eligibleExtras = form.value.eligibleExtras.filter(e => e.productId !== productId)
    }
}
function setExtraInventory(productId: string, val: string | number) {
    const target = form.value.eligibleExtras.find(e => e.productId === productId)
    if (!target) return
    const n = typeof val === 'number' ? val : parseInt(val, 10)
    target.inventory = Number.isFinite(n) && n > 0 ? n : null
}

const imageFile = ref<File | File[] | null>(null)
const uploadingImage = ref(false)

const apiUrl: string = (import.meta as any).env?.VITE_API_ENDPOINT ?? ''
function apiOrigin(): string {
    try { return new URL(apiUrl, window.location.origin).origin } catch { return '' }
}
function absoluteUrl(url: string | null | undefined): string {
    if (!url) return ''
    if (/^https?:\/\//i.test(url)) return url
    return `${apiOrigin()}${url}`
}

function tz(): string { return branding.timezone || 'UTC' }
function localToUtc(localValue: string): string { return dayjs.tz(localValue, tz()).utc().toISOString() }
function utcToLocalInput(utc: string): string { return dayjs.utc(utc).tz(tz()).format('YYYY-MM-DDTHH:mm') }

onMounted(async () => {
    const types = await eventTypeService.list()
    typeOptions.value = (types.data as any).data
    if (typeOptions.value.length > 0 && !form.value.eventTypeId) {
        form.value.eventTypeId = typeOptions.value[0].id
    }
    try {
        const r = await passService.listForAdmin()
        passProducts.value = (r.data as any).data
    } catch { passProducts.value = [] }
    try {
        const r = await waiverService.listAdmin()
        waivers.value = (r.data as any).data
    } catch { waivers.value = [] }
    if (branding.extrasEnabled) {
        try {
            const r = await extraService.listForAdmin()
            extraProducts.value = ((r.data as any).data as ExtraProduct[]).filter(p => p.isActive)
        } catch { extraProducts.value = [] }
    }
})

// When there's exactly one active product and the eligibility list is empty,
// auto-select it so the admin doesn't have to remember to check the only box.
function defaultEligibility(existing: string[]): string[] {
    if (existing.length > 0) return [...existing]
    const actives = passProducts.value.filter(p => p.isActive)
    return actives.length === 1 ? [actives[0].id] : []
}

// Re-seed the form whenever the dialog opens. Source of truth priority:
// - `event` set → edit mode prefilled from the row.
// - `duplicateFrom` set → create mode prefilled from the source with start/end +7d.
// - neither → fresh create-mode defaults.
watch(() => props.open, (open) => {
    if (!open) return
    if (props.event) {
        const row = props.event
        editing.value = row
        activeTab.value = 'info'
        form.value = {
            eventTypeId: row.eventTypeId,
            title: row.title,
            description: row.description ?? '',
            startsLocal: utcToLocalInput(row.startsAtUtc),
            endsLocal: utcToLocalInput(row.endsAtUtc),
            allDay: row.allDay,
            capacity: row.capacity,
            locationLabel: row.locationLabel ?? '',
            status: row.status,
            requiresRiderWaiver: row.requiresRiderWaiver,
            requiresSpectatorWaiver: row.requiresSpectatorWaiver,
            spectatorWaiverId: row.spectatorWaiverId ?? null,
            racerWaiverId: row.racerWaiverId ?? null,
            imageUrl: row.imageUrl,
            eligiblePassProductIds: defaultEligibility(
                (row.eligiblePasses ?? []).map(p => p.id)),
            eligibleExtras: (row.eligibleExtras ?? []).map(e => ({
                productId: e.productId,
                inventory: e.inventory,
            })),
            schedule: (row.schedule ?? []).map(s => ({ time: s.time, label: s.label })),
        }
    } else if (props.duplicateFrom) {
        seedFromDuplicate(props.duplicateFrom)
    } else {
        editing.value = null
        activeTab.value = 'info'
        const start = dayjs().tz(tz()).startOf('hour').add(1, 'hour')
        form.value = {
            eventTypeId: typeOptions.value[0]?.id ?? '',
            title: '',
            description: '',
            startsLocal: start.format('YYYY-MM-DDTHH:mm'),
            endsLocal: start.add(2, 'hour').format('YYYY-MM-DDTHH:mm'),
            allDay: false,
            capacity: null,
            locationLabel: '',
            status: 'scheduled',
            requiresRiderWaiver: true,
            requiresSpectatorWaiver: false,
            spectatorWaiverId: null,
            racerWaiverId: null,
            imageUrl: null,
            eligiblePassProductIds: defaultEligibility([]),
            eligibleExtras: [],
            schedule: [],
        }
    }
    imageFile.value = null
})

function seedFromDuplicate(src: EventDto) {
    editing.value = null
    activeTab.value = 'info'
    const start = dayjs.utc(src.startsAtUtc).tz(tz()).add(7, 'day')
    const end = dayjs.utc(src.endsAtUtc).tz(tz()).add(7, 'day')
    form.value = {
        eventTypeId: src.eventTypeId,
        title: src.title,
        description: src.description ?? '',
        startsLocal: start.format('YYYY-MM-DDTHH:mm'),
        endsLocal: end.format('YYYY-MM-DDTHH:mm'),
        allDay: src.allDay,
        capacity: src.capacity,
        locationLabel: src.locationLabel ?? '',
        status: src.status,
        requiresRiderWaiver: src.requiresRiderWaiver,
        requiresSpectatorWaiver: src.requiresSpectatorWaiver,
        spectatorWaiverId: src.spectatorWaiverId ?? null,
        racerWaiverId: src.racerWaiverId ?? null,
        eligiblePassProductIds: defaultEligibility(
            (src.eligiblePasses ?? []).map(p => p.id)),
        eligibleExtras: (src.eligibleExtras ?? []).map(e => ({
            productId: e.productId,
            inventory: e.inventory,
        })),
        schedule: (src.schedule ?? []).map(s => ({ time: s.time, label: s.label })),
        imageUrl: src.imageUrl,
    }
    imageFile.value = null
}

async function onImageSelected(v: File | File[] | null) {
    const f = Array.isArray(v) ? (v[0] ?? null) : v
    if (!f) return
    try {
        uploadingImage.value = true
        const r = await eventService.uploadImage(f)
        form.value.imageUrl = r.data.data.imageUrl
    } catch (err: any) {
        emit('flash', err.response?.data?.error || 'Image upload failed.', 'error')
    } finally {
        uploadingImage.value = false
        imageFile.value = null
    }
}

async function save() {
    try {
        saving.value = true
        const body = {
            eventTypeId: form.value.eventTypeId,
            title: form.value.title.trim(),
            description: form.value.description && form.value.description.trim().length > 0 ? form.value.description : null,
            startsAtUtc: localToUtc(form.value.startsLocal),
            endsAtUtc: localToUtc(form.value.endsLocal),
            allDay: form.value.allDay,
            capacity: isRaceEvent.value ? null : (form.value.capacity || null),
            locationLabel: form.value.locationLabel && form.value.locationLabel.trim().length > 0 ? form.value.locationLabel : null,
            status: form.value.status,
            requiresRiderWaiver: form.value.requiresRiderWaiver,
            requiresSpectatorWaiver: form.value.requiresSpectatorWaiver,
            spectatorWaiverId: form.value.requiresSpectatorWaiver ? form.value.spectatorWaiverId : null,
            racerWaiverId: form.value.requiresRiderWaiver ? form.value.racerWaiverId : null,
            imageUrl: form.value.imageUrl,
            // Race events use tier-based admissions, not pass reservations —
            // always send an empty list to clear any stray entries.
            eligiblePassProductIds: isRaceEvent.value ? [] : form.value.eligiblePassProductIds,
            eligibleExtras: form.value.eligibleExtras,
            schedule: form.value.schedule
                .map(s => ({ time: s.time.trim(), label: s.label.trim() }))
                .filter(s => s.time.length > 0 || s.label.length > 0),
        }
        if (editing.value) {
            await eventService.update(editing.value.id, body)
            emit('saved', editing.value)
            emit('update:open', false)
            emit('flash', 'Event saved.', 'success')
        } else {
            // Create. For race events, keep the dialog open and jump to the Tickets
            // tab so the admin can immediately add race entries — those events
            // basically always need tiers. For other types (open ride days, etc.)
            // tiers are optional, so just save and close.
            const r = await eventService.create(body)
            const created = (r.data as any).data as EventDto
            const isRaceEvent = typeOptions.value.find(t => t.id === created.eventTypeId)?.code === 'race'
            if (isRaceEvent) {
                editing.value = created
                activeTab.value = 'race'
                emit('saved', created)
                emit('flash', 'Event saved — add race classes below.', 'success')
            } else {
                emit('saved', created)
                emit('update:open', false)
                emit('flash', 'Event saved.', 'success')
            }
        }
    } catch (err: any) {
        emit('flash', err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function remove() {
    if (!editing.value) return
    if (!confirm(`Delete "${editing.value.title}"? Any tickets and race entries are removed too.`)) return
    try {
        await eventService.delete(editing.value.id)
        emit('deleted', editing.value.id)
        emit('update:open', false)
        emit('flash', 'Event deleted.', 'success')
    } catch (err: any) {
        emit('flash', err.response?.data?.error || 'Delete failed.', 'error')
    }
}

function dup() {
    if (!editing.value) return
    const src = editing.value
    seedFromDuplicate(src)
    emit('flash', `Duplicating "${src.title}" — adjust and save to create the copy.`, 'success')
}
</script>

<style scoped>
.event-image-preview {
    width: 100px;
    height: 70px;
    border-radius: 6px;
    background-size: cover;
    background-position: center;
    border: 1px solid rgba(0, 0, 0, 0.1);
    display: flex;
    align-items: center;
    justify-content: center;
}
.event-image-preview.empty {
    background: rgba(0, 0, 0, 0.04);
}
</style>
