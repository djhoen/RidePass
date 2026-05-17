<template>
    <v-container class="py-8" max-width="780">
        <v-card v-if="survey && !submitted">
            <SurveyForm :title="survey.title" :description="survey.description" :require-email="survey.requireEmail"
                :questions="survey.questions" :invite-email="survey.inviteEmail"
                :already-completed="survey.alreadyCompleted" :has-invite-token="!!survey.inviteToken"
                :submitting="submitting" :error-text="errorText" @submit="onSubmit" />
        </v-card>

        <v-card v-if="submitted" class="text-center pa-8">
            <v-icon size="64" color="success">mdi-check-circle</v-icon>
            <h2 class="text-h5 mt-3">Thanks for your response!</h2>
            <p class="text-medium-emphasis mt-1">Your answers have been recorded.</p>
        </v-card>

        <v-alert v-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { SurveyService, type PublicSurveyResponse } from '@/services/SurveyService'
import SurveyForm from '@/components/SurveyForm.vue'

const route = useRoute()
const service = new SurveyService()
const token = computed(() => route.params.token as string)

const survey = ref<PublicSurveyResponse | null>(null)
const loadError = ref('')
const submitted = ref(false)
const submitting = ref(false)
const errorText = ref('')

onMounted(async () => {
    try {
        const r = await service.getPublic(token.value)
        survey.value = (r.data as any).data
    } catch (err: any) {
        loadError.value = err.response?.data?.error || 'This survey is not available.'
    }
})

async function onSubmit(payload: {
    respondentName: string | null
    respondentEmail: string | null
    answers: { questionId: string; choiceIds: string[]; freeText: string | null }[]
}) {
    if (!survey.value) return
    errorText.value = ''

    // Lightweight pre-flight: required questions, required email. Server still
    // validates, but failing locally avoids a round-trip on obvious gaps.
    const needsRespondent = survey.value.requireEmail || !survey.value.inviteToken
    if (needsRespondent && (!payload.respondentName || !payload.respondentEmail)) {
        errorText.value = 'Please enter your name and email.'
        return
    }
    for (const q of survey.value.questions) {
        if (!q.required) continue
        const a = payload.answers.find(x => x.questionId === q.id)
        const answered = q.kind === 'free_form'
            ? !!a?.freeText?.trim()
            : (a?.choiceIds?.length ?? 0) > 0
        if (!answered) { errorText.value = `Please answer: ${q.prompt}`; return }
    }

    submitting.value = true
    try {
        await service.submitPublic(token.value, {
            respondentName: payload.respondentName,
            respondentEmail: payload.respondentEmail,
            inviteToken: survey.value.inviteToken,
            answers: payload.answers,
        })
        submitted.value = true
    } catch (err: any) {
        errorText.value = err.response?.data?.error || 'Submit failed. Please try again.'
    } finally {
        submitting.value = false
    }
}
</script>
