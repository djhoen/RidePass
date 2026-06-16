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

    async resetPassword(req: { email: string }) {
        return axios.post(`${this.apiUrl}/User/ResetPassword`, req);
    }

    async confirmPasswordReset(req: { token: string; newPassword: string }) {
        return axios.post(`${this.apiUrl}/User/ResetPassword/Confirm`, req);
    }

    async verifyEmail(token: string) {
        return axios.post(`${this.apiUrl}/User/VerifyEmail`, { token });
    }

    async resendVerification(email: string) {
        return axios.post(`${this.apiUrl}/User/ResendVerification`, { email });
    }

    async updateEmergencyContact(req: { name: string; phone: string }) {
        return axios.put(`${this.apiUrl}/User/Profile/EmergencyContact`, req);
    }

    async updatePhone(req: { phone: string }) {
        return axios.put(`${this.apiUrl}/User/Profile/Phone`, req);
    }

    async updateRacerInfo(req: { bike: string | null; raceNumber: string | null }) {
        return axios.put(`${this.apiUrl}/User/Profile/RacerInfo`, req);
    }

    async updateBirthdate(req: { birthdate: string }) {
        return axios.put(`${this.apiUrl}/User/Profile/Birthdate`, req);
    }

    async updateAddress(req: {
        addressLine: string | null
        addressLine2: string | null
        city: string | null
        state: string | null
        postalCode: string | null
        country: string | null
    }) {
        return axios.put(`${this.apiUrl}/User/Profile/Address`, req);
    }

    // Tenant user management
    listTenantUsers() {
        return axios.get<{ data: TenantUserListItem[] }>(`${this.apiUrl}/User/Tenant`)
    }

    createTenantUser(req: { email: string; firstName: string; lastName: string; roles: string[] }) {
        return axios.post<{ data: CreateTenantUserResponse }>(`${this.apiUrl}/User/Tenant`, req)
    }

    updateTenantUserRoles(id: string, roles: string[]) {
        return axios.put(`${this.apiUrl}/User/Tenant/${id}/Role`, { roles })
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
    roles: string[]
    status: string
    createdAtUtc: string
}

export interface CreateTenantUserResponse {
    id: string
    email: string
    role: string
    temporaryPassword: string
}
