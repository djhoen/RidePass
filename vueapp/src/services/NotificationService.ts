import axios from 'axios'

export class NotificationService {
    private apiUrl: string;

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT;
    }

    async getNotifications(page: number) {
        return axios.get(`${this.apiUrl}/Notification?page=${page}`);
    }

    async getUnreadCount() {
        return axios.get(`${this.apiUrl}/Notification/UnreadCount`);
    }

    async markAsRead() {
        return axios.post(`${this.apiUrl}/Notification/MarkAsRead`);
    }
}
