import axios from 'axios'

export interface Instructor {
    id: string
    name: string
    email: string | null
    phone: string | null
    bio: string | null
    imageUrl: string | null
    isActive: boolean
    sortOrder: number
    // How many students this coach takes in one session; caps a training group alongside
    // the group's own inventory.
    maxStudentsPerSession: number
}

export interface UpsertInstructor {
    name: string
    email: string | null
    phone: string | null
    bio: string | null
    imageUrl: string | null
    isActive: boolean
    sortOrder: number
    maxStudentsPerSession: number
}

export class InstructorService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    // Public: active instructors (lesson detail / discovery).
    listActive() {
        return axios.get<{ data: Instructor[] }>(`${this.apiUrl}/Instructor`)
    }
    // Admin: all instructors incl. inactive.
    listForAdmin() {
        return axios.get<{ data: Instructor[] }>(`${this.apiUrl}/Instructor/Admin`)
    }
    create(req: UpsertInstructor) {
        return axios.post<{ data: Instructor }>(`${this.apiUrl}/Instructor`, req)
    }
    update(id: string, req: UpsertInstructor) {
        return axios.put<{ data: Instructor }>(`${this.apiUrl}/Instructor/${id}`, req)
    }
    remove(id: string) {
        return axios.delete(`${this.apiUrl}/Instructor/${id}`)
    }
}
