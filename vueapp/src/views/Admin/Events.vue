<template>
    <v-container>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <h1 class="text-h4">Events</h1>
            <v-spacer></v-spacer>
            <v-text-field v-model="rangeFrom" type="date" label="From" density="compact" hide-details style="max-width: 180px"></v-text-field>
            <v-text-field v-model="rangeTo" type="date" label="To" density="compact" hide-details style="max-width: 180px"></v-text-field>
            <v-btn variant="text" @click="load">Refresh</v-btn>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">Add Event</v-btn>
        </div>

        <p class="text-caption text-medium-emphasis mb-2">
            Times displayed in tenant timezone: <strong>{{ branding.timezone }}</strong>. Input fields are interpreted in that zone too.
        </p>

        <EventCalendar v-model:monthStart="calendarMonth" :events="calendarEvents" :blackouts="calendarBlackouts"
            :timezone="tz()" @select="openEdit" />

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th style="width: 180px">Start</th>
                        <th style="width: 180px">End</th>
                        <th>Title</th>
                        <th style="width: 160px">Type</th>
                        <th style="width: 90px">Capacity</th>
                        <th style="width: 120px">Reserved</th>
                        <th style="width: 110px">Status</th>
                        <th style="width: 220px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="row in rows" :key="row.id">
                        <td>{{ formatInTenant(row.startsAtUtc) }}</td>
                        <td>{{ formatInTenant(row.endsAtUtc) }}</td>
                        <td>
                            <div>{{ row.title }}</div>
                            <div v-if="row.locationLabel" class="text-caption text-medium-emphasis">{{ row.locationLabel }}</div>
                        </td>
                        <td>
                            <v-chip size="small" :style="{ backgroundColor: row.eventTypeColor, color: '#fff' }">
                                {{ row.eventTypeName }}
                            </v-chip>
                        </td>
                        <td>{{ row.capacity ?? '—' }}</td>
                        <td>
                            <template v-if="row.capacity">
                                <v-chip size="small" :color="reservedChipColor(row)" variant="tonal">
                                    {{ row.spotsReserved ?? 0 }} / {{ row.capacity }}
                                </v-chip>
                            </template>
                            <span v-else class="text-medium-emphasis">—</span>
                        </td>
                        <td>{{ row.status }}</td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" @click="openShare(row)">Share</v-btn>
                            <v-btn variant="text" size="small" @click="openEdit(row)">Edit</v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && !loadError && rows.length === 0">
                        <td colspan="8" class="text-center text-medium-emphasis py-8">No events in this range.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <EventDialog v-model:open="dialog" :event="editing" @saved="onSaved" @deleted="onDeleted" @flash="flash" />

        <v-dialog v-model="shareOpen" max-width="560">
            <v-card v-if="sharing">
                <v-card-title class="d-flex align-center">
                    <span>Share event</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="shareOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div class="text-subtitle-1 mb-1">{{ sharing.title }}</div>
                    <div class="text-caption text-medium-emphasis mb-3">
                        Direct link: <a :href="shareUrl" target="_blank" rel="noopener">{{ shareUrl }}</a>
                    </div>
                    <p v-if="!hasAnyTenantSocial" class="text-caption text-medium-emphasis mb-3">
                        Add Facebook, Instagram, TikTok, or YouTube URLs in
                        <router-link to="/Admin/Settings/Branding">Branding settings</router-link>
                        to limit this list to your registered platforms. For now, all options are shown.
                    </p>
                    <SocialShare :url="shareUrl" :title="shareTitle" :text="shareText"
                        :platforms="tenantSharePlatforms" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="shareOpen = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { EventService, type EventDto } from '@/services/EventService'
import { BlackoutService, type BlackoutDto } from '@/services/BlackoutService'
import { branding } from '@/stores/branding'
import EventDialog from '@/components/EventDialog.vue'
import EventCalendar from '@/components/EventCalendar.vue'
import SocialShare, { type SocialSharePlatform } from '@/components/SocialShare.vue'

const route = useRoute()
const router = useRouter()

const eventService = new EventService()
const blackoutService = new BlackoutService()

const today = dayjs()
// Default window: start of this month → 6 months out. Most upcoming-event browsing
// happens within ~half a year; admins can widen via the date pickers if needed.
const rangeFrom = ref(today.startOf('month').format('YYYY-MM-DD'))
const rangeTo = ref(today.startOf('month').add(6, 'month').format('YYYY-MM-DD'))

