import type { SurveyQuestionDto } from '@/services/SurveyService'

// One question's answer as the survey submit endpoints take it (single/multiple choice picks,
// free text, or a picked "Other" choice's explanation).
export interface SurveyAnswerInput {
    questionId: string
    choiceIds: string[]
    freeText: string | null
}

/** The first required question left unanswered, or null. The server checks the same rule. */
export function firstUnanswered(questions: SurveyQuestionDto[], answers: SurveyAnswerInput[]): SurveyQuestionDto | null {
    for (const q of questions) {
        if (!q.required) continue
        const a = answers.find(x => x.questionId === q.id)
        const answered = q.kind === 'free_form'
            ? !!a?.freeText?.trim()
            : (a?.choiceIds?.length ?? 0) > 0
        if (!answered) return q
    }
    return null
}
