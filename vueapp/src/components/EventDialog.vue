<template>
    <v-dialog :model-value="open" @update:model-value="$emit('update:open', $event)" fullscreen>
        <v-card class="d-flex flex-column" style="height: 100%">
            <v-card-title class="d-flex align-center">
                <span>{{ editing ? 'Edit Event' : 'Add Event' }}</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" @click="$emit('update:open', false)"></v-btn>
            </v-card-title>
            <!-- Tabs: General | Race Classes (race events only) | Add-ons | Waivers.
                 Tickets are gone — use a Gate Fee add-on for spectator admissions. -->
            <v-tabs v-model="activeTab" color="primary" grow style="flex: 0 0 auto">
                <v-tab value="info">Details</v-tab>
                <v-tab value="entry">Entry &amp; Add-ons</v-tab>
                <v-tab value="waivers">Waivers</v-tab>
            </v-tabs>

            <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                <v-window v-model="activeTab" class="mt-4">
                    <v-window-item value="info">
                        <p class="text-caption text-medium-emphasis mt-2 mb-0">
                            Set the basics here. Use the <strong>Entry &amp; Add-ons</strong> tab to choose who can attend and
                            set up rider entry, gate fees, and add-ons, and the <strong>Waivers</strong> tab for required signatures.
                        </p>
                        <v-row class="mt-1">
                            <v-col cols="12" md="8">
                                <v-text-field v-model="form.title" label="Title" density="compact"
                                    :rules="[v => !!(v && v.trim()) || 'Title is required']"></v-text-field>
                            </v-col>
                            <v-col cols="12" md="4">
                                <v-select v-model="form.eventTypeId" :items="typeOptions" item-title="name" item-value="id"
                                    label="Type" density="compact" :rules="[v => !!v || 'Type is required']"></v-select>
                            </v-col>
                        </v-row>
                        <v-row>
                            <v-col cols="12" md="6">
                                <v-text-field v-model="form.startsLocal" type="datetime-local" label="Starts" density="compact"
                                    :rules="[v => !!v || 'Start is required']"></v-text-field>
                            </v-col>
                            <v-col cols="12" md="6">
                                <v-text-field v-model="form.endsLocal" type="datetime-local" label="Ends" density="compact"
                                    :rules="[v => !!v || 'End is required']"></v-text-field>
                            </v-col>
                        </v-row>
                        <v-row>
                            <v-col cols="12" :md="isRaceEvent ? 6 : 4">
                                <v-checkbox v-model="form.allDay" label="All day"></v-checkbox>
                            </v-col>
                            <v-col v-if="!isRaceEvent" cols="12" md="4">
                                <v-text-field v-model.number="form.capacity" type="number" label="Capacity (blank = unlimited)" density="compact"
                                    :rules="[v => v == null || v === '' || Number(v) >= 1 || 'Capacity must be at least 1']"></v-text-field>
                            </v-col>
                            <v-col cols="12" :md="isRaceEvent ? 6 : 4">
                                <v-select v-model="form.status" :items="['scheduled','cancelled']" label="Status" density="compact"></v-select>
                            </v-col>
                        </v-row>
                        <p v-if="isRaceEvent" class="text-caption text-medium-emphasis mt-n2 mb-2">
                            Race-event capacity is set by the inventory on each race entry below.
                        </p>
                        <v-text-field v-model="form.locationLabel" label="Location" density="compact" class="mt-4"></v-text-field>

                        <label class="text-subtitle-2 d-block mt-6 mb-1">Event details</label>
                        <p class="text-caption text-medium-emphasis mb-2">
                            Each row shows up as its own bullet point in the "Event Details" list on the
                            public event page.
                        </p>
                        <div v-for="(row, i) in form.details" :key="i" class="d-flex align-center ga-2 mb-2">
                            <v-text-field v-model="row.text" label="Detail"
                                placeholder="All ages welcome" density="compact" hide-details class="mt-4"></v-text-field>
                            <v-btn icon="mdi-close" variant="text" size="small"
                                @click="form.details.splice(i, 1)"></v-btn>
                        </div>
                        <v-btn variant="tonal" size="small" prepend-icon="mdi-plus" class="mt-2"
                            @click="form.details.push({ text: '' })">Add Event Detail Row</v-btn>

                        <label class="text-subtitle-2 d-block mt-4 mb-2">Event schedule (optional)</label>
                        <p class="text-caption text-medium-emphasis mb-2">
                            Shown as a schedule on the public event page. Add rows like "7:00 AM" / "Gates open &amp; check-in".
                        </p>
                        <div v-for="(row, i) in form.schedule" :key="i" class="d-flex align-center ga-2 mb-2 mt-4">
                            <v-text-field v-model="row.time" label="Time" placeholder="7:00 AM"
                                density="compact" hide-details style="max-width: 150px"></v-text-field>
                            <v-text-field v-model="row.label" label="What's happening"
                                placeholder="Gates open & check-in" density="compact" hide-details></v-text-field>
                            <v-btn icon="mdi-close" variant="text" size="small"
                                @click="form.schedule.splice(i, 1)"></v-btn>
                        </div>
                        <v-btn variant="tonal" size="small" prepend-icon="mdi-plus" class="mt-2"
                            @click="form.schedule.push({ time: '', label: '' })">Add Event Schedule Row</v-btn>

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

                    <v-window-item value="entry">
                        <!-- Who can attend: drives the entry options below + the Waivers tab. -->
                        <label class="text-subtitle-2 d-block mb-1">Who can attend</label>
                        <p class="text-caption text-medium-emphasis mb-1">
                            Pick at least one audience. Each enabled audience gets its own entry options below
                            and waivers on the Waivers tab.
                        </p>
                        <v-switch v-model="form.allowsRiders" label="Allow riders"
                            color="primary" density="compact" hide-details class="ml-2"></v-switch>
                        <v-switch v-model="form.allowsSpectators" label="Allow spectators"
                            color="primary" density="compact" hide-details class="ml-2"></v-switch>
                        <v-alert v-if="!form.allowsRiders && !form.allowsSpectators"
                            type="warning" variant="tonal" density="compact" class="mt-2 mb-1">
                            Pick at least one audience — riders, spectators, or both.
                        </v-alert>

                        <!-- Rider entry: race classes for race events. Non-race riders pay via a
                             rider gate fee (configured in the Gate fees section below). -->
                        <template v-if="form.allowsRiders && isRaceEvent">
                            <v-divider class="my-5"></v-divider>
                            <label class="text-subtitle-2 d-block mb-1">Race classes</label>
                            <p class="text-caption text-medium-emphasis mb-2">
                                Riders pay to enter a class (Open, Beginner, Pro, etc.). You can attach
                                bundled coupons that get auto-issued at purchase.
                            </p>
                            <TicketTiersList ref="raceClassesList" :event-id="editing?.id ?? null" kind="race_entry" />
                        </template>

                        <!-- Gate fees: first-class per-event tiers for riders and spectators. A required
                             rider gate fee forces "race class + one rider gate fee"; a required spectator
                             gate fee is how spectators are admitted. $0 allowed for free kids gates. -->
                        <v-divider class="my-5"></v-divider>
                        <label class="text-subtitle-2 d-block mb-1">Gate fees</label>
                        <p class="text-caption text-medium-emphasis mb-2">
                            Facility gate fees for riders and spectators. Mark one <strong>required</strong> to force it —
                            riders then pay a race class plus one rider gate fee. Use $0 for a free kids gate.
                        </p>
                        <TicketTiersList ref="gateFeesList" :event-id="editing?.id ?? null" kind="gate_fee" />

                        <!-- General add-ons: camping, parking, merch, etc. Gate fees live above, not here. -->
                        <template v-if="branding.extrasEnabled">
                            <v-divider class="my-5"></v-divider>
                            <label class="text-subtitle-2 d-block mb-1">Add-ons</label>
                            <p class="text-caption text-medium-emphasis mb-2">
                                Optional camping, parking, pit-vehicle, merch, etc. Inventory is per-event; leave blank
                                for unlimited.
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
                        </template>
                    </v-window-item>

                    <v-window-item value="waivers">
                        <p class="text-caption text-medium-emphasis mb-3">
                            Only the audiences you allowed on the Details tab appear here. Each can be required to
                            sign its own waiver, or none. Leave a waiver blank to fall back to the tenant default.
                        </p>

                        <v-switch v-if="form.allowsRiders" v-model="form.requiresRiderWaiver"
                            label="Require Rider Signed Waiver"
                            color="primary" density="compact" hide-details class="mb-2 ml-2"></v-switch>
                        <template v-if="form.allowsRiders && form.requiresRiderWaiver">
                            <v-select v-model="form.racerWaiverId"
                                :items="racerWaiverOptions" item-title="title" item-value="value"
                                :label="isRaceEvent ? 'Racer waiver' : 'Rider waiver'" density="compact" clearable hide-details
                                :hint="isRaceEvent ? 'Signed by riders entering a race class. Leave blank for tenant default.' : 'Signed by riders entering this event. Leave blank for tenant default.'"
                                persistent-hint class="mb-3"></v-select>
                            <v-alert v-if="racerWaiverInvalid"
                                type="warning" variant="tonal" density="compact" class="mb-3">
                                The selected rider waiver expires before this event ends. Pick another or extend its expiration.
                            </v-alert>
                        </template>

                        <v-divider v-if="form.allowsRiders && form.allowsSpectators" class="my-4"></v-divider>

                        <v-switch v-if="form.allowsSpectators" v-model="form.requiresSpectatorWaiver"
                            label="Require Spectator Signed Waiver"
                            color="primary" density="compact" hide-details class="mb-2 ml-2"></v-switch>
                        <template v-if="form.allowsSpectators && form.requiresSpectatorWaiver">
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
                <v-btn color="primary" :loading="saving"
                    :disabled="!form.allowsRiders && !form.allowsSpectators" @click="save">
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
import { ExtraService, type ExtraProduct } from '@/services/ExtraService'
import { WaiverService, type WaiverDto } from '@/services/WaiverService'
import { branding } from '@/stores/branding'
import TicketTiersList from '@/components/TicketTiersList.vue'
import { useConfirm } from '@/composables/useConfirm'

