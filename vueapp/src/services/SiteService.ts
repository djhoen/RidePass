import axios from 'axios'

export class SiteService {
    private apiUrl: string;

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT;
    }

    async getBanner() {
        return axios.get(`${this.apiUrl}/Site/Banner`);
    }

    async getBanners() {
        return axios.get(`${this.apiUrl}/Site/Admin/Banners`);
    }

    async saveBanner(req: any) {
        return axios.post(`${this.apiUrl}/Site/Admin/SaveBanner`, req);
    }

    async getSetting(name: string) {
        return axios.get(`${this.apiUrl}/Site/Admin/Setting?name=${name}`);
    }

    async saveSetting(req: any) {
        return axios.post(`${this.apiUrl}/Site/Admin/SaveSetting`, req);
    }
}
