import axios from 'axios'

export type SurveyStatus = 'draft' | 'published' | 'closed'
export type QuestionKind = 'single_choice' | 'multiple_choice' | 'free_form'

export interface SurveyChoiceDto {
    id: string
    label: string
    sortOrder: number
    allowsFreeText: boolean
}

export interface ChoiceInput {
    label: string
    allowsFreeText: boolean
}

export interface SurveyQuestionDto {
    id: string
    kind: QuestionKind
    prompt: string
    sortOrder: number
    required: boolean
    choices: SurveyChoiceDto[]
}

export interface SurveyListItem {
    id: string
    name: string
    title: string
    status: SurveyStatus
    closesAtUtc: string | null
    publicToken: string
    questionCount: number
    responseCount: number
    createdAtUtc: string
}

export interface SurveyAdminResponse {
    id: string
    name: string
    title: string
    description: string | null
    status: SurveyStatus
    closesAtUtc: string | null
    requireEmail: boolean
    publicToken: string
    createdAtUtc: string
    updatedAtUtc: string
    questions: SurveyQuestionDto[]
}

export interface PublicSurveyResponse {
    id: string
    title: string
    description: string | null
    status: SurveyStatus
    requireEmail: boolean
    closesAtUtc: string | null
    inviteToken: string | null
    inviteEmail: string | null
    alreadyCompleted: boolean | null
    questions: SurveyQuestionDto[]
}

export interface CreateSurveyRequest {
    name: string
    title: string
    description: string | null
    closesAtUtc: string | null
    requireEmail: boolean
}
export type UpdateSurveyRequest = CreateSurveyRequest

export interface CreateQuestionRequest {
    kind: QuestionKind
    prompt: string
    sortOrder: number
    required: boolean
    choices?: ChoiceInput[] | null
}

export interface UpdateQuestionRequest {
    prompt: string
    sortOrder: number
    required: boolean
}

export interface SubmitSurveyAnswer {
    questionId: string
    choiceIds?: string[] | null
    freeText?: string | null
}

export interface SubmitSurveyRequest {
    respondentName: string | null
    respondentEmail: string | null
    inviteToken: string | null
    answers: SubmitSurveyAnswer[]
}

export type AudienceType = 'custom' | 'event' | 'timeframe' | 'all_customers' | 'subscribers'

export interface AudienceCriteria {
    type: AudienceType
    emails?: string[] | null      // for 'custom'
    eventId?: string | null       // for 'event'
    fromUtc?: string | null       // for 'timeframe'
    toUtc?: string | null         // for 'timeframe'
}

export interface AudiencePreviewResponse {
    count: number
    sample: string[]
}

export interface SendSurveyInvitesRequest {
    audience: AudienceCriteria
}

export interface InvitePreviewResponse {
    subject: string
    bodyHtml: string
}

export interface SendSurveyInvitesResponse {
    sent: number
    skipped: number
    skippedEmails: string[]
}

export interface SurveyChoiceResult {
    choiceId: string
    label: string
    count: number
    percent: number
    allowsFreeText: boolean
    freeTextAnswers: string[]
}

export interface SurveyQuestionResult {
    questionId: string
    kind: QuestionKind
    prompt: string
    answeredCount: number
    choiceResults: SurveyChoiceResult[]
    freeFormAnswers: string[]
}

export interface SurveyResultsResponse {
    id: string
    title: string
    status: SurveyStatus
    responseCount: number
    inviteSent: number
    inviteOpened: number
    inviteCompleted: number
    questions: SurveyQuestionResult[]
}

export interface SurveyInviteDto {
    id: string
    email: string
    sentAtUtc: string | null
    openedAtUtc: string | null
    completedAtUtc: string | null
    createdAtUtc: string
}

export class SurveyService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    listAdmin() {
        return axios.get<{ data: SurveyListItem[] }>(`${this.apiUrl}/Survey/Admin`)
    }
    getAdmin(id: string) {
        return axios.get<{ data: SurveyAdminResponse }>(`${this.apiUrl}/Survey/Admin/${id}`)
    }
    create(req: CreateSurveyRequest) {
        return axios.post<{ data: SurveyAdminResponse }>(`${this.apiUrl}/Survey/Admin`, req)
    }
    update(id: string, req: UpdateSurveyRequest) {
        return axios.put<{ data: SurveyAdminResponse }>(`${this.apiUrl}/Survey/Admin/${id}`, req)
    }
    updateStatus(id: string, status: SurveyStatus) {
        return axios.put<{ data: { id: string; status: SurveyStatus } }>(
            `${this.apiUrl}/Survey/Admin/${id}/Status`, { status })
    }

    createQuestion(surveyId: string, req: CreateQuestionRequest) {
        return axios.post<{ data: SurveyQuestionDto }>(`${this.apiUrl}/Survey/Admin/${surveyId}/Questions`, req)
    }
    updateQuestion(questionId: string, req: UpdateQuestionRequest) {
        return axios.put<{ data: SurveyQuestionDto }>(`${this.apiUrl}/Survey/Admin/Questions/${questionId}`, req)
    }
    deleteQuestion(questionId: string) {
        return axios.delete(`${this.apiUrl}/Survey/Admin/Questions/${questionId}`)
    }
    replaceChoices(questionId: string, choices: ChoiceInput[]) {
        return axios.put<{ data: SurveyQuestionDto }>(
            `${this.apiUrl}/Survey/Admin/Questions/${questionId}/Choices`, { choices })
    }
    reorderQuestions(surveyId: string, items: { id: string; sortOrder: number }[]) {
        return axios.post(`${this.apiUrl}/Survey/Admin/${surveyId}/Questions/Reorder`, { items })
    }
    reorderChoices(questionId: string, items: { id: string; sortOrder: number }[]) {
        return axios.post(`${this.apiUrl}/Survey/Admin/Questions/${questionId}/Choices/Reorder`, { items })
    }

    listInvites(id: string) {
        return axios.get<{ data: SurveyInviteDto[] }>(`${this.apiUrl}/Survey/Admin/${id}/Invites`)
    }
    sendInvites(id: string, req: SendSurveyInvitesRequest) {
        return axios.post<{ data: SendSurveyInvitesResponse }>(`${this.apiUrl}/Survey/Admin/${id}/Send`, req)
    }
    previewAudience(id: string, criteria: AudienceCriteria) {
        return axios.post<{ data: AudiencePreviewResponse }>(
            `${this.apiUrl}/Survey/Admin/${id}/Audience/Preview`, criteria)
    }
    previewInvite(id: string) {
        return axios.get<{ data: InvitePreviewResponse }>(`${this.apiUrl}/Survey/Admin/${id}/InvitePreview`)
    }
    results(id: string) {
        return axios.get<{ data: SurveyResultsResponse }>(`${this.apiUrl}/Survey/Admin/${id}/Results`)
    }

    getPublic(token: string) {
        return axios.get<{ data: PublicSurveyResponse }>(`${this.apiUrl}/Survey/Public/${token}`)
    }
    submitPublic(token: string, req: SubmitSurveyRequest) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/Survey/Public/${token}/Submit`, req)
    }
}