const confirm = useConfirm()

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
const activeTab = ref<'info' | 'entry' | 'waivers'>('info')

// Child tier editors. In create mode they buffer rows locally; on save we create the
// event then flush each editor's buffered tiers to it (persistTo).
const raceClassesList = ref<{ persistTo: (eventId: string) => Promise<void> } | null>(null)
const gateFeesList = ref<{ persistTo: (eventId: string) => Promise<void> } | null>(null)

// Race events get their capacity from the sum of race-entry tier inventories,
// so the event-level capacity field is hidden + always saved as null.
const isRaceEvent = computed(() =>
    typeOptions.value.find(t => t.id === form.value.eventTypeId)?.code === 'race'
)

watch(isRaceEvent, (race) => {
    // Race events take capacity from the sum of race-class inventories.
    if (race) form.value.capacity = null
})

const saveLabel = computed(() => editing.value ? 'Save changes' : 'Create event')

const form = ref({
    eventTypeId: '',
    title: '',
    details: [] as { text: string }[],
    startsLocal: '',
    endsLocal: '',
    allDay: false,
    capacity: null as number | null,
    locationLabel: '' as string | null,
    status: 'scheduled' as 'scheduled' | 'cancelled',
    allowsRiders: true,
    allowsSpectators: false,
    requiresRiderWaiver: true,
    requiresSpectatorWaiver: false,
    spectatorWaiverId: null as string | null,
    racerWaiverId: null as string | null,
    imageUrl: null as string | null,
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
        const r = await waiverService.listAdmin()
        waivers.value = (r.data as any).data
    } catch { waivers.value = [] }
    if (branding.extrasEnabled) {
        try {
            const r = await extraService.listForAdmin()
            // Gate fees are first-class per-event tiers now, not add-ons, so the legacy
            // gate_fee extra kind is hidden from the add-ons list.
            extraProducts.value = ((r.data as any).data as ExtraProduct[])
                .filter(p => p.isActive && (p as any).kind !== 'gate_fee')
        } catch { extraProducts.value = [] }
    }
})

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
            details: descriptionToRows(row.description),
            startsLocal: utcToLocalInput(row.startsAtUtc),
            endsLocal: utcToLocalInput(row.endsAtUtc),
            allDay: row.allDay,
            capacity: row.capacity,
            locationLabel: row.locationLabel ?? '',
            status: row.status,
            allowsRiders: row.allowsRiders,
            allowsSpectators: row.allowsSpectators,
            requiresRiderWaiver: row.requiresRiderWaiver,
            requiresSpectatorWaiver: row.requiresSpectatorWaiver,
            spectatorWaiverId: row.spectatorWaiverId ?? null,
            racerWaiverId: row.racerWaiverId ?? null,
            imageUrl: row.imageUrl,
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
            details: [],
            startsLocal: start.format('YYYY-MM-DDTHH:mm'),
            endsLocal: start.add(2, 'hour').format('YYYY-MM-DDTHH:mm'),
            allDay: false,
            capacity: null,
            locationLabel: '',
            status: 'scheduled',
            allowsRiders: true,
            allowsSpectators: false,
            requiresRiderWaiver: true,
            requiresSpectatorWaiver: false,
            spectatorWaiverId: null,
            racerWaiverId: null,
            imageUrl: null,
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
        details: descriptionToRows(src.description),
        startsLocal: start.format('YYYY-MM-DDTHH:mm'),
        endsLocal: end.format('YYYY-MM-DDTHH:mm'),
        allDay: src.allDay,
        capacity: src.capacity,
        locationLabel: src.locationLabel ?? '',
        status: src.status,
        allowsRiders: src.allowsRiders,
        allowsSpectators: src.allowsSpectators,
        requiresRiderWaiver: src.requiresRiderWaiver,
        requiresSpectatorWaiver: src.requiresSpectatorWaiver,
        spectatorWaiverId: src.spectatorWaiverId ?? null,
        racerWaiverId: src.racerWaiverId ?? null,
        eligibleExtras: (src.eligibleExtras ?? []).map(e => ({
            productId: e.productId,
            inventory: e.inventory,
        })),
        schedule: (src.schedule ?? []).map(s => ({ time: s.time, label: s.label })),
        imageUrl: src.imageUrl,
    }
    imageFile.value = null
}

