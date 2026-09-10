<template>
    <v-container>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <h1 class="text-h4">Email Campaigns</h1>
            <v-chip v-if="activeSubscriberCount !== null" size="small" color="success" variant="tonal">
                {{ activeSubscriberCount }} active subscribers
            </v-chip>
            <v-spacer></v-spacer>
            <v-btn variant="text" @click="load">Refresh</v-btn>
            <v-btn color="primary" prepend-icon="mdi-email-edit-outline" @click="openCompose(null)">
                New Campaign
            </v-btn>
        </div>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th>Subject</th>
                        <th style="width: 220px">Audience</th>
                        <th style="width: 130px">Status</th>
                        <th style="width: 140px">Recipients</th>
                        <th style="width: 180px">Sent / Scheduled</th>
                        <th style="width: 160px">Created</th>
                        <th style="width: 260px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="c in campaigns" :key="c.id">
                        <td>{{ c.subject }}</td>
                        <td class="text-medium-emphasis">{{ c.audienceLabel }}</td>
                        <td><v-chip size="small" :color="statusColor(c.status)">{{ c.status }}</v-chip></td>
                        <td>{{ c.recipientCount }}</td>
                        <td>
                            <span v-if="c.status === 'scheduled' && c.scheduledForUtc" class="text-info">
                                {{ formatDate(c.scheduledForUtc) }}
                            </span>
                            <span v-else>{{ c.sentAtUtc ? formatDate(c.sentAtUtc) : '—' }}</span>
                        </td>
                        <td>{{ formatDate(c.createdAtUtc) }}</td>
                        <td class="text-right">
                            <v-btn v-if="c.status === 'draft'" variant="text" size="small" @click="openCompose(c.id)">
                                Edit
                            </v-btn>
                            <v-btn v-if="c.status === 'draft'" size="small" color="primary" variant="tonal"
                                @click="sendCampaign(c)">
                                Send
                            </v-btn>
                            <v-btn v-if="c.status === 'scheduled'" size="small" color="warning" variant="tonal"
                                @click="cancelSchedule(c)">
                                Cancel send
                            </v-btn>
                            <v-btn v-if="c.status !== 'sent' && c.status !== 'sending' && c.status !== 'scheduled'"
                                variant="text" size="small" color="error" @click="deleteCampaign(c)">
                                Delete
                            </v-btn>
                            <v-btn v-if="c.status === 'sent'" variant="text" size="small" @click="openCompose(c.id)">
                                View
                            </v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && campaigns.length === 0">
                        <td colspan="7" class="text-center text-medium-emphasis py-8">
                            No campaigns yet. Compose one to get started.
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <!-- Compose / view dialog -->
        <v-dialog v-model="composeOpen" max-width="900" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ composeTitle }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="composeOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <!-- Audience: who this goes to. Purchase audiences are resolved again at send
                         time, so the count shown here is a preview, not a snapshot. -->
                    <v-select v-model="audienceKind" :items="audienceKindItems" item-title="title" item-value="value"
                        label="Audience" density="compact" :readonly="composeReadonly"></v-select>
                    <v-autocomplete v-if="audienceKind === 'event'" v-model="audienceConfig.eventId" :items="eventItems"
                        item-title="title" item-value="id" label="Event" density="compact" class="mt-4"
                        :readonly="composeReadonly" :loading="!audienceOptions" no-data-text="No events in the last two years"></v-autocomplete>
                    <template v-if="audienceKind === 'event_type'">
                        <v-autocomplete v-model="audienceConfig.eventTypeId" :items="audienceOptions?.eventTypes ?? []"
                            item-title="name" item-value="id" label="Event type" density="compact" class="mt-4"
                            :readonly="composeReadonly" :loading="!audienceOptions"></v-autocomplete>
                        <v-row dense class="mt-2">
                            <v-col cols="12" sm="6">
                                <v-text-field v-model="audienceFromLocal" type="date" label="Events from (optional)"
                                    density="compact" :readonly="composeReadonly" hint="Limit to events starting on or after this date" persistent-hint></v-text-field>
                            </v-col>
                            <v-col cols="12" sm="6">
                                <v-text-field v-model="audienceToLocal" type="date" label="Events through (optional)"
                                    density="compact" :readonly="composeReadonly" hint="Limit to events starting on or before this date" persistent-hint></v-text-field>
                            </v-col>
                        </v-row>
                    </template>
                    <v-autocomplete v-if="audienceKind === 'pass_product'" v-model="audienceConfig.passProductId"
                        :items="audienceOptions?.passProducts ?? []" item-title="name" item-value="id" label="Pass product"
                        density="compact" class="mt-4" :readonly="composeReadonly" :loading="!audienceOptions"></v-autocomplete>
                    <div class="text-caption mt-1 mb-4">
                        <span v-if="audienceCountLoading" class="text-medium-emphasis">Counting recipients...</span>
                        <span v-else-if="audienceCountError" class="text-error">{{ audienceCountError }}</span>
                        <span v-else-if="audienceCount" class="text-success">
                            {{ audienceCount.recipients }} recipient{{ audienceCount.recipients === 1 ? '' : 's' }}: {{ audienceCount.label }}<template
                                v-if="audienceCount.suppressed"> ({{ audienceCount.suppressed }} on the suppression list will be skipped)</template>
                        </span>
                        <span v-else class="text-medium-emphasis">Pick an audience to see how many people it reaches.</span>
                    </div>
                    <v-text-field v-model="composeForm.subject" label="Subject" density="compact"
                        :readonly="composeReadonly"></v-text-field>
                    <div class="text-caption text-medium-emphasis mb-1">Body</div>
                    <RichTextEditor v-if="!composeReadonly" v-model="composeForm.bodyHtml" />
                    <div v-else class="rendered-body">
                        <RichTextView :html="composeForm.bodyHtml" />
                    </div>
                    <v-text-field v-if="!composeReadonly" v-model="scheduleLocal" type="datetime-local"
                        label="Schedule for (optional)" density="compact" class="mt-4"
                        hint="Leave blank to send now. Time is in your track's timezone." persistent-hint
                        prepend-inner-icon="mdi-clock-outline"></v-text-field>
                    <p class="text-caption text-medium-emphasis mt-3">
                        An unsubscribe link and a short footer are added automatically when campaigns are delivered.
                    </p>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="saving" @click="composeOpen = false">{{ composeReadonly ? 'Close' : 'Cancel' }}</v-btn>
                    <v-btn v-if="!composeReadonly" :loading="saving" color="primary" @click="saveDraft">Save Draft</v-btn>
                    <v-btn v-if="!composeReadonly" :loading="sending" color="success" @click="saveAndSend">
                        {{ scheduleLocal ? 'Save & Schedule' : 'Save & Send' }}
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="5000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import dayjs from 'dayjs'
import { formatTenantDateTime } from '@/helpers/TenantTime'
import { CampaignService, type CampaignListItem , type CampaignAudienceKind, type CampaignAudienceConfig, type CampaignAudienceOptions, type CampaignAudienceCount } from '@/services/CampaignService'
import { NewsletterService } from '@/services/NewsletterService'
import RichTextEditor from '@/components/RichTextEditor.vue'
import RichTextView from '@/components/RichTextView.vue'
import { useConfirm } from '@/composables/useConfirm'
import { formatEmailCost } from '@/helpers/EmailPricing'
import { branding } from '@/stores/branding'

