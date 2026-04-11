import axios from 'axios'

export class FaqService {
    private apiUrl: string;

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT;
    }

    async getFaqs() {
        return axios.get(`${this.apiUrl}/Faq`);
    }

    async createFaq(req: any) {
        return axios.post(`${this.apiUrl}/Faq/Admin/Create`, req);
    }

    async updateFaq(req: any) {
        return axios.post(`${this.apiUrl}/Faq/Admin/Update`, req);
    }

    async deleteFaq(id: number) {
        return axios.post(`${this.apiUrl}/Faq/Admin/Delete?id=${id}`);
    }
}