// Event "details" round-trip: stored as a newline-joined string in `description`
// (the public page renders each line as a bullet), but edited here as discrete rows.
function descriptionToRows(desc: string | null | undefined): { text: string }[] {
    if (!desc) return []
    return desc.split('\n').map(text => ({ text }))
}
function rowsToDescription(rows: { text: string }[]): string | null {
    const joined = rows.map(r => r.text.trim()).filter(t => t.length > 0).join('\n')
    return joined.length > 0 ? joined : null
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
    if (!form.value.title?.trim()) {
        emit('flash', 'Title is required.', 'error')
        activeTab.value = 'info'
        return
    }
    if (!form.value.eventTypeId) {
        emit('flash', 'Pick an event type.', 'error')
        activeTab.value = 'info'
        return
    }
    if (!form.value.startsLocal || !form.value.endsLocal) {
        emit('flash', 'Start and end date/time are required.', 'error')
        activeTab.value = 'info'
        return
    }
    if (!form.value.allowsRiders && !form.value.allowsSpectators) {
        emit('flash', 'Pick at least one audience — riders, spectators, or both.', 'error')
        activeTab.value = 'info'
        return
    }
    try {
        saving.value = true
        const body = {
            eventTypeId: form.value.eventTypeId,
            title: form.value.title.trim(),
            description: rowsToDescription(form.value.details),
            startsAtUtc: localToUtc(form.value.startsLocal),
            endsAtUtc: localToUtc(form.value.endsLocal),
            allDay: form.value.allDay,
            capacity: isRaceEvent.value ? null : (form.value.capacity || null),
            locationLabel: form.value.locationLabel && form.value.locationLabel.trim().length > 0 ? form.value.locationLabel : null,
            status: form.value.status,
            allowsRiders: form.value.allowsRiders,
            allowsSpectators: form.value.allowsSpectators,
            // Waivers only apply to an allowed audience.
            requiresRiderWaiver: form.value.allowsRiders && form.value.requiresRiderWaiver,
            requiresSpectatorWaiver: form.value.allowsSpectators && form.value.requiresSpectatorWaiver,
            spectatorWaiverId: (form.value.allowsSpectators && form.value.requiresSpectatorWaiver) ? form.value.spectatorWaiverId : null,
            racerWaiverId: (form.value.allowsRiders && form.value.requiresRiderWaiver) ? form.value.racerWaiverId : null,
            imageUrl: form.value.imageUrl,
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
            // Create the event, then flush any race classes / gate fees the admin added
            // in the Entry tab before saving (the editors buffer them in create mode).
            const r = await eventService.create(body)
            const created = (r.data as any).data as EventDto
            await raceClassesList.value?.persistTo(created.id)
            await gateFeesList.value?.persistTo(created.id)
            emit('saved', created)
            emit('update:open', false)
            emit('flash', 'Event saved.', 'success')
        }
    } catch (err: any) {
        emit('flash', saveErrorMessage(err), 'error')
    } finally {
        saving.value = false
    }
}

