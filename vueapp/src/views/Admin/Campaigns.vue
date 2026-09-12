<template>
    <v-container>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <span class="text-body-2 text-medium-emphasis">One email to a list, sent now or on a date you pick.</span>
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
                        <th style="width: 110px">Opens</th>
                        <th style="width: 110px">Clicks</th>
                        <th style="width: 120px">Bought</th>
                        <th style="width: 180px">Sent / Scheduled</th>
                        <th style="width: 160px">Created</th>
                        <th style="width: 260px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="c in campaigns" :key="c.id">
                        <td>
                            {{ c.subject }}
                            <v-chip v-if="c.channel !== 'email'" size="x-small" variant="tonal" class="ml-1">
                                {{ c.channel === 'sms' ? 'Text' : 'Email + text' }}
                            </v-chip>
                        </td>
                        <td class="text-medium-emphasis">{{ c.audienceLabel }}</td>
                        <td><v-chip size="small" :color="statusColor(c.status)">{{ c.status }}</v-chip></td>
                        <td>
                            {{ c.recipientCount }}
                            <span v-if="c.textCount" class="text-caption text-medium-emphasis">({{ c.textCount }} text{{ c.textCount === 1 ? '' : 's' }})</span>
                        </td>
                        <td>
                            <v-tooltip v-if="c.status === 'sent'" text="Distinct people who opened. Includes automatic opens from Apple Mail, so treat as a ceiling." location="top">
                                <template #activator="{ props }">
                                    <span v-bind="props">{{ c.uniqueOpens }} <span class="text-caption text-medium-emphasis">{{ pct(c.uniqueOpens, c.recipientCount) }}</span></span>
                                </template>
                            </v-tooltip>
                            <span v-else class="text-medium-emphasis">-</span>
                        </td>
                        <td>
                            <span v-if="c.status === 'sent'">{{ c.uniqueClicks }} <span class="text-caption text-medium-emphasis">{{ pct(c.uniqueClicks, c.recipientCount) }}</span></span>
                            <span v-else class="text-medium-emphasis">-</span>
                        </td>
                        <td>
                            <v-tooltip v-if="c.status === 'sent'" :text="`Bought a ticket or a pass within a week of the send: ${money(c.revenueCents)} in sales`" location="top">
                                <template #activator="{ props }">
                                    <span v-bind="props">{{ c.conversions }} <span class="text-caption text-medium-emphasis">{{ pct(c.conversions, c.recipientCount) }}</span></span>
                                </template>
                            </v-tooltip>
                            <span v-else class="text-medium-emphasis">-</span>
                        </td>
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
                            <v-btn v-if="c.status !== 'sending'" variant="text" size="small" prepend-icon="mdi-content-copy"
                                @click="duplicateCampaign(c)">
                                Duplicate
                            </v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && campaigns.length === 0">
                        <td colspan="10" class="text-center text-medium-emphasis py-8">
                            No campaigns yet. Compose one to get started.
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <!-- Compose / view dialog -->
        <v-dialog v-model="composeOpen" fullscreen persistent transition="dialog-bottom-transition">
            <v-card class="d-flex flex-column" style="height: 100%">
                <v-card-title class="d-flex align-center" style="flex: 0 0 auto">
                    <span>{{ composeTitle }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="composeOpen = false"></v-btn>
                </v-card-title>
                <!-- A sent campaign has two halves: how it did, and what it said. -->
                <v-tabs v-if="composeReadonly" v-model="detailTab" color="primary" density="comfortable" style="flex: 0 0 auto">
                    <v-tab value="results" prepend-icon="mdi-chart-box-outline">Results</v-tab>
                    <v-tab value="message" prepend-icon="mdi-email-outline">Message</v-tab>
                </v-tabs>
                <v-divider style="flex: 0 0 auto"></v-divider>
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                    <!-- Results: the numbers, who did what, and which links did the work. -->
                    <template v-if="composeReadonly && detailTab === 'results'">
                    <div v-if="!report" class="text-center py-8"><v-progress-circular indeterminate size="24" /></div>
                    <template v-else>
                        <div class="d-flex flex-wrap ga-3 mb-2">
                            <v-card v-for="t in reportTiles" :key="t.label" variant="tonal" class="pa-3 flex-grow-1" min-width="120">
                                <div class="text-h6">{{ t.value }}</div>
                                <div class="text-caption text-medium-emphasis">{{ t.label }}</div>
                            </v-card>
                        </div>
                        <div class="text-caption text-medium-emphasis mb-4">
                            Bought = a ticket or pass purchase by a recipient within {{ report.windowDays }} days of the send; {{ report.clickConversions }} of them clicked the email first.
                            Opens include Apple Mail's automatic ones, so clicks are the honest engagement number.
                        </div>
                        <div class="d-flex flex-wrap align-center ga-2 mb-2">
                            <v-btn-toggle v-model="recipientFilter" mandatory density="compact" variant="outlined" divided @update:model-value="loadRecipients(1)">
                                <v-btn value="all" size="small">All</v-btn>
                                <v-btn value="opened" size="small">Opened</v-btn>
                                <v-btn value="clicked" size="small">Clicked</v-btn>
                                <v-btn value="bought" size="small">Bought</v-btn>
                                <v-btn value="skipped" size="small">Skipped</v-btn>
                            </v-btn-toggle>
                            <v-text-field v-model="recipientSearch" density="compact" hide-details clearable placeholder="Search name or email"
                                prepend-inner-icon="mdi-magnify" style="max-width: 280px" @keyup.enter="loadRecipients(1)" @click:clear="loadRecipients(1)"></v-text-field>
                            <v-spacer></v-spacer>
                            <span class="text-caption text-medium-emphasis">{{ recipientsTotal }} {{ recipientsTotal === 1 ? 'person' : 'people' }}</span>
                            <v-btn size="small" variant="text" prepend-icon="mdi-download" :loading="csvLoading" @click="downloadCsv">CSV</v-btn>
                        </div>
                        <v-table density="compact" class="mb-2">
                            <thead>
                                <tr><th>Who</th><th>Via</th><th>Status</th><th>Opened</th><th>Clicked</th><th>Bought</th></tr>
                            </thead>
                            <tbody>
                                <tr v-if="!recipientsLoading && recipients.length === 0"><td colspan="6" class="text-center text-medium-emphasis py-4">Nobody matches.</td></tr>
                                <tr v-for="r in recipients" :key="r.id">
                                    <td><div>{{ r.name || r.email }}</div><div v-if="r.name" class="text-caption text-medium-emphasis">{{ r.email }}</div></td>
                                    <td>{{ r.channel === 'sms' ? 'Text' : 'Email' }}</td>
                                    <td>
                                        <v-tooltip v-if="r.reason" :text="r.reason" location="top">
                                            <template #activator="{ props }"><span v-bind="props" class="text-decoration-underline">{{ r.status }}</span></template>
                                        </v-tooltip>
                                        <span v-else>{{ r.status }}</span>
                                    </td>
                                    <td>{{ r.openedAtUtc ? formatDate(r.openedAtUtc) : '-' }}</td>
                                    <td>{{ r.clickedAtUtc ? formatDate(r.clickedAtUtc) : '-' }}</td>
                                    <td>
                                        <span v-if="r.boughtAtUtc">{{ formatDate(r.boughtAtUtc) }} <span class="text-caption text-medium-emphasis">{{ money(r.revenueCents) }}</span></span>
                                        <span v-else>-</span>
                                    </td>
                                </tr>
                            </tbody>
                        </v-table>
                        <div v-if="recipientsTotal > recipientPageSize" class="d-flex justify-center mb-4">
                            <v-pagination :model-value="recipientPage" :length="Math.ceil(recipientsTotal / recipientPageSize)" density="compact"
                                :total-visible="7" @update:model-value="loadRecipients"></v-pagination>
                        </div>
                        <div v-if="viewClickUrls.length" class="text-subtitle-2 mt-4">Links clicked</div>
                    <!-- Sent campaigns: which links people clicked, distinct people first. -->
                    <v-table v-if="composeReadonly && viewClickUrls.length" density="compact" class="mt-4">
                        <thead>
                            <tr><th>Link clicked</th><th class="text-right">People</th><th class="text-right">Clicks</th></tr>
                        </thead>
                        <tbody>
                            <tr v-for="u in viewClickUrls" :key="u.url">
                                <td class="text-truncate" style="max-width: 520px"><a :href="u.url" target="_blank" rel="noopener">{{ u.url }}</a></td>
                                <td class="text-right">{{ u.uniqueClickers }}</td>
                                <td class="text-right">{{ u.totalClicks }}</td>
                            </tr>
                        </tbody>
                    </v-table>
                    </template>
                    </template>
                    <template v-if="!composeReadonly || detailTab === 'message'">
                    <!-- Audience: a saved audience from the Audiences tab. It is resolved again at
                         send time, so the count shown here is a preview, not a snapshot. -->
                    <v-select v-model="audienceIds" :items="audienceItems" item-title="title" item-value="value"
                        label="Audiences" density="compact" :readonly="composeReadonly" :loading="audiencesLoading"
                        multiple chips closable-chips
                        :hint="legacyAudienceLabel || 'Pick one or more. Someone in several audiences is sent once. Build audiences on the Audiences tab.'" persistent-hint
                        no-data-text="No audiences yet. Add them on the Audiences tab."></v-select>
                    <div class="text-caption mt-1 mb-4">
                        <span v-if="audienceCountLoading" class="text-medium-emphasis">Counting recipients...</span>
                        <span v-else-if="audienceCountError" class="text-error">{{ audienceCountError }}</span>
                        <span v-else-if="audienceCount" class="text-success">
                            {{ audienceCount.recipients }} recipient{{ audienceCount.recipients === 1 ? '' : 's' }}: {{ audienceCount.label }}<template
                                v-if="audienceCount.suppressed"> ({{ audienceCount.suppressed }} on the suppression list will be skipped)</template><template
                                v-if="composeForm.channel !== 'email'"> · {{ audienceCount.phones }} can be texted</template>
                        </span>
                        <span v-else class="text-medium-emphasis">Pick an audience to see how many people it reaches.</span>
                    </div>
                    <!-- How it goes out. A text needs a phone on the rider's account; the count above says how many have one. -->
                    <div class="d-flex align-center flex-wrap ga-3 mb-4">
                        <span class="text-caption text-medium-emphasis">Send as</span>
                        <v-btn-toggle v-model="composeForm.channel" mandatory density="compact" variant="outlined" divided
                            :disabled="composeReadonly">
                            <v-btn value="email" size="small" prepend-icon="mdi-email-outline">Email</v-btn>
                            <v-btn value="sms" size="small" prepend-icon="mdi-message-text-outline">Text</v-btn>
                            <v-btn value="both" size="small">Both</v-btn>
                        </v-btn-toggle>
                    </div>
                    <v-select v-if="!composeReadonly && templates.length && composeForm.channel !== 'sms'" v-model="templatePick" :items="templates"
                        item-title="name" item-value="id" label="Start from a saved template" density="compact" clearable
                        class="mb-2" @update:model-value="applyTemplate"></v-select>
                    <v-text-field v-model="composeForm.subject" :label="composeForm.channel === 'sms' ? 'Name (only you see this)' : 'Subject'"
                        density="compact" :readonly="composeReadonly"></v-text-field>
                    <template v-if="composeForm.channel !== 'email'">
                        <v-textarea v-model="composeForm.smsBody" label="Text message" density="compact" class="mt-4" rows="3" auto-grow
                            :readonly="composeReadonly" :counter="1000" :maxlength="1000"
                            :hint="'Merge fields work here too. ' + smsCounter(composeForm.smsBody) + ' \'Reply STOP to opt out\' is added automatically.'"
                            persistent-hint></v-textarea>
                    </template>
                    <template v-if="composeForm.channel !== 'sms'">
                    <v-text-field v-model="composeForm.previewText" label="Preview text (optional)" density="compact" class="mt-4"
                        :readonly="composeReadonly" hint="The snippet inboxes show under the subject line" persistent-hint></v-text-field>
                    <div class="d-flex align-center mt-4 mb-1">
                        <span class="text-caption text-medium-emphasis">Body</span>
                        <v-btn v-if="!composeReadonly" size="x-small" variant="text" class="ml-2" prepend-icon="mdi-content-save-outline"
                            @click="openSaveTemplate">Save as template</v-btn>
                        <v-spacer></v-spacer>
                        <v-btn-toggle v-model="composeView" mandatory density="compact" variant="outlined" divided>
                            <v-btn value="edit" size="small">{{ composeReadonly ? 'Message' : 'Edit' }}</v-btn>
                            <v-btn value="phone" size="small" prepend-icon="mdi-cellphone">Phone</v-btn>
                            <v-btn value="desktop" size="small" prepend-icon="mdi-monitor">Desktop</v-btn>
                        </v-btn-toggle>
                    </div>
                    <template v-if="composeView === 'edit'">
                        <RichTextEditor v-if="!composeReadonly" v-model="composeForm.bodyHtml" :upload-image="uploadInlineImage" email-buttons />
                        <div v-else class="rendered-body">
                            <RichTextView :html="composeForm.bodyHtml" />
                        </div>
                    </template>
                    <div v-else class="email-preview-frame" :class="{ phone: composeView === 'phone' }">
                        <div v-if="previewLoading" class="text-center py-8"><v-progress-circular indeterminate size="24" /></div>
                        <div v-else-if="previewError" class="text-error text-body-2 pa-4">{{ previewError }}</div>
                        <iframe v-else :srcdoc="previewHtml" title="Email preview" sandbox=""></iframe>
                    </div>
                    </template>
                    <v-text-field v-if="!composeReadonly" v-model="scheduleLocal" type="datetime-local"
                        label="Schedule for (optional)" density="compact" class="mt-4"
                        hint="Leave blank to send now. Time is in your track's timezone." persistent-hint
                        prepend-inner-icon="mdi-clock-outline"></v-text-field>
                    <p v-if="!composeReadonly" class="text-caption text-medium-emphasis mt-3">
                        An unsubscribe link and a short footer are added automatically when campaigns are delivered.
                    </p>
                    </template>
                </v-card-text>
                <v-card-actions style="flex: 0 0 auto">
                    <v-btn v-if="composeId" variant="text" prepend-icon="mdi-content-copy" @click="duplicateFromDialog">Duplicate</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="saving" @click="composeOpen = false">{{ composeReadonly ? 'Close' : 'Cancel' }}</v-btn>
                    <v-btn v-if="!composeReadonly" :loading="saving" color="primary" @click="saveDraft">Save Draft</v-btn>
                    <v-btn v-if="!composeReadonly" :loading="sending" color="success" @click="saveAndSend">
                        {{ scheduleLocal ? 'Save & Schedule' : 'Save & Send' }}
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Save the current body as a reusable template. -->
        <v-dialog v-model="saveTemplateOpen" max-width="440">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Save as template</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="saveTemplateOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="saveTemplateName" label="Template name" density="compact" autofocus
                        placeholder="Monthly newsletter" @keyup.enter="saveTemplate"></v-text-field>
                    <div class="text-caption text-medium-emphasis">Saves the subject, preview text, and body as they are now.</div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="saveTemplateOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="savingTemplate" :disabled="!saveTemplateName.trim()" @click="saveTemplate">Save</v-btn>
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
import { useRoute } from 'vue-router'
import { CampaignService, smsSegments, type CampaignListItem , type CampaignAudienceKind, type CampaignAudienceConfig, type CampaignAudienceCount, type MessageChannel, type CampaignReport, type CampaignRecipient, type CampaignRecipientFilter } from '@/services/CampaignService'
import { AudienceService, type AudienceItem } from '@/services/AudienceService'
import { NewsletterService } from '@/services/NewsletterService'
import RichTextEditor from '@/components/RichTextEditor.vue'
import RichTextView from '@/components/RichTextView.vue'
import { useConfirm } from '@/composables/useConfirm'
import { EmailTemplateService, type EmailTemplateItem } from '@/services/EmailTemplateService'
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
const composeForm = ref({ subject: '', bodyHtml: '', previewText: '' as string | null, channel: 'email' as MessageChannel, smsBody: '' as string | null })

function smsCounter(text: string | null): string {
    const { chars, segments } = smsSegments(text ?? '')
    return chars === 0 ? '' : `${chars} characters, ${segments} segment${segments === 1 ? '' : 's'}.`
}
// Edit / phone / desktop. The preview is the real send-time HTML from the API, not a guess.
const composeView = ref<'edit' | 'phone' | 'desktop'>('edit')
// Sent campaigns open on Results; anything still editable has only the message.
const detailTab = ref<'results' | 'message'>('results')
const viewClickUrls = ref<{ url: string; uniqueClickers: number; totalClicks: number }[]>([])

// The report shown when a sent campaign is opened.
const report = ref<CampaignReport | null>(null)
const recipients = ref<CampaignRecipient[]>([])
const recipientsTotal = ref(0)
const recipientsLoading = ref(false)
const recipientFilter = ref<CampaignRecipientFilter>('all')
const recipientSearch = ref('')
const recipientPage = ref(1)
const recipientPageSize = 50
const csvLoading = ref(false)
const reportTiles = computed(() => {
    const r = report.value
    if (!r) return []
    const rate = (n: number) => r.people > 0 ? ` (${Math.round((n / r.people) * 100)}%)` : ''
    return [
        { label: r.texts ? `Delivered (${r.emails} emails, ${r.texts} texts)` : 'Delivered', value: String(r.delivered) },
        { label: 'Opened', value: `${r.uniqueOpens}${rate(r.uniqueOpens)}` },
        { label: 'Clicked', value: `${r.uniqueClicks}${rate(r.uniqueClicks)}` },
        { label: 'Bought', value: `${r.conversions}${rate(r.conversions)}` },
        { label: 'Sales', value: money(r.revenueCents) },
    ]
})
function money(cents: number) { return `$${((cents ?? 0) / 100).toFixed(2)}` }

async function loadReport(id: string) {
    report.value = null
    recipients.value = []
    recipientsTotal.value = 0
    recipientFilter.value = 'all'
    recipientSearch.value = ''
    try {
        const { data } = await campaignService.report(id)
        report.value = data.data
        await loadRecipients(1)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load the campaign report. Close and reopen the campaign to retry.', 'error')
    }
}
async function loadRecipients(page: number) {
    if (!composeId.value) return
    recipientsLoading.value = true
    recipientPage.value = page
    try {
        const { data } = await campaignService.recipients(composeId.value, {
            search: recipientSearch.value.trim() || undefined, filter: recipientFilter.value, page, pageSize: recipientPageSize,
        })
        recipients.value = data.data.items
        recipientsTotal.value = data.data.total
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load the recipient list. Try the filter again.', 'error')
    } finally {
        recipientsLoading.value = false
    }
}
async function downloadCsv() {
    if (!composeId.value) return
    csvLoading.value = true
    try {
        const rows: CampaignRecipient[] = []
        for (let page = 1; page <= 200; page++) {
            const { data } = await campaignService.recipients(composeId.value, {
                search: recipientSearch.value.trim() || undefined, filter: recipientFilter.value, page, pageSize: 500,
            })
            rows.push(...data.data.items)
            if (rows.length >= data.data.total || data.data.items.length === 0) break
        }
        const q = (v: string | number | null | undefined) => `"${String(v ?? '').replace(/"/g, '""')}"`
        const lines = [['Name', 'Email', 'Via', 'Status', 'Reason', 'Sent', 'Opened', 'Clicked', 'Bought', 'Sales'].map(q).join(',')]
        for (const r of rows) {
            lines.push([r.name, r.email, r.channel === 'sms' ? 'Text' : 'Email', r.status, r.reason,
                r.sentAtUtc ? formatDate(r.sentAtUtc) : '', r.openedAtUtc ? formatDate(r.openedAtUtc) : '',
                r.clickedAtUtc ? formatDate(r.clickedAtUtc) : '', r.boughtAtUtc ? formatDate(r.boughtAtUtc) : '',
                r.boughtAtUtc ? (r.revenueCents / 100).toFixed(2) : ''].map(q).join(','))
        }
        const blob = new Blob(['\ufeff' + lines.join('\n')], { type: 'text/csv;charset=utf-8' })
        const a = document.createElement('a')
        a.href = URL.createObjectURL(blob)
        a.download = `${(composeForm.value.subject || 'campaign').replace(/[^\w.-]+/g, '_')}-recipients.csv`
        a.click()
        URL.revokeObjectURL(a.href)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not build the CSV. Try again.', 'error')
    } finally {
        csvLoading.value = false
    }
}

// --- Templates ------------------------------------------------------------------------
const templateService = new EmailTemplateService()
const templates = ref<EmailTemplateItem[]>([])
const templatePick = ref<string | null>(null)
const saveTemplateOpen = ref(false)
const saveTemplateName = ref('')
const savingTemplate = ref(false)

function pct(n: number, of: number): string {
    return of > 0 ? `(${Math.round((n / of) * 100)}%)` : ''
}

async function loadTemplates() {
    try {
        const r = await templateService.list()
        templates.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load saved templates. You can still write the campaign.', 'error')
    }
}

async function applyTemplate(id: string | null) {
    if (!id) return
    const t = templates.value.find(x => x.id === id)
    if (!t) return
    const hasBody = composeForm.value.bodyHtml.trim() && composeForm.value.bodyHtml !== '<p></p>'
    if (hasBody && !await confirm({
        title: 'Replace the body?',
        message: `Replace what you have written with the "${t.name}" template?`,
        confirmText: 'Replace',
    })) { templatePick.value = null; return }
    composeForm.value = {
        ...composeForm.value,
        subject: t.subject ?? composeForm.value.subject,
        previewText: t.previewText ?? composeForm.value.previewText,
        bodyHtml: t.bodyHtml,
    }
}

function openSaveTemplate() {
    saveTemplateName.value = composeForm.value.subject || ''
    saveTemplateOpen.value = true
}

async function saveTemplate() {
    if (!saveTemplateName.value.trim()) return
    savingTemplate.value = true
    try {
        await templateService.create({
            name: saveTemplateName.value.trim(),
            subject: composeForm.value.subject || null,
            previewText: composeForm.value.previewText || null,
            bodyHtml: composeForm.value.bodyHtml,
        })
        saveTemplateOpen.value = false
        flash('Template saved.')
        await loadTemplates()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not save the template. Check the name and body and try again.', 'error')
    } finally {
        savingTemplate.value = false
    }
}
const previewHtml = ref('')
const previewLoading = ref(false)
const previewError = ref('')
watch(composeView, async (v) => {
    if (v === 'edit') return
    previewLoading.value = true
    previewError.value = ''
    try {
        const r = await campaignService.preview({ bodyHtml: composeForm.value.bodyHtml, previewText: composeForm.value.previewText })
        previewHtml.value = (r.data as any).data.html
    } catch (err: any) {
        previewError.value = err.response?.data?.error || 'Could not build the preview. Switch back to Edit and try again.'
    } finally {
        previewLoading.value = false
    }
})

// --- Audience ------------------------------------------------------------------------
// Saved audiences (the Audiences tab) are the only picker now. A campaign written before they
// existed keeps its old target, shown as a hint until the admin picks a saved audience.
const audienceService = new AudienceService()
const audiences = ref<AudienceItem[]>([])
const audiencesLoading = ref(false)
const audienceIds = ref<string[]>([])
const legacyAudienceLabel = ref('')
const audienceCount = ref<CampaignAudienceCount | null>(null)
const audienceCountLoading = ref(false)
const audienceCountError = ref('')
const audienceItems = computed(() => audiences.value.map(a => ({ value: a.id, title: `${a.name} (${a.memberCount})` })))

function currentAudienceConfig(): CampaignAudienceConfig {
    return {
        eventId: null, eventTypeId: null, passProductId: null, fromUtc: null, toUtc: null,
        audienceId: audienceIds.value[0] ?? null, audienceIds: [...audienceIds.value],
    }
}
function audienceTargetChosen(): boolean {
    return audienceIds.value.length > 0
}
function composePayload() {
    return { ...composeForm.value, audienceKind: 'audience' as CampaignAudienceKind, audienceConfig: currentAudienceConfig() }
}
function applyAudience(kind: CampaignAudienceKind | undefined, cfg: Partial<CampaignAudienceConfig> | null | undefined, label?: string) {
    audienceIds.value = kind !== 'audience' ? []
        : cfg?.audienceIds?.length ? [...cfg.audienceIds]
        : cfg?.audienceId ? [cfg.audienceId] : []
    legacyAudienceLabel.value = kind && kind !== 'audience' ? `Currently: ${label ?? kind}. Pick a saved audience to change it.` : ''
    audienceCount.value = null
    audienceCountError.value = ''
}

let countTimer: ReturnType<typeof setTimeout> | null = null
watch(audienceIds, () => {
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
        const r = await campaignService.audienceCount('audience', currentAudienceConfig())
        audienceCount.value = (r.data as any).data
    } catch (err: any) {
        audienceCountError.value = err.response?.data?.error || 'Could not count this audience. Try again or pick a different one.'
    } finally {
        audienceCountLoading.value = false
    }
}

async function loadAudienceOptions() {
    audiencesLoading.value = true
    try {
        const r = await audienceService.list()
        audiences.value = r.data.data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load your audiences. Open the Audiences tab to check them, then reopen the campaign.', 'error')
    } finally {
        audiencesLoading.value = false
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

const route = useRoute()
onMounted(async () => {
    await load()
    if (typeof route.query.audience === 'string') openCompose(null)
})

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
            composeForm.value = { subject: d.subject, bodyHtml: d.bodyHtml, previewText: d.previewText ?? '', channel: d.channel ?? 'email', smsBody: d.smsBody ?? '' }
            viewClickUrls.value = d.clickUrls ?? []
            composeReadonly.value = d.status !== 'draft'
            applyAudience(d.audienceKind, d.audienceConfig, d.audienceLabel)
            if (d.status === 'sent' || d.status === 'sending') { detailTab.value = 'results'; loadReport(id) }
            else { detailTab.value = 'message'; report.value = null }
        } catch (err: any) {
            flash(err.response?.data?.error || 'Failed to load campaign.', 'error')
            return
        }
    } else {
        composeForm.value = { subject: '', bodyHtml: '', previewText: '', channel: 'email', smsBody: '' }
        viewClickUrls.value = []
        report.value = null
        // Arriving from the Audiences tab ("send a campaign to this audience") preselects it.
        const preselect = typeof route.query.audience === 'string' ? route.query.audience : null
        applyAudience('audience', { audienceIds: preselect ? [preselect] : [] })
    }
    composeView.value = 'edit'
    templatePick.value = null
    composeOpen.value = true
    loadTemplates()
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
        message: buildSendConfirm(composeForm.value.subject, scheduledForUtc, null, composeForm.value.channel),
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
    if (!await confirm({ title: 'Send campaign?', message: buildSendConfirm(c.subject, null, count, c.channel), confirmText: 'Send' })) return
    try {
        const r = await campaignService.send(c.id)
        const notice = (r.data as any).data.sendNotice
        flash(notice ? `Queued ${(r.data as any).data.recipientCount} recipients. ${notice}` : `Sent.`, 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Send failed.', 'error')
    }
}

async function duplicateFromDialog() {
    if (!composeId.value) return
    const source = { id: composeId.value, subject: composeForm.value.subject } as CampaignListItem
    composeOpen.value = false
    await duplicateCampaign(source)
}

async function duplicateCampaign(c: CampaignListItem) {
    try {
        const { data } = await campaignService.duplicate(c.id)
        flash('Copied as a new draft.', 'success')
        await load()
        await openCompose(data.data.id)
    } catch (err: any) {
        flash(err.response?.data?.error || `Could not copy "${c.subject}". Reload the page and try again.`, 'error')
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

function buildSendConfirm(subject: string, scheduledForUtc?: string | null, count?: CampaignAudienceCount | null, channel: MessageChannel = 'email'): string {
    const c = count ?? audienceCount.value
    const emails = channel === 'sms' ? 0 : (c ? Math.max(0, c.recipients - c.suppressed) : (activeSubscriberCount.value ?? 0))
    const texts = channel === 'email' ? 0 : (c?.phones ?? 0)
    const parts: string[] = []
    if (channel !== 'sms') parts.push(`${emails} email${emails === 1 ? '' : 's'}`)
    if (channel !== 'email') parts.push(`${texts} text${texts === 1 ? '' : 's'}`)
    const who = `${parts.join(' and ')}${c ? ` (${c.label})` : ''}`
    const lead = scheduledForUtc
        ? `Schedule "${subject}" for ${formatDate(scheduledForUtc)}: ${who}?`
        : `Send "${subject}": ${who}?`
    const cost: string[] = []
    if (channel !== 'sms') cost.push(`${formatEmailCost(emails)} for ${emails} emails`)
    if (channel !== 'email') cost.push(`texts are billed per message segment at your SMS rate`)
    return `${lead}\n\nEstimated cost: ${cost.join('; ')}`
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

async function uploadInlineImage(file: File): Promise<string> {
    const resp = await campaignService.uploadImage(file)
    return resp.data.data.imageUrl
}

function validate(): boolean {
    if (!audienceTargetChosen()) {
        flash('Pick who this campaign goes to: an event, an event type, or a pass product.', 'error'); return false
    }
    if (!composeForm.value.subject.trim()) {
        flash('Subject is required.', 'error'); return false
    }
    if (composeForm.value.channel !== 'sms' && (!composeForm.value.bodyHtml.trim() || composeForm.value.bodyHtml === '<p></p>')) {
        flash('Write the email body, or switch the campaign to text only.', 'error'); return false
    }
    if (composeForm.value.channel !== 'email' && !(composeForm.value.smsBody ?? '').trim()) {
        flash('Write the text message, or switch the campaign to email only.', 'error'); return false
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
.email-preview-frame {
    background: #f3f4f6;
    border: 1px solid #e5e7eb;
    border-radius: 6px;
    height: min(72vh, 960px);
    overflow: hidden;
}
.email-preview-frame.phone {
    width: 390px;
    max-width: 100%;
    margin: 0 auto;
}
.email-preview-frame iframe {
    width: 100%;
    height: 100%;
    border: 0;
    background: #f3f4f6;
}
.rendered-body {
    border: 1px solid rgba(0, 0, 0, 0.12);
    border-radius: 4px;
    padding: 12px;
    min-height: 200px;
}
</style>
