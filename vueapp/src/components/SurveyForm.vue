<template>
    <v-card-text>
        <h1 class="text-h4 mb-2">{{ title }}</h1>
        <p v-if="description" class="text-body-1 mb-4 text-medium-emphasis" style="white-space: pre-wrap">
            {{ description }}
        </p>

        <v-alert v-if="previewMode" type="info" variant="tonal" class="mb-4" density="compact">
            This is a preview — responses won't be saved.
        </v-alert>

        <v-alert v-if="alreadyCompleted" type="info" variant="tonal" class="mb-4" density="compact">
            You've already submitted this survey — you can submit again, but only the latest response counts.
        </v-alert>

        <!-- Respondent info: shown when required, OR when not on a per-recipient invite. -->
        <v-card v-if="needsRespondent" variant="outlined" class="pa-3 mb-4">
            <v-text-field v-model="respondentName" label="Your name" density="compact"
                :rules="[v => !!v || 'Name is required']"></v-text-field>
            <v-text-field v-model="respondentEmail" label="Your email" type="email" density="compact" class="mt-6"
                :readonly="!!inviteEmail" :rules="[v => !!v || 'Email is required']"></v-text-field>
            <p v-if="inviteEmail" class="text-caption text-medium-emphasis">
                We're recording your response under {{ inviteEmail }}.
            </p>
        </v-card>

        <!-- Questions -->
        <div v-for="(q, idx) in questions" :key="q.id" class="mb-5">
            <div class="text-subtitle-1 mb-1">
                {{ idx + 1 }}. {{ q.prompt }}
                <span v-if="q.required" class="text-error">*</span>
            </div>

            <template v-if="q.kind === 'single_choice'">
                <v-radio-group v-model="answers[q.id]" hide-details>
                    <v-radio v-for="c in q.choices" :key="c.id" :label="c.label" :value="c.id"></v-radio>
                </v-radio-group>
                <v-textarea v-if="showOtherInput(q)" v-model="otherText[q.id]" rows="2" auto-grow density="compact"
                    class="mt-2" placeholder="Please explain..." :label="otherInputLabel(q)"></v-textarea>
            </template>

            <template v-else-if="q.kind === 'multiple_choice'">
                <div class="d-flex flex-column">
                    <v-checkbox v-for="c in q.choices" :key="c.id" :label="c.label" v-model="multiAnswers[q.id]"
                        :value="c.id" density="compact" hide-details></v-checkbox>
                </div>
                <v-textarea v-if="showOtherInput(q)" v-model="otherText[q.id]" rows="2" auto-grow density="compact"
                    class="mt-2" placeholder="Please explain..." :label="otherInputLabel(q)"></v-textarea>
            </template>

            <template v-else>
                <v-textarea v-model="freeAnswers[q.id]" rows="2" auto-grow density="compact"
                    placeholder="Your answer..."></v-textarea>
            </template>
        </div>

        <v-alert v-if="errorText" type="error" variant="tonal" class="mb-3" density="compact">
            {{ errorText }}
        </v-alert>

        <v-btn v-if="!previewMode" color="primary" :loading="submitting" @click="onSubmit">Submit</v-btn>
    </v-card-text>
</template>

<script setup lang="ts">
import { ref, computed, reactive, watch } from 'vue'
import type { SurveyQuestionDto } from '@/services/SurveyService'

interface SubmitAnswer {
    questionId: string
    choiceIds: string[]
    freeText: string | null
}
interface SubmitPayload {
    respondentName: string | null
    respondentEmail: string | null
    answers: SubmitAnswer[]
}

const props = defineProps<{
    title: string
    description: string | null
    requireEmail: boolean
    questions: SurveyQuestionDto[]
    inviteEmail?: string | null
    alreadyCompleted?: boolean | null
    /**
     * When true, the form is fully interactive (the admin can click radios,
     * type, etc) but Submit is disabled and a banner explains responses won't
     * be saved.
     */
    previewMode?: boolean
    /**
     * False when no per-recipient invite was used — i.e. anonymous public link.
     * Drives the "show name/email fields" rule along with requireEmail.
     */
    hasInviteToken?: boolean
    submitting?: boolean
    errorText?: string
}>()

const emit = defineEmits<{
    (e: 'submit', payload: SubmitPayload): void
}>()

const respondentName = ref('')
const respondentEmail = ref(props.inviteEmail ?? '')

watch(() => props.inviteEmail, v => { if (v) respondentEmail.value = v })

const answers = reactive<Record<string, string | null>>({})
const multiAnswers = reactive<Record<string, string[]>>({})
const freeAnswers = reactive<Record<string, string>>({})
const otherText = reactive<Record<string, string>>({})

watch(() => props.questions, qs => {
    for (const q of qs ?? []) {
        if (q.kind === 'single_choice' && !(q.id in answers)) answers[q.id] = null
        else if (q.kind === 'multiple_choice' && !(q.id in multiAnswers)) multiAnswers[q.id] = []
        else if (q.kind === 'free_form' && !(q.id in freeAnswers)) freeAnswers[q.id] = ''
        if (!(q.id in otherText)) otherText[q.id] = ''
    }
}, { immediate: true })

const needsRespondent = computed(() => {
    if (props.requireEmail) return true
    return !props.hasInviteToken
})

function showOtherInput(q: SurveyQuestionDto): boolean {
    const otherChoiceIds = q.choices.filter(c => c.allowsFreeText).map(c => c.id)
    if (otherChoiceIds.length === 0) return false
    if (q.kind === 'single_choice') {
        return !!answers[q.id] && otherChoiceIds.includes(answers[q.id]!)
    }
    if (q.kind === 'multiple_choice') {
        return (multiAnswers[q.id] ?? []).some(id => otherChoiceIds.includes(id))
    }
    return false
}

function otherInputLabel(q: SurveyQuestionDto): string {
    const picked = q.choices.find(c => c.allowsFreeText &&
        (q.kind === 'single_choice'
            ? answers[q.id] === c.id
            : (multiAnswers[q.id] ?? []).includes(c.id)))
    return picked ? `${picked.label} — please explain` : 'Please explain'
}

function onSubmit() {
    const payload: SubmitPayload = {
        respondentName: respondentName.value.trim() || null,
        respondentEmail: respondentEmail.value.trim() || null,
        answers: props.questions.map(q => {
            const other = showOtherInput(q) ? (otherText[q.id] ?? '').trim() || null : null
            if (q.kind === 'single_choice') {
                return { questionId: q.id, choiceIds: answers[q.id] ? [answers[q.id]!] : [], freeText: other }
            }
            if (q.kind === 'multiple_choice') {
                return { questionId: q.id, choiceIds: multiAnswers[q.id] ?? [], freeText: other }
            }
            return { questionId: q.id, choiceIds: [], freeText: (freeAnswers[q.id] ?? '').trim() || null }
        }),
    }
    emit('submit', payload)
}
</script>
