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
                        <th style="width: 130px">Status</th>
                        <th style="width: 140px">Recipients</th>
                        <th style="width: 180px">Sent</th>
                        <th style="width: 160px">Created</th>
                        <th style="width: 260px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="c in campaigns" :key="c.id">
                        <td>{{ c.subject }}</td>
                        <td><v-chip size="small" :color="statusColor(c.status)">{{ c.status }}</v-chip></td>
                        <td>{{ c.recipientCount }}</td>
                        <td>{{ c.sentAtUtc ? formatDate(c.sentAtUtc) : '—' }}</td>
                        <td>{{ formatDate(c.createdAtUtc) }}</td>
                        <td class="text-right">
                            <v-btn v-if="c.status === 'draft'" variant="text" size="small" @click="openCompose(c.id)">
                                Edit
                            </v-btn>
                            <v-btn v-if="c.status === 'draft'" size="small" color="primary" variant="tonal"
                                @click="sendCampaign(c)">
                                Send
                            </v-btn>
                            <v-btn v-if="c.status !== 'sent' && c.status !== 'sending'" variant="text" size="small"
                                color="error" @click="deleteCampaign(c)">
                                Delete
                            </v-btn>
                            <v-btn v-if="c.status === 'sent'" variant="text" size="small" @click="openCompose(c.id)">
                                View
                            </v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && campaigns.length === 0">
                        <td colspan="6" class="text-center text-medium-emphasis py-8">
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
                    <v-text-field v-model="composeForm.subject" label="Subject" density="compact"
                        :readonly="composeReadonly"></v-text-field>
                    <div class="text-caption text-medium-emphasis mb-1">Body</div>
                    <RichTextEditor v-if="!composeReadonly" v-model="composeForm.bodyHtml" />
                    <div v-else class="rendered-body">
                        <RichTextView :html="composeForm.bodyHtml" />
                    </div>
                    <p class="text-caption text-medium-emphasis mt-3">
                        An unsubscribe link and a short footer are added automatically when campaigns are delivered.
                    </p>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="saving" @click="composeOpen = false">{{ composeReadonly ? 'Close' : 'Cancel' }}</v-btn>
                    <v-btn v-if="!composeReadonly" :loading="saving" color="primary" @click="saveDraft">Save Draft</v-btn>
                    <v-btn v-if="!composeReadonly" :loading="sending" color="success" @click="saveAndSend">
                        Save &amp; Send
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="5000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { CampaignService, type CampaignListItem } from '@/services/CampaignService'
import { NewsletterService } from '@/services/NewsletterService'
import RichTextEditor from '@/components/RichTextEditor.vue'
import RichTextView from '@/components/RichTextView.vue'
import { useConfirm } from '@/composables/useConfirm'
import { formatEmailCost } from '@/helpers/EmailPricing'

const confirm = useConfirm()
const campaignService = new CampaignService()
const newsletterService = new NewsletterService()

const campaigns = ref<CampaignListItem[]>([])
const loading = ref(false)
const activeSubscriberCount = ref<number | null>(null)

const composeOpen = ref(false)
const composeId = ref<string | null>(null)
const composeReadonly = ref(false)
const composeForm = ref({ subject: '', bodyHtml: '' })
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
    if (id) {
        try {
            const r = await campaignService.get(id)
            const d: any = (r.data as any).data
            composeForm.value = { subject: d.subject, bodyHtml: d.bodyHtml }
            composeReadonly.value = d.status !== 'draft'
        } catch (err: any) {
            flash(err.response?.data?.error || 'Failed to load campaign.', 'error')
            return
        }
    } else {
        composeForm.value = { subject: '', bodyHtml: '' }
    }
    composeOpen.value = true
}

async function saveDraft() {
    if (!validate()) return
    saving.value = true
    try {
        if (composeId.value) {
            await campaignService.update(composeId.value, composeForm.value)
        } else {
            const r = await campaignService.create(composeForm.value)
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
    if (!await confirm({ title: 'Send campaign?', message: buildSendConfirm(composeForm.value.subject), confirmText: 'Send' })) return
    sending.value = true
    try {
        let id = composeId.value
        if (id) {
            await campaignService.update(id, composeForm.value)
        } else {
            const r = await campaignService.create(composeForm.value)
            id = (r.data as any).data.id
        }
        const sendR = await campaignService.send(id!)
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
    if (!await confirm({ title: 'Send campaign?', message: buildSendConfirm(c.subject), confirmText: 'Send' })) return
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

function buildSendConfirm(subject: string): string {
    const n = activeSubscriberCount.value ?? 0
    return `Send "${subject}" to ${n} active subscribers?\n\nEstimated cost: ${formatEmailCost(n)} (${n} emails this send)`
}

function validate(): boolean {
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
    return dayjs.utc(utc).local().format('YYYY-MM-DD HH:mm')
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
