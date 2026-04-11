import axios from 'axios'

export class CouponService {
    private apiUrl: string;

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT;
    }

    async getCoupons() {
        return axios.get(`${this.apiUrl}/Coupon/Admin/List`);
    }

    async getCoupon(couponId: number) {
        return axios.get(`${this.apiUrl}/Coupon/Admin/${couponId}`);
    }

    async createCoupon(req: any) {
        return axios.post(`${this.apiUrl}/Coupon/Admin/Create`, req);
    }

    async updateCoupon(req: any) {
        return axios.post(`${this.apiUrl}/Coupon/Admin/Update`, req);
    }
}
