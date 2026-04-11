import axios from 'axios'

export class ProductService {
    private apiUrl: string;

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT;
    }

    async getProducts() {
        return axios.get(`${this.apiUrl}/Product`);
    }

    async getProduct(productId: number) {
        return axios.get(`${this.apiUrl}/Product/${productId}`);
    }
}
