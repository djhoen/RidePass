import axios from 'axios'

export class OrderService {
    private apiUrl: string;

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT;
    }

    async getOrder(orderId: number) {
        return axios.get(`${this.apiUrl}/Order/${orderId}`);
    }

    async getUserOrders() {
        return axios.get(`${this.apiUrl}/Order/UserOrders`);
    }

    async searchOrders(req: any) {
        return axios.post(`${this.apiUrl}/Order/Search`, req);
    }
}