const confirm = useConfirm()
const tz = () => branding.timezone || 'UTC'
const campaignService = new CampaignService()
const newsletterService = new NewsletterService()

const campaigns = ref<CampaignListItem[]>([])
const loading = ref(false)
const activeSubscriberCount = ref<number | null>(null)

const composeOpen = ref(false)
const composeId = ref<string | null>(null)
const composeReadonly = ref(false)
const composeForm = ref({ subject: '', bodyHtml: '' })

// --- Audience ------------------------------------------------------------------------
const audienceKindItems: { title: string; value: CampaignAudienceKind }[] = [
    { title: 'Newsletter subscribers', value: 'subscribers' },
    { title: 'Purchasers of an event', value: 'event' },
    { title: 'Purchasers of an event type', value: 'event_type' },
    { title: 'Holders of a pass product', value: 'pass_product' },
]
function emptyAudienceConfig(): CampaignAudienceConfig {
    return { eventId: null, eventTypeId: null, passProductId: null, fromUtc: null, toUtc: null }
}
const audienceKind = ref<CampaignAudienceKind>('subscribers')
const audienceConfig = ref<CampaignAudienceConfig>(emptyAudienceConfig())
// Date-only inputs in the track's timezone; converted to a UTC window at the API boundary.
const audienceFromLocal = ref('')
const audienceToLocal = ref('')
const audienceOptions = ref<CampaignAudienceOptions | null>(null)
const audienceCount = ref<CampaignAudienceCount | null>(null)
const audienceCountLoading = ref(false)
const audienceCountError = ref('')

const eventItems = computed(() => (audienceOptions.value?.events ?? []).map(e => ({
    id: e.id,
    title: `${e.title} (${dayjs(e.startsAtUtc).tz(tz()).format('MMM D, YYYY')}${e.status === 'cancelled' ? ', cancelled' : ''})`,
})))

