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

    async searchUsers(req: any) {
        return axios.post(`${this.apiUrl}/User/SearchUsers`, req);
    }

    async getUser(userId: string) {
        return axios.get(`${this.apiUrl}/User/${userId}`);
    }

    async saveUserRoles(req: any) {
        return axios.post(`${this.apiUrl}/User/SaveUserRoles`, req);
    }
}
