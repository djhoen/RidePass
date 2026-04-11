import axios from 'axios'

export class AddressService {
    private apiUrl: string;

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT;
    }

    async getAddress(addressId: number) {
        return axios.get(`${this.apiUrl}/Address/${addressId}`);
    }

    async createAddress(req: any) {
        return axios.post(`${this.apiUrl}/Address/Create`, req);
    }

    async updateAddress(req: any) {
        return axios.post(`${this.apiUrl}/Address/Update`, req);
    }
}
