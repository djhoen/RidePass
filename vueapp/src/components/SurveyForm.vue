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

        <!-- Questions (shared with the lesson self-assessment at checkout) -->
        <SurveyQuestionFields :questions="questions" v-model="answerList" />

        <v-alert v-if="errorText" type="error" variant="tonal" class="mb-3" density="compact">
            {{ errorText }}
        </v-alert>

        <v-btn v-if="!previewMode" color="primary" :loading="submitting" @click="onSubmit">Submit</v-btn>
    </v-card-text>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import type { SurveyQuestionDto } from '@/services/SurveyService'
import type { SurveyAnswerInput } from '@/helpers/SurveyAnswers'
import SurveyQuestionFields from '@/components/SurveyQuestionFields.vue'

interface SubmitPayload {
    respondentName: string | null
    respondentEmail: string | null
    answers: SurveyAnswerInput[]
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

const answerList = ref<SurveyAnswerInput[]>([])

const needsRespondent = computed(() => {
    if (props.requireEmail) return true
    return !props.hasInviteToken
})

function onSubmit() {
    emit('submit', {
        respondentName: respondentName.value.trim() || null,
        respondentEmail: respondentEmail.value.trim() || null,
        answers: answerList.value,
    })
}
</script>