// Surface a readable reason from either our own { error } responses or ASP.NET's
// model-validation 400 ({ errors: { Field: [msg, ...] } }), instead of a generic
// "Save failed." — so every required/invalid field gives the user something useful.
function saveErrorMessage(err: any): string {
    const data = err?.response?.data
    if (data?.error) return data.error
    const errors = data?.errors
    if (errors && typeof errors === 'object') {
        const first = Object.values(errors).flat().find((m: any) => typeof m === 'string')
        if (typeof first === 'string') return first as string
    }
    return 'Save failed. Please check the highlighted fields and try again.'
}

async function remove() {
    if (!editing.value) return
    const ok = await confirm({
        title: 'Delete event?',
        message: `Delete "${editing.value.title}"? Any tickets and race entries are removed too.`,
        confirmText: 'Delete', confirmColor: 'error',
    })
    if (!ok) return
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

/* Active-tab indicator.
   Vuetify's native .v-tab__slider is sized/positioned by VSlideGroup's layout
   measurement. The Details tab is the only one whose content scrolls, so its scrollbar
   appears/disappears as you switch tabs, shifting the layout the slider was measured
   against and leaving the Details underline missing. Replace the measured slider with a
   plain class-driven border that needs no measurement and so is scroll/resize-proof. */
:deep(.v-tab__slider) {
    display: none;
}
:deep(.v-tab) {
    border-bottom: 2px solid transparent;
}
:deep(.v-tab.v-tab--selected) {
    border-bottom-color: currentColor;
}
</style>