const rows = ref<EventDto[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
const dialog = ref(false)
const editing = ref<EventDto | null>(null)

// Calendar: its own month + event set so navigating months doesn't disturb the
// date-range table below. Always loads the full visible 6-week grid.
const calendarMonth = ref(today.startOf('month').format('YYYY-MM-DD'))
const calendarEvents = ref<EventDto[]>([])
const calendarBlackouts = ref<BlackoutDto[]>([])

const shareOpen = ref(false)
const sharing = ref<EventDto | null>(null)
const shareUrl = computed(() =>
    sharing.value ? `${window.location.origin}/Event/${sharing.value.id}` : '')
const shareTitle = computed(() =>
    sharing.value ? `${sharing.value.title} — ${branding.displayName}` : '')
const shareText = computed(() => {
    if (!sharing.value) return ''
    const date = dayjs.utc(sharing.value.startsAtUtc).tz(tz()).format('MMM D, YYYY')
    return `Check out ${sharing.value.title} on ${date} at ${branding.displayName}.`
})

// Restrict the visible buttons to socials the tenant has actually registered.
// Generic non-account-bound platforms (X, LinkedIn, WhatsApp, Reddit, Email)
// are always available since they only need the user's own login. If the tenant
// hasn't registered any account-tied platform yet, fall back to showing
// everything so the dialog isn't useless.
const hasAnyTenantSocial = computed(() => !!(branding.socialFacebookUrl
    || branding.socialInstagramUrl || branding.socialTiktokUrl || branding.socialYoutubeUrl))
const tenantSharePlatforms = computed<SocialSharePlatform[] | undefined>(() => {
    if (!hasAnyTenantSocial.value) return undefined
    const list: SocialSharePlatform[] = ['twitter', 'linkedin', 'whatsapp', 'reddit', 'email']
    if (branding.socialFacebookUrl) list.unshift('facebook')
    if (branding.socialInstagramUrl) list.push('instagram')
    if (branding.socialTiktokUrl) list.push('tiktok')
    return list
})

function openShare(row: EventDto) {
    sharing.value = row
    shareOpen.value = true
}

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(() => { load(); loadCalendar() })
watch(calendarMonth, loadCalendar)

function tz(): string { return branding.timezone || 'UTC' }

async function loadCalendar() {
    // Cover the whole 6-week grid (the Sunday on/before the 1st, 42 days out) so events
    // bleeding in from adjacent months still show in their trailing/leading cells.
    const gridStart = dayjs(calendarMonth.value).startOf('month').startOf('week')
    const gridEnd = gridStart.add(42, 'day')
    const fromUtc = dayjs.tz(gridStart.format('YYYY-MM-DD') + 'T00:00', tz()).utc().toISOString()
    const toUtc = dayjs.tz(gridEnd.format('YYYY-MM-DD') + 'T00:00', tz()).utc().toISOString()
    try {
        const [r, b] = await Promise.all([
            eventService.list(fromUtc, toUtc),
            blackoutService.list(fromUtc, toUtc),
        ])
        calendarEvents.value = (r.data as any).data
        calendarBlackouts.value = (b.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error ?? 'Couldn’t load the calendar. Refresh to try again.', 'error')
    }
}

function formatInTenant(utc: string): string {
    return dayjs.utc(utc).tz(tz()).format('YYYY-MM-DD HH:mm')
}

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const fromUtc = dayjs.tz(rangeFrom.value + 'T00:00', tz()).utc().toISOString()
        const toUtc = dayjs.tz(rangeTo.value + 'T00:00', tz()).utc().toISOString()
        const r = await eventService.list(fromUtc, toUtc)
        rows.value = (r.data as any).data

        // Deep-link from elsewhere (e.g. dashboard "Edit" button): if ?edit=<id>
        // is on the URL, open the editor for that event. We widen the date
        // window if needed so the row is in the in-memory list. The query is
        // cleared after we open the dialog so a refresh doesn't re-trigger it.
        const editId = route.query.edit as string | undefined
        if (editId) {
            let target = rows.value.find(r => r.id === editId)
            if (!target) {
                // Event is outside the default 6-month window — fetch wider.
                const wideFrom = dayjs().subtract(2, 'year').utc().toISOString()
                const wideTo = dayjs().add(2, 'year').utc().toISOString()
                const wider = await eventService.list(wideFrom, wideTo)
                target = ((wider.data as any).data as EventDto[]).find(r => r.id === editId)
            }
            if (target) openEdit(target)
            const { edit, ...rest } = route.query
            router.replace({ path: route.path, query: rest })
        }
    } catch (err: any) {
        const msg = err.response?.data?.error ?? 'Couldn’t load events. Refresh to try again.'
        loadError.value = msg
        flash(msg, 'error')
    } finally {
        loading.value = false
    }
}

function openCreate() {
    editing.value = null
    dialog.value = true
}

function openEdit(row: EventDto) {
    editing.value = row
    dialog.value = true
}

async function onSaved(_ev: EventDto) { await load(); await loadCalendar() }
async function onDeleted(_id: string) { await load(); await loadCalendar() }

function reservedChipColor(row: EventDto): string {
    if (!row.capacity) return 'default'
    const used = row.spotsReserved ?? 0
    if (used >= row.capacity) return 'error'
    if (used >= row.capacity * 0.8) return 'warning'
    return 'success'
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
