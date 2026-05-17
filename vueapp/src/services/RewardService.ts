import axios from 'axios'

export interface RewardProgram {
    id: string
    name: string
    description: string | null
    enrollmentMode: 'auto' | 'opt_in'
    requirementKind: 'pass' | 'event_ticket' | 'any'
    requirementCount: number
    rewardPercentOff: number
    proximityEmailThreshold: number | null
    isActive: boolean
    createdAtUtc: string
}

export interface UpsertRewardProgram {
    name: string
    description: string | null
    enrollmentMode: 'auto' | 'opt_in'
    requirementKind: 'pass' | 'event_ticket' | 'any'
    requirementCount: number
    rewardPercentOff: number
    proximityEmailThreshold: number | null
    isActive: boolean
}

export interface RiderRewardProgram {
    programId: string
    name: string
    description: string | null
    enrollmentMode: 'auto' | 'opt_in'
    requirementKind: 'pass' | 'event_ticket' | 'any'
    requirementCount: number
    rewardPercentOff: number
    isEnrolled: boolean
    progress: number
    remainingForReward: number
    enrolledAtUtc: string | null
}

export interface RiderRewardRedemption {
    id: string
    programId: string
    programName: string
    rewardPercentOff: number
    earnedAtUtc: string
    redeemedAtUtc: string | null
}

export class RewardService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    listProgramsAdmin() {
        return axios.get<{ data: RewardProgram[] }>(`${this.apiUrl}/Reward/Programs/Admin`)
    }
    createProgram(body: UpsertRewardProgram) {
        return axios.post<{ data: RewardProgram }>(`${this.apiUrl}/Reward/Programs`, body)
    }
    updateProgram(id: string, body: UpsertRewardProgram) {
        return axios.put<{ data: RewardProgram }>(`${this.apiUrl}/Reward/Programs/${id}`, body)
    }
    deleteProgram(id: string) {
        return axios.delete(`${this.apiUrl}/Reward/Programs/${id}`)
    }

    listMyPrograms() {
        return axios.get<{ data: RiderRewardProgram[] }>(`${this.apiUrl}/Reward/Mine`)
    }
    listMyRedemptions() {
        return axios.get<{ data: RiderRewardRedemption[] }>(`${this.apiUrl}/Reward/MyRedemptions`)
    }
    listRiderRedemptions(riderUserId: string) {
        return axios.get<{ data: RiderRewardRedemption[] }>(`${this.apiUrl}/Reward/Riders/${riderUserId}/Redemptions`)
    }
    enroll(programId: string) {
        return axios.post(`${this.apiUrl}/Reward/Programs/${programId}/Enroll`)
    }
    unenroll(programId: string) {
        return axios.post(`${this.apiUrl}/Reward/Programs/${programId}/Unenroll`)
    }
}
