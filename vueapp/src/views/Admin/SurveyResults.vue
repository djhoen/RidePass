<template>
    <v-container v-if="results">
        <div class="d-flex align-center mb-4 flex-wrap ga-3">
            <v-btn variant="text" size="small" @click="$router.push('/Admin/Surveys')">
                <v-icon>mdi-arrow-left</v-icon> Back
            </v-btn>
            <h1 class="text-h5">{{ results.title }}</h1>
            <v-chip size="small" :color="statusColor(results.status)">{{ results.status }}</v-chip>
            <v-spacer></v-spacer>
            <v-btn variant="text" size="small" @click="$router.push(`/Admin/Surveys/${id}`)">Edit survey</v-btn>
            <v-btn variant="text" size="small" @click="load">Refresh</v-btn>
        </div>

        <v-row class="mb-4">
            <v-col cols="6" md="3">
                <v-card variant="outlined" class="pa-3 text-center">
                    <div class="text-h5">{{ results.responseCount }}</div>
                    <div class="text-caption text-medium-emphasis">Responses</div>
                </v-card>
            </v-col>
            <v-col cols="6" md="3">
                <v-card variant="outlined" class="pa-3 text-center">
                    <div class="text-h5">{{ results.inviteSent }}</div>
                    <div class="text-caption text-medium-emphasis">Invites sent</div>
                </v-card>
            </v-col>
            <v-col cols="6" md="3">
                <v-card variant="outlined" class="pa-3 text-center">
                    <div class="text-h5">{{ results.inviteOpened }}</div>
                    <div class="text-caption text-medium-emphasis">Opened</div>
                </v-card>
            </v-col>
            <v-col cols="6" md="3">
                <v-card variant="outlined" class="pa-3 text-center">
                    <div class="text-h5">{{ results.inviteCompleted }}</div>
                    <div class="text-caption text-medium-emphasis">Completed</div>
                </v-card>
            </v-col>
        </v-row>

        <v-card v-for="q in results.questions" :key="q.questionId" class="mb-3">
            <v-card-text>
                <div class="d-flex align-center mb-2 flex-wrap ga-2">
                    <v-chip size="x-small" color="primary" variant="tonal">{{ kindLabel(q.kind) }}</v-chip>
                    <span class="text-caption text-medium-emphasis">
                        {{ q.answeredCount }} answered
                    </span>
                </div>
                <div class="text-h6 mb-3">{{ q.prompt }}</div>

                <div v-if="q.kind !== 'free_form'">
                    <div v-if="q.choiceResults.length === 0" class="text-medium-emphasis">
                        No choices configured.
                    </div>
                    <div v-for="c in q.choiceResults" :key="c.choiceId" class="mb-2">
                        <div class="d-flex justify-space-between text-body-2">
                            <span>
                                {{ c.label }}
                                <v-chip v-if="c.allowsFreeText" size="x-small" variant="tonal" class="ml-2">
                                    Other
                                </v-chip>
                            </span>
                            <span class="text-medium-emphasis">{{ c.count }} ({{ c.percent.toFixed(1) }}%)</span>
                        </div>
                        <v-progress-linear :model-value="c.percent" height="14" rounded color="primary"></v-progress-linear>
                        <v-list v-if="c.freeTextAnswers.length > 0" density="compact" class="mt-1 pl-2">
                            <v-list-item v-for="(text, i) in c.freeTextAnswers" :key="i"
                                class="text-caption text-medium-emphasis">
                                <v-list-item-title style="white-space: pre-wrap">— {{ text }}</v-list-item-title>
                            </v-list-item>
                        </v-list>
                    </div>
                </div>

                <div v-else>
                    <div v-if="q.freeFormAnswers.length === 0" class="text-medium-emphasis">
                        No responses yet.
                    </div>
                    <v-list v-else density="compact">
                        <v-list-item v-for="(text, i) in q.freeFormAnswers" :key="i">
                            <v-list-item-title style="white-space: pre-wrap">{{ text }}</v-list-item-title>
                        </v-list-item>
                    </v-list>
                </div>
            </v-card-text>
        </v-card>

        <v-card v-if="invites.length > 0" class="mt-4">
            <v-card-title>Invites</v-card-title>
            <v-table density="compact">
                <thead>
                    <tr>
                        <th>Email</th>
                        <th>Sent</th>
                        <th>Opened</th>
                        <th>Completed</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="i in invites" :key="i.id">
                        <td>{{ i.email }}</td>
                        <td>{{ i.sentAtUtc ? formatDate(i.sentAtUtc) : '—' }}</td>
                        <td>{{ i.openedAtUtc ? formatDate(i.openedAtUtc) : '—' }}</td>
                        <td>{{ i.completedAtUtc ? formatDate(i.completedAtUtc) : '—' }}</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>
    </v-container>
    <v-container v-else-if="loadError">
        <v-alert type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import { SurveyService, type SurveyResultsResponse, type SurveyInviteDto, type QuestionKind } from '@/services/SurveyService'
import { branding } from '@/stores/branding'

const route = useRoute()
const service = new SurveyService()
const id = computed(() => route.params.id as string)

const results = ref<SurveyResultsResponse | null>(null)
const invites = ref<SurveyInviteDto[]>([])
const loadError = ref<string | null>(null)

onMounted(load)

async function load() {
    loadError.value = null
    try {
        const [r1, r2] = await Promise.all([
            service.results(id.value),
            service.listInvites(id.value),
        ])
        results.value = (r1.data as any).data
        invites.value = (r2.data as any).data
    } catch (err: any) {
        loadError.value = err.response?.data?.error ?? 'Couldn’t load survey results. Refresh to try again.'
    }
}

function kindLabel(kind: QuestionKind) {
    return kind === 'single_choice' ? 'Single choice'
        : kind === 'multiple_choice' ? 'Multiple choice'
            : 'Free-form'
}
function statusColor(s: string) {
    return s === 'published' ? 'success' : s === 'closed' ? 'grey' : 'warning'
}
function formatDate(utc: string) {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('MMM D, h:mm A')
}
</script>
