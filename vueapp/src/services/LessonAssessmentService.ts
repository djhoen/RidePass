import axios from 'axios'
import type { SurveyQuestionDto } from '@/services/SurveyService'

// Rider self-assessment at lesson checkout (Script0318): the survey an event's type asks once per
// rider ticket, and the answers stored on each ticket (read by admins with Reports access).
export interface LessonAssessmentSurvey {
    surveyId: string
    title: string
    description: string | null
    questions: SurveyQuestionDto[]
}

export interface AssessmentSurveyOption {
    id: string
    name: string
    title: string
    status: string
}

export interface TicketAssessment {
    ticketId: string
    riderName: string | null
    surveyTitle: string
    submittedAtUtc: string
    answers: { prompt: string; answer: string }[]
}

export class LessonAssessmentService {
    private apiUrl: string

    constructor() {
        this.apiUrl = (import.meta as any).env?.VITE_API_ENDPOINT ?? ''
    }

    /** The event's self-assessment survey; data is null when its type asks none. */
    forEvent(eventId: string) {
        return axios.get<{ data: LessonAssessmentSurvey | null }>(`${this.apiUrl}/LessonAssessment/Event/${eventId}`)
    }

    /** Surveys an admin can pick on the Event Types page. */
    surveys() {
        return axios.get<{ data: AssessmentSurveyOption[] }>(`${this.apiUrl}/LessonAssessment/Surveys`)
    }

    /** Which of these tickets have a self-assessment on file. */
    has(ticketIds: string[]) {
        return axios.post<{ data: string[] }>(`${this.apiUrl}/LessonAssessment/Has`, { ticketIds })
    }

    answers(ticketIds: string[]) {
        return axios.post<{ data: TicketAssessment[] }>(`${this.apiUrl}/LessonAssessment/Answers`, { ticketIds })
    }
}