// The config the API receives: only the target the kind uses, dates widened to whole days.
function currentAudienceConfig(): CampaignAudienceConfig {
    const c = emptyAudienceConfig()
    if (audienceKind.value === 'event') c.eventId = audienceConfig.value.eventId
    if (audienceKind.value === 'event_type') {
        c.eventTypeId = audienceConfig.value.eventTypeId
        c.fromUtc = audienceFromLocal.value ? dayjs.tz(audienceFromLocal.value, tz()).utc().toISOString() : null
        c.toUtc = audienceToLocal.value ? dayjs.tz(audienceToLocal.value, tz()).add(1, 'day').utc().toISOString() : null
    }
    if (audienceKind.value === 'pass_product') c.passProductId = audienceConfig.value.passProductId
    return c
}
function audienceTargetChosen(): boolean {
    const c = currentAudienceConfig()
    switch (audienceKind.value) {
        case 'event': return !!c.eventId
        case 'event_type': return !!c.eventTypeId
        case 'pass_product': return !!c.passProductId
        default: return true
    }
}
function composePayload() {
    return { ...composeForm.value, audienceKind: audienceKind.value, audienceConfig: currentAudienceConfig() }
}
function applyAudience(kind: CampaignAudienceKind | undefined, cfg: Partial<CampaignAudienceConfig> | null | undefined) {
    audienceKind.value = kind ?? 'subscribers'
    audienceConfig.value = { ...emptyAudienceConfig(), ...(cfg ?? {}) }
    audienceFromLocal.value = cfg?.fromUtc ? dayjs(cfg.fromUtc).tz(tz()).format('YYYY-MM-DD') : ''
    // toUtc is stored as the exclusive start of the day after the chosen "through" date.
    audienceToLocal.value = cfg?.toUtc ? dayjs(cfg.toUtc).tz(tz()).subtract(1, 'day').format('YYYY-MM-DD') : ''
    audienceCount.value = null
    audienceCountError.value = ''
}

let countTimer: ReturnType<typeof setTimeout> | null = null
watch([audienceKind, audienceConfig, audienceFromLocal, audienceToLocal], () => {
    if (!composeOpen.value) return
    if (countTimer) clearTimeout(countTimer)
    countTimer = setTimeout(refreshAudienceCount, 300)
}, { deep: true })

async function refreshAudienceCount() {
    audienceCount.value = null
    audienceCountError.value = ''
    if (!audienceTargetChosen()) return
    audienceCountLoading.value = true
    try {
        const r = await campaignService.audienceCount(audienceKind.value, currentAudienceConfig())
        audienceCount.value = (r.data as any).data
    } catch (err: any) {
        audienceCountError.value = err.response?.data?.error || 'Could not count this audience. Try again or pick a different target.'
    } finally {
        audienceCountLoading.value = false
    }
}

async function loadAudienceOptions() {
    if (audienceOptions.value) return
    try {
        const r = await campaignService.audienceOptions()
        audienceOptions.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load the events and passes for the audience picker. Reopen the campaign to retry.', 'error')
    }
}
// datetime-local string in the tenant's timezone; blank = send immediately.
const scheduleLocal = ref('')
const saving = ref(false)
const sending = ref(false)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const composeTitle = computed(() => composeReadonly.value ? 'Campaign' : (composeId.value ? 'Edit Campaign' : 'New Campaign'))

onMounted(load)

async function load() {
    loading.value = true
    try {
        const [cl, nc] = await Promise.all([
            campaignService.list(),
            newsletterService.getActiveCount(),
        ])
        campaigns.value = (cl.data as any).data
        activeSubscriberCount.value = (nc.data as any).data.count
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load campaigns.', 'error')
    } finally {
        loading.value = false
    }
}

async function openCompose(id: string | null) {
    composeId.value = id
    composeReadonly.value = false
    scheduleLocal.value = ''
    if (id) {
        try {
            const r = await campaignService.get(id)
            const d: any = (r.data as any).data
            composeForm.value = { subject: d.subject, bodyHtml: d.bodyHtml }
            composeReadonly.value = d.status !== 'draft'
            applyAudience(d.audienceKind, d.audienceConfig)
        } catch (err: any) {
            flash(err.response?.data?.error || 'Failed to load campaign.', 'error')
            return
        }
    } else {
        composeForm.value = { subject: '', bodyHtml: '' }
        applyAudience('subscribers', null)
    }
    composeOpen.value = true
    loadAudienceOptions()
    refreshAudienceCount()
}

