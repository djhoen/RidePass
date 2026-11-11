<template>
    <!-- A survey's questions as inputs (single choice, multiple choice, free form, "Other — please
         explain"). Shared by the public survey page (SurveyForm) and the lesson self-assessment at checkout. -->
    <div>
        <div v-for="(q, idx) in questions" :key="q.id" :class="dense ? 'mb-3' : 'mb-5'">
            <div :class="dense ? 'text-body-2 font-weight-medium mb-1' : 'text-subtitle-1 mb-1'">
                {{ idx + 1 }}. {{ q.prompt }}
                <span v-if="q.required" class="text-error">*</span>
            </div>

            <template v-if="q.kind === 'single_choice'">
                <v-radio-group v-model="answers[q.id]" hide-details :density="dense ? 'compact' : undefined">
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
    </div>
</template>

<script setup lang="ts">
import { reactive, watch } from 'vue'
import type { SurveyQuestionDto } from '@/services/SurveyService'
import type { SurveyAnswerInput } from '@/helpers/SurveyAnswers'

const props = defineProps<{
    questions: SurveyQuestionDto[]
    /** Output only: the component owns the inputs and emits the collected answers. */
    modelValue?: SurveyAnswerInput[]
    /** Tighter spacing, for a survey embedded in another form (checkout). */
    dense?: boolean
}>()

// v-model: the answers in the submit endpoints' shape, one per question, kept in sync as the rider types.
const emit = defineEmits<{ (e: 'update:modelValue', value: SurveyAnswerInput[]): void }>()

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

function collect(): SurveyAnswerInput[] {
    return props.questions.map(q => {
        const other = showOtherInput(q) ? (otherText[q.id] ?? '').trim() || null : null
        if (q.kind === 'single_choice') {
            return { questionId: q.id, choiceIds: answers[q.id] ? [answers[q.id]!] : [], freeText: other }
        }
        if (q.kind === 'multiple_choice') {
            return { questionId: q.id, choiceIds: [...(multiAnswers[q.id] ?? [])], freeText: other }
        }
        return { questionId: q.id, choiceIds: [], freeText: (freeAnswers[q.id] ?? '').trim() || null }
    })
}

watch([answers, multiAnswers, freeAnswers, otherText, () => props.questions],
    () => emit('update:modelValue', collect()), { deep: true, immediate: true })
</script>
