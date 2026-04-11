export default {
    isAuthenticated(): boolean {
        const token = localStorage.getItem('token')
        return token != null && token !== ''
    },

    getToken(): string | null {
        return localStorage.getItem('token')
    },

    setToken(token: string): void {
        localStorage.setItem('token', token)
    },

    removeToken(): void {
        localStorage.removeItem('token')
    },

    getUserId(): string | null {
        return localStorage.getItem('userId')
    },

    setUserId(userId: string): void {
        localStorage.setItem('userId', userId)
    },

    logout(): void {
        localStorage.removeItem('token')
        localStorage.removeItem('userId')
    }
}