async function saveDraft() {
    if (!validate()) return
    saving.value = true
    try {
        if (composeId.value) {
            await campaignService.update(composeId.value, composePayload())
        } else {
            const r = await campaignService.create(composePayload())
            composeId.value = (r.data as any).data.id
        }
        flash('Draft saved.', 'success')
        composeOpen.value = false
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function saveAndSend() {
    if (!validate()) return
    // Blank schedule = send now. A future time schedules it.
    let scheduledForUtc: string | null = null
    if (scheduleLocal.value) {
        const when = dayjs.tz(scheduleLocal.value, tz())
        if (!when.isValid()) { flash('That schedule time is invalid.', 'error'); return }
        if (when.isBefore(dayjs())) { flash('Schedule time must be in the future.', 'error'); return }
        scheduledForUtc = when.utc().toISOString()
    }
    const isScheduling = scheduledForUtc !== null
    if (!await confirm({
        title: isScheduling ? 'Schedule campaign?' : 'Send campaign?',
        message: buildSendConfirm(composeForm.value.subject, scheduledForUtc),
        confirmText: isScheduling ? 'Schedule' : 'Send',
    })) return
    sending.value = true
    try {
        let id = composeId.value
        if (id) {
            await campaignService.update(id, composePayload())
        } else {
            const r = await campaignService.create(composePayload())
            id = (r.data as any).data.id
        }
        const sendR = await campaignService.send(id!, scheduledForUtc)
        const notice = (sendR.data as any).data.sendNotice
        flash(notice ? `Queued ${(sendR.data as any).data.recipientCount} recipients. ${notice}` : `Sent to ${(sendR.data as any).data.recipientCount} recipients.`, 'success')
        composeOpen.value = false
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Send failed.', 'error')
    } finally {
        sending.value = false
    }
}

async function sendCampaign(c: CampaignListItem) {
    // Count the audience as it stands now, the same way the send will resolve it.
    let count: CampaignAudienceCount | null = null
    try {
        const r = await campaignService.audienceCount(c.audienceKind, c.audienceConfig)
        count = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not count this campaign\'s audience; open it and check the audience before sending.', 'error')
        return
    }
    if (!await confirm({ title: 'Send campaign?', message: buildSendConfirm(c.subject, null, count), confirmText: 'Send' })) return
    try {
        const r = await campaignService.send(c.id)
        const notice = (r.data as any).data.sendNotice
        flash(notice ? `Queued ${(r.data as any).data.recipientCount} recipients. ${notice}` : `Sent.`, 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Send failed.', 'error')
    }
}

async function deleteCampaign(c: CampaignListItem) {
    if (!await confirm({ title: 'Delete campaign?', message: `Delete "${c.subject}"? This is permanent.`, confirmText: 'Delete', confirmColor: 'error' })) return
    try {
        await campaignService.delete(c.id)
        flash('Campaign deleted.', 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    }
}

function buildSendConfirm(subject: string, scheduledForUtc?: string | null, count?: CampaignAudienceCount | null): string {
    const c = count ?? audienceCount.value
    const n = c ? Math.max(0, c.recipients - c.suppressed) : (activeSubscriberCount.value ?? 0)
    const who = c ? `${n} recipient${n === 1 ? '' : 's'} (${c.label})` : `${n} active subscribers`
    const lead = scheduledForUtc
        ? `Schedule "${subject}" for ${formatDate(scheduledForUtc)} to ${who}?`
        : `Send "${subject}" to ${who}?`
    return `${lead}\n\nEstimated cost: ${formatEmailCost(n)} (${n} emails this send)`
}

async function cancelSchedule(c: CampaignListItem) {
    if (!await confirm({
        title: 'Cancel scheduled send?',
        message: `"${c.subject}" will return to draft and won't send at its scheduled time.`,
        confirmText: 'Cancel send',
        confirmColor: 'warning',
    })) return
    try {
        await campaignService.unschedule(c.id)
        flash('Schedule cancelled; campaign is back to draft.', 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not cancel the schedule.', 'error')
    }
}

function validate(): boolean {
    if (!audienceTargetChosen()) {
        flash('Pick who this campaign goes to: an event, an event type, or a pass product.', 'error'); return false
    }
    if (!composeForm.value.subject.trim()) {
        flash('Subject is required.', 'error'); return false
    }
    if (!composeForm.value.bodyHtml.trim() || composeForm.value.bodyHtml === '<p></p>') {
        flash('Body is required.', 'error'); return false
    }
    return true
}

function statusColor(status: string): string {
    switch (status) {
        case 'draft': return 'grey'
        case 'scheduled': return 'info'
        case 'sending': return 'warning'
        case 'sent': return 'success'
        case 'failed': return 'error'
        default: return 'default'
    }
}

function formatDate(utc: string): string {
    return formatTenantDateTime(utc, 'YYYY-MM-DD HH:mm')
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>

<style scoped>
.rendered-body {
    border: 1px solid rgba(0, 0, 0, 0.12);
    border-radius: 4px;
    padding: 12px;
    min-height: 200px;
}
</style>
