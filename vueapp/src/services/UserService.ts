import axios from 'axios'

export class UserService {
    private apiUrl: string;

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT;
    }

    async login(req: any) {
        return axios.post(`${this.apiUrl}/User/Login`, req);
    }

    async createAccount(req: any) {
        return axios.post(`${this.apiUrl}/User/CreateAccount`, req);
    }

    async getProfile() {
        return axios.get(`${this.apiUrl}/User/Profile`);
    }

    async updateProfile(req: any) {
        return axios.post(`${this.apiUrl}/User/UpdateProfile`, req);
    }

    async updatePassword(req: any) {
        return axios.post(`${this.apiUrl}/User/UpdatePassword`, req);
    }

    async resetPassword(req: any) {
        return axios.post(`${this.apiUrl}/User/ResetPassword`, req);
    }

    // Tenant user management
    listTenantUsers() {
        return axios.get<{ data: TenantUserListItem[] }>(`${this.apiUrl}/User/Tenant`)
    }

    createTenantUser(req: { email: string; firstName: string; lastName: string; role: string }) {
        return axios.post<{ data: CreateTenantUserResponse }>(`${this.apiUrl}/User/Tenant`, req)
    }

    updateTenantUserRole(id: string, role: string) {
        return axios.put(`${this.apiUrl}/User/Tenant/${id}/Role`, { role })
    }

    updateTenantUserStatus(id: string, status: 'active' | 'disabled') {
        return axios.put(`${this.apiUrl}/User/Tenant/${id}/Status`, { status })
    }

    resetTenantUserPassword(id: string) {
        return axios.post<{ data: { temporaryPassword: string } }>(`${this.apiUrl}/User/Tenant/${id}/ResetPassword`)
    }
}

export interface TenantUserListItem {
    id: string
    email: string
    firstName: string
    lastName: string
    role: string
    status: string
    createdAtUtc: string
}

export interface CreateTenantUserResponse {
    id: string
    email: string
    role: string
    temporaryPassword: string
}
