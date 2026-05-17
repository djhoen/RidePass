<template>
    <v-container v-if="survey">
        <div class="d-flex align-center mb-4 flex-wrap ga-3">
            <v-btn variant="text" size="small" @click="$router.push('/Admin/Surveys')">
                <v-icon>mdi-arrow-left</v-icon> Back
            </v-btn>
            <h1 class="text-h5">{{ survey.name }}</h1>
            <v-chip size="small" :color="statusColor(survey.status)">{{ survey.status }}</v-chip>
            <v-spacer></v-spacer>
            <v-btn variant="text" size="small" @click="goResults">View results</v-btn>
            <v-btn variant="text" size="small" @click="previewOpen = true">Preview survey</v-btn>
            <v-btn v-if="survey.status === 'draft'" color="primary" variant="tonal" @click="setStatus('published')">
                Publish
            </v-btn>
            <v-btn v-if="survey.status === 'published'" variant="tonal" @click="openSendDialog">Send invites</v-btn>
            <v-btn v-if="survey.status === 'published'" variant="tonal" @click="setStatus('closed')">Close</v-btn>
            <v-btn v-if="survey.status === 'closed'" color="primary" variant="tonal"
                @click="setStatus('published')">
                Reopen
            </v-btn>
        </div>

        <!-- Survey metadata -->
        <v-card class="mb-4">
            <v-card-title>Details</v-card-title>
            <v-card-text>
                <v-text-field v-model="meta.name" label="Internal name" density="compact" class="mt-6"></v-text-field>
                <v-text-field v-model="meta.title" label="Title (shown to respondents)" density="compact" class="mt-6"></v-text-field>
                <v-textarea v-model="meta.description" label="Description (optional)" rows="2" auto-grow class="mt-6"
                    density="compact"></v-textarea>
                <v-text-field v-model="meta.closesAtUtc" type="datetime-local" label="Closes (optional)" class="mt-6"
                    density="compact"></v-text-field>
                <v-checkbox v-model="meta.requireEmail" label="Require respondent email + name" class="mt-4"
                    density="compact"></v-checkbox>
                <v-btn color="primary" :loading="savingMeta" @click="saveMeta">Save details</v-btn>
                <span class="ml-3 text-caption text-medium-emphasis">
                    Public link:
                    <a :href="publicLink" target="_blank" rel="noopener">{{ publicLink }}</a>
                </span>
            </v-card-text>
        </v-card>

        <!-- Questions -->
        <v-card class="mb-4">
            <v-card-title class="d-flex align-center">
                Questions
                <v-spacer></v-spacer>
                <v-btn size="small" color="primary" prepend-icon="mdi-plus" @click="openAddQuestion">
                    Add question
                </v-btn>
            </v-card-title>
            <v-card-text>
                <div v-if="questions.length === 0" class="text-medium-emphasis text-center py-4">
                    No questions yet. Add one to get started.
                </div>

                <draggable :list="visibleQuestions" item-key="id" handle=".question-drag-handle"
                    :animation="180" ghost-class="drag-ghost" @end="onQuestionReorderEnd">
                    <template #item="{ element: q, index: idx }">
                        <v-card variant="outlined" class="mb-3">
                            <v-card-text>
                                <div class="d-flex align-center mb-2 flex-wrap ga-2">
                                    <v-icon class="question-drag-handle" color="grey">mdi-drag-vertical</v-icon>
                                    <v-chip size="x-small" color="primary" variant="tonal">
                                        {{ kindLabel(q.kind) }}
                                    </v-chip>
                                    <v-chip v-if="q.required" size="x-small" color="error" variant="tonal">required</v-chip>
                                    <span class="text-caption text-medium-emphasis">#{{ idx + 1 }}</span>
                                    <v-spacer></v-spacer>
                                    <v-btn variant="text" size="small" color="error" @click="deleteQuestion(q.id)">
                                        Delete
                                    </v-btn>
                                </div>
                                <v-text-field v-model="q.prompt" label="Prompt" density="compact"
                                    @blur="saveQuestion(q)"></v-text-field>
                                <v-checkbox v-model="q.required" label="Required" density="compact"
                                    @change="saveQuestion(q)"></v-checkbox>

                                <div v-if="q.kind !== 'free_form'">
                                    <div class="text-subtitle-2 mb-1">Choices</div>
                                    <draggable :list="choiceDrafts[q.id]" :item-key="'label'"
                                        handle=".choice-drag-handle"
                                        :animation="180" ghost-class="drag-ghost">
                                        <template #item="{ element: c, index: ci }">
                                            <div class="d-flex align-center mb-2 ga-2">
                                                <v-icon class="choice-drag-handle" size="small" color="grey">mdi-drag-vertical</v-icon>
                                                <v-text-field v-model="c.label" density="compact" hide-details
                                                    placeholder="Choice label"></v-text-field>
                                                <v-checkbox v-model="c.allowsFreeText" label="Other (allow custom answer)"
                                                    density="compact" hide-details></v-checkbox>
                                                <v-btn variant="text" size="small" icon @click="removeChoice(q.id, ci)">
                                                    <v-icon>mdi-close</v-icon>
                                                </v-btn>
                                            </div>
                                        </template>
                                    </draggable>
                                    <div class="d-flex align-center ga-2">
                                        <v-btn size="small" variant="text" prepend-icon="mdi-plus"
                                            @click="addChoice(q.id)">
                                            Add choice
                                        </v-btn>
                                        <v-spacer></v-spacer>
                                        <v-btn size="small" color="primary" variant="tonal" @click="saveChoices(q.id)">
                                            Save choices
                                        </v-btn>
                                    </div>
                                </div>
                            </v-card-text>
                        </v-card>
                    </template>
                </draggable>
            </v-card-text>
        </v-card>

        <!-- Add question dialog -->
        <v-dialog v-model="addQOpen" max-width="640" persistent>
            <v-card>
                <v-card-title>Add question</v-card-title>
                <v-card-text>
                    <v-select v-model="newQ.kind" :items="kindOptions" label="Type" density="compact" class="mt-6"></v-select>
                    <v-text-field v-model="newQ.prompt" label="Question" density="compact" class="mt-6"></v-text-field>
                    <v-checkbox v-model="newQ.required" label="Required" density="compact" class="mt-6"></v-checkbox>
                    <div v-if="newQ.kind !== 'free_form'">
                        <div class="text-subtitle-2 mb-1">Choices</div>
                        <div v-for="(c, ci) in newQ.choices" :key="ci"
                            class="d-flex align-center mb-2 ga-2">
                            <v-text-field v-model="c.label" density="compact" hide-details
                                placeholder="Choice label"></v-text-field>
                            <v-checkbox v-model="c.allowsFreeText" label="Other (allow custom answer)"
                                density="compact" hide-details></v-checkbox>
                            <v-btn variant="text" size="small" icon @click="removeNewChoice(ci)">
                                <v-icon>mdi-close</v-icon>
                            </v-btn>
                        </div>
                        <v-btn size="small" variant="text" prepend-icon="mdi-plus" @click="addNewChoice">
                            Add choice
                        </v-btn>
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="savingQ" @click="addQOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="savingQ" @click="addQuestion">Add</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Send invites dialog -->
        <v-dialog v-model="sendOpen" max-width="720" persistent>
            <v-card>
                <v-card-title>Send invites</v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-3">
                        Pick who gets the survey. Each recipient gets a unique link so you can see who opened
                        and completed it.
                    </p>

                    <v-select v-model="audienceType" :items="audienceTypeOptions" label="Audience"
                        density="compact" @update:model-value="onAudienceChange"></v-select>

                    <!-- 1. Custom — search and pick customers -->
                    <div v-if="audienceType === 'custom'">
                        <v-text-field v-model="customerSearch" label="Search customers"
                            density="compact" hide-details prepend-inner-icon="mdi-magnify"
                            @update:model-value="searchCustomers"></v-text-field>
                        <v-list v-if="customerResults.length > 0" density="compact" class="mt-2"
                            style="max-height: 200px; overflow-y: auto">
                            <v-list-item v-for="c in customerResults" :key="c.userId"
                                :title="`${c.firstName} ${c.lastName}`" :subtitle="c.email"
                                @click="addCustomer(c)">
                                <template #append>
                                    <v-icon size="small">mdi-plus</v-icon>
                                </template>
                            </v-list-item>
                        </v-list>
                        <div class="text-caption text-medium-emphasis mt-2">
                            Selected ({{ selectedCustomers.length }}):
                        </div>
                        <div class="d-flex flex-wrap ga-1 mt-1">
                            <v-chip v-for="c in selectedCustomers" :key="c.userId" closable size="small"
                                @click:close="removeCustomer(c.userId)">
                                {{ c.firstName }} {{ c.lastName }} ({{ c.email }})
                            </v-chip>
                        </div>
                    </div>

                    <!-- 2. Event purchasers -->
                    <div v-if="audienceType === 'event'">
                        <v-autocomplete v-model="audienceEventId" :items="eventOptions" label="Event" class="mt-6"
                            density="compact" :loading="eventsLoading" auto-select-first clearable
                            placeholder="Type to search by title..."></v-autocomplete>
                    </div>

                    <!-- 3. Timeframe -->
                    <div v-if="audienceType === 'timeframe'" class="d-flex ga-3">
                        <v-text-field v-model="audienceFrom" type="date" label="From" density="compact"></v-text-field>
                        <v-text-field v-model="audienceTo" type="date" label="To" density="compact"></v-text-field>
                    </div>

                    <!-- 4. All customers -->
                    <p v-if="audienceType === 'all_customers'" class="text-caption text-medium-emphasis">
                        Sends to every customer who has completed a paid purchase from your track.
                    </p>

                    <!-- 5. Subscribers -->
                    <p v-if="audienceType === 'subscribers'" class="text-caption text-medium-emphasis">
                        Sends to all active newsletter subscribers (excludes anyone who's unsubscribed).
                    </p>

                    <div class="d-flex align-center mt-4 ga-3">
                        <v-btn variant="tonal" size="small" :loading="previewLoading" @click="previewAudience">
                            Preview recipient count
                        </v-btn>
                        <span v-if="audiencePreview" class="text-body-2">
                            <strong>{{ audiencePreview.count }}</strong> recipient{{ audiencePreview.count === 1 ? '' : 's' }}
                            <span v-if="audiencePreview.sample.length > 0" class="text-medium-emphasis">
                                — e.g. {{ audiencePreview.sample.slice(0, 3).join(', ') }}{{ audiencePreview.sample.length > 3 ? '…' : '' }}
                            </span>
                        </span>
                    </div>

                    <div class="mt-4">
                        <div class="text-subtitle-2 mb-1">Email preview</div>
                        <div v-if="invitePreview" class="text-caption text-medium-emphasis mb-1">
                            <strong>Subject:</strong> {{ invitePreview.subject }}
                        </div>
                        <iframe v-if="invitePreview" :srcdoc="invitePreview.bodyHtml" class="invite-preview-frame"
                            sandbox=""></iframe>
                        <div v-else class="text-caption text-medium-emphasis">Loading preview…</div>
                        <p class="text-caption text-medium-emphasis mt-1">
                            The "Take the survey" button uses a unique link per recipient.
                        </p>
                    </div>

                    <v-alert v-if="sendResult" :type="sendResult.skipped > 0 ? 'warning' : 'success'" variant="tonal"
                        class="mt-2" density="compact">
                        Sent {{ sendResult.sent }} • Skipped {{ sendResult.skipped }}
                        <span v-if="sendResult.skippedEmails.length > 0">
                            ({{ sendResult.skippedEmails.slice(0, 5).join(', ') }}{{ sendResult.skippedEmails.length > 5 ? '…' : '' }})
                        </span>
                    </v-alert>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="sending" @click="sendOpen = false">Close</v-btn>
                    <v-btn color="primary" :loading="sending" :disabled="!canSend" @click="sendInvites">Send</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Survey preview dialog -->
        <v-dialog v-model="previewOpen" max-width="780" scrollable>
            <v-card>
                <v-card-title class="d-flex align-center">
                    Preview
                    <v-spacer></v-spacer>
                    <v-btn icon variant="text" size="small" @click="previewOpen = false">
                        <v-icon>mdi-close</v-icon>
                    </v-btn>
                </v-card-title>
                <SurveyForm v-if="previewOpen && survey" :key="previewKey" :title="survey.title"
                    :description="survey.description" :require-email="survey.requireEmail"
                    :questions="survey.questions" preview-mode />
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, reactive, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import draggable from 'vuedraggable'
import dayjs from 'dayjs'
import {
    SurveyService,
    type SurveyAdminResponse,
    type QuestionKind,
    type SendSurveyInvitesResponse,
    type ChoiceInput,
    type AudienceType,
    type AudienceCriteria,
    type AudiencePreviewResponse,
    type InvitePreviewResponse,
} from '@/services/SurveyService'
import { useDragReorder } from '@/composables/useDragReorder'
import { CustomerService, type CustomerSummaryDto } from '@/services/CustomerService'
import { EventService } from '@/services/EventService'
import SurveyForm from '@/components/SurveyForm.vue'

const route = useRoute()
const router = useRouter()
const service = new SurveyService()
const surveyId = computed(() => route.params.id as string)

const survey = ref<SurveyAdminResponse | null>(null)
const choiceDrafts = reactive<Record<string, ChoiceInput[]>>({})

// Separate ref for the question list so vuedraggable has a mutable Ref<T[]> to
// bind to. Synced from survey.value.questions on load and after every reorder.
type SurveyQuestionRow = SurveyAdminResponse['questions'][number]
const questions = ref<SurveyQuestionRow[]>([])
const { visibleRows: visibleQuestions, onReorderEnd: onQuestionReorderEnd } = useDragReorder<SurveyQuestionRow>({
    rows: questions,
    save: items => service.reorderQuestions(surveyId.value, items),
    onSuccess: () => flash('Order saved.', 'success'),
    onError: async err => {
        flash((err as any)?.response?.data?.error || 'Failed to save order — refreshing.', 'error')
        await load()
    },
})

const meta = ref({
    name: '',
    title: '',
    description: '',
    closesAtUtc: '',
    requireEmail: false,
})
const savingMeta = ref(false)

const addQOpen = ref(false)
const savingQ = ref(false)
const newQ = ref<{ kind: QuestionKind; prompt: string; required: boolean; choices: ChoiceInput[] }>({
    kind: 'single_choice',
    prompt: '',
    required: false,
    choices: [],
})
const kindOptions = [
    { value: 'single_choice', title: 'Single choice (poll)' },
    { value: 'multiple_choice', title: 'Multiple choice' },
    { value: 'free_form', title: 'Free-form text' },
]

const customerService = new CustomerService()
const eventService = new EventService()

const previewOpen = ref(false)
// Bumped each time the dialog opens so SurveyForm remounts and any answers
// the admin clicked into during a previous preview don't persist.
const previewKey = ref(0)
watch(previewOpen, v => { if (v) previewKey.value++ })

const sendOpen = ref(false)
const sending = ref(false)
const sendResult = ref<SendSurveyInvitesResponse | null>(null)
const invitePreview = ref<InvitePreviewResponse | null>(null)

const audienceType = ref<AudienceType>('subscribers')
const audienceTypeOptions = [
    { value: 'custom', title: 'Custom — pick specific customers' },
    { value: 'event', title: 'Customers who bought for a specific event' },
    { value: 'timeframe', title: 'Customers who purchased in a date range' },
    { value: 'all_customers', title: 'All paying customers' },
    { value: 'subscribers', title: 'All newsletter subscribers' },
]
const audienceEventId = ref<string | null>(null)
const audienceFrom = ref('')
const audienceTo = ref('')
const audiencePreview = ref<AudiencePreviewResponse | null>(null)
const previewLoading = ref(false)

const customerSearch = ref('')
const customerResults = ref<CustomerSummaryDto[]>([])
const selectedCustomers = ref<CustomerSummaryDto[]>([])
let customerSearchTimer: ReturnType<typeof setTimeout> | null = null

const eventOptions = ref<{ value: string; title: string }[]>([])
const eventsLoading = ref(false)

const canSend = computed(() => {
    if (audienceType.value === 'custom') return selectedCustomers.value.length > 0
    if (audienceType.value === 'event') return !!audienceEventId.value
    if (audienceType.value === 'timeframe') return !!audienceFrom.value && !!audienceTo.value
    return true
})

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const publicLink = computed(() =>
    survey.value ? `${window.location.origin}/Survey/${survey.value.publicToken}` : '')

onMounted(load)

async function load() {
    try {
        const r = await service.getAdmin(surveyId.value)
        survey.value = (r.data as any).data
        meta.value = {
            name: survey.value!.name,
            title: survey.value!.title,
            description: survey.value!.description ?? '',
            closesAtUtc: survey.value!.closesAtUtc
                ? dayjs.utc(survey.value!.closesAtUtc).local().format('YYYY-MM-DDTHH:mm')
                : '',
            requireEmail: survey.value!.requireEmail,
        }
        for (const q of survey.value!.questions) {
            choiceDrafts[q.id] = q.choices.map(c => ({ label: c.label, allowsFreeText: c.allowsFreeText }))
        }
        questions.value = survey.value!.questions
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load survey.', 'error')
    }
}

async function saveMeta() {
    savingMeta.value = true
    try {
        const r = await service.update(surveyId.value, {
            name: meta.value.name.trim(),
            title: meta.value.title.trim(),
            description: meta.value.description.trim() || null,
            closesAtUtc: meta.value.closesAtUtc
                ? dayjs(meta.value.closesAtUtc).utc().toISOString()
                : null,
            requireEmail: meta.value.requireEmail,
        })
        survey.value = (r.data as any).data
        flash('Saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        savingMeta.value = false
    }
}

async function setStatus(status: 'draft' | 'published' | 'closed') {
    try {
        await service.updateStatus(surveyId.value, status)
        flash(`Survey ${status}.`, 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Update failed.', 'error')
    }
}

function openAddQuestion() {
    newQ.value = { kind: 'single_choice', prompt: '', required: false, choices: [] }
    addQOpen.value = true
}
function addNewChoice() {
    newQ.value.choices.push({ label: '', allowsFreeText: false })
}
function removeNewChoice(idx: number) {
    newQ.value.choices.splice(idx, 1)
}
async function addQuestion() {
    if (!newQ.value.prompt.trim()) {
        flash('Prompt is required.', 'error')
        return
    }
    savingQ.value = true
    try {
        const choices = newQ.value.kind !== 'free_form'
            ? newQ.value.choices
                .map(c => ({ label: c.label.trim(), allowsFreeText: c.allowsFreeText }))
                .filter(c => c.label.length > 0)
            : null
        await service.createQuestion(surveyId.value, {
            kind: newQ.value.kind,
            prompt: newQ.value.prompt.trim(),
            sortOrder: (survey.value!.questions.length + 1) * 10,
            required: newQ.value.required,
            choices,
        })
        addQOpen.value = false
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Add failed.', 'error')
    } finally {
        savingQ.value = false
    }
}

async function saveQuestion(q: SurveyAdminResponse['questions'][number]) {
    try {
        await service.updateQuestion(q.id, {
            prompt: q.prompt.trim(),
            sortOrder: q.sortOrder,
            required: q.required,
        })
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    }
}

async function deleteQuestion(id: string) {
    if (!confirm('Delete this question and any responses to it?')) return
    try {
        await service.deleteQuestion(id)
        await load()
        flash('Question deleted.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    }
}

async function moveQuestion(idx: number, delta: -1 | 1) {
    if (!survey.value) return
    const a = survey.value.questions[idx]
    const b = survey.value.questions[idx + delta]
    if (!a || !b) return
    // Swap sort_order via two updates. Cheap and predictable.
    const aOrder = a.sortOrder
    const bOrder = b.sortOrder
    try {
        await service.updateQuestion(a.id, { prompt: a.prompt, sortOrder: bOrder, required: a.required })
        await service.updateQuestion(b.id, { prompt: b.prompt, sortOrder: aOrder, required: b.required })
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Reorder failed.', 'error')
    }
}

function addChoice(qId: string) {
    if (!choiceDrafts[qId]) choiceDrafts[qId] = []
    choiceDrafts[qId].push({ label: '', allowsFreeText: false })
}
function removeChoice(qId: string, ci: number) {
    choiceDrafts[qId].splice(ci, 1)
}
async function saveChoices(qId: string) {
    const choices = (choiceDrafts[qId] ?? [])
        .map(c => ({ label: c.label.trim(), allowsFreeText: c.allowsFreeText }))
        .filter(c => c.label.length > 0)
    try {
        await service.replaceChoices(qId, choices)
        await load()
        flash('Choices saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    }
}

function openSendDialog() {
    sendResult.value = null
    audiencePreview.value = null
    audienceType.value = 'subscribers'
    audienceEventId.value = null
    audienceFrom.value = ''
    audienceTo.value = ''
    selectedCustomers.value = []
    customerResults.value = []
    customerSearch.value = ''
    invitePreview.value = null
    sendOpen.value = true
    loadInvitePreview()
}

async function loadInvitePreview() {
    try {
        const r = await service.previewInvite(surveyId.value)
        invitePreview.value = (r.data as any).data
    } catch {
        // Preview is best-effort; the rest of the dialog still works.
    }
}

function onAudienceChange() {
    audiencePreview.value = null
    if (audienceType.value === 'event' && eventOptions.value.length === 0) {
        loadEvents()
    }
}

async function loadEvents() {
    eventsLoading.value = true
    try {
        // Wide window — past 2 years through next 2 years catches anything an
        // admin would reasonably survey about.
        const from = dayjs().subtract(2, 'year').utc().toISOString()
        const to = dayjs().add(2, 'year').utc().toISOString()
        const r = await eventService.list(from, to)
        const events = (r.data as any).data ?? []
        eventOptions.value = events
            .sort((a: any, b: any) => (b.startsAtUtc ?? '').localeCompare(a.startsAtUtc ?? ''))
            .map((e: any) => ({
                value: e.id,
                title: `${e.title} (${e.startsAtUtc ? dayjs.utc(e.startsAtUtc).format('MMM D, YYYY') : 'no date'})`,
            }))
    } finally {
        eventsLoading.value = false
    }
}

function searchCustomers() {
    if (customerSearchTimer) clearTimeout(customerSearchTimer)
    const q = customerSearch.value.trim()
    if (q.length < 2) { customerResults.value = []; return }
    customerSearchTimer = setTimeout(async () => {
        const r = await customerService.list(q, 25, 0)
        const all = (r.data as any).data?.items ?? []
        const selectedIds = new Set(selectedCustomers.value.map(c => c.userId))
        customerResults.value = all.filter((c: CustomerSummaryDto) => !selectedIds.has(c.userId))
    }, 250)
}
function addCustomer(c: CustomerSummaryDto) {
    if (selectedCustomers.value.some(x => x.userId === c.userId)) return
    selectedCustomers.value.push(c)
    customerResults.value = customerResults.value.filter(x => x.userId !== c.userId)
    audiencePreview.value = null
}
function removeCustomer(userId: string) {
    selectedCustomers.value = selectedCustomers.value.filter(c => c.userId !== userId)
    audiencePreview.value = null
}

function buildCriteria(): AudienceCriteria {
    if (audienceType.value === 'custom') {
        return { type: 'custom', emails: selectedCustomers.value.map(c => c.email) }
    }
    if (audienceType.value === 'event') {
        return { type: 'event', eventId: audienceEventId.value }
    }
    if (audienceType.value === 'timeframe') {
        return {
            type: 'timeframe',
            fromUtc: dayjs(audienceFrom.value).utc().toISOString(),
            // Half-open: include the end date by adding 1 day.
            toUtc: dayjs(audienceTo.value).add(1, 'day').utc().toISOString(),
        }
    }
    return { type: audienceType.value }
}

async function previewAudience() {
    if (!canSend.value) {
        flash('Pick the audience details first.', 'error')
        return
    }
    previewLoading.value = true
    try {
        const r = await service.previewAudience(surveyId.value, buildCriteria())
        audiencePreview.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Preview failed.', 'error')
    } finally {
        previewLoading.value = false
    }
}

async function sendInvites() {
    if (!canSend.value) {
        flash('Pick the audience details first.', 'error')
        return
    }
    sending.value = true
    try {
        const r = await service.sendInvites(surveyId.value, {
            audience: buildCriteria(),
        })
        sendResult.value = (r.data as any).data
        flash(`Sent ${sendResult.value!.sent} invites.`, 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Send failed.', 'error')
    } finally {
        sending.value = false
    }
}

function goResults() { router.push(`/Admin/Surveys/${surveyId.value}/Results`) }

function kindLabel(kind: QuestionKind) {
    return kind === 'single_choice' ? 'Single choice'
        : kind === 'multiple_choice' ? 'Multiple choice'
            : 'Free-form'
}
function statusColor(s: string) {
    return s === 'published' ? 'success' : s === 'closed' ? 'grey' : 'warning'
}
function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>

<style scoped>
.invite-preview-frame {
    width: 100%;
    height: 320px;
    border: 1px solid rgba(0, 0, 0, 0.12);
    border-radius: 4px;
    background: #fafafa;
}
.question-drag-handle,
.choice-drag-handle { cursor: grab; }
.question-drag-handle:active,
.choice-drag-handle:active { cursor: grabbing; }
.drag-ghost { opacity: 0.35; background: rgba(25, 118, 210, 0.08); }
</style>
