import { reactive, computed } from 'vue'
import { permissionsForRole, type Permission } from './TenantPermissions'

interface AuthState {
    token: string | null
    userId: string | null
    role: string | null
    impersonatedLabel: string | null // "Alice Acme <alice@...>" when impersonating
}

const ORIGINAL_TOKEN_KEY = 'original_token'
const ORIGINAL_USERID_KEY = 'original_userId'
const ORIGINAL_ROLE_KEY = 'original_role'
const IMPERSONATED_LABEL_KEY = 'impersonated_label'

const state = reactive<AuthState>({
    token: localStorage.getItem('token'),
    userId: localStorage.getItem('userId'),
    role: localStorage.getItem('role'),
    impersonatedLabel: sessionStorage.getItem(IMPERSONATED_LABEL_KEY),
})

function decodeJwt(token: string): Record<string, any> | null {
    try {
        const payload = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')
        const padded = payload + '='.repeat((4 - (payload.length % 4)) % 4)
        return JSON.parse(atob(padded))
    } catch {
        return null
    }
}

// Hydrate role + userId from the JWT if they're missing, and expire stale tokens.
if (state.token) {
    const decoded = decodeJwt(state.token)
    if (decoded) {
        const now = Math.floor(Date.now() / 1000)
        if (decoded.exp && decoded.exp < now) {
            state.token = null
            state.userId = null
            state.role = null
            localStorage.removeItem('token')
            localStorage.removeItem('userId')
            localStorage.removeItem('role')
        } else {
            if (!state.userId && decoded.UserId) {
                state.userId = decoded.UserId
                localStorage.setItem('userId', decoded.UserId)
            }
            if (!state.role && decoded.role) {
                state.role = decoded.role
                localStorage.setItem('role', decoded.role)
            }
        }
    }
}

export const authState = state

export const isImpersonating = computed(() => sessionStorage.getItem(ORIGINAL_TOKEN_KEY) !== null)

export default {
    isAuthenticated(): boolean {
        return state.token !== null && state.token !== ''
    },

    getToken(): string | null {
        return state.token
    },

    setToken(token: string): void {
        state.token = token
        localStorage.setItem('token', token)
    },

    removeToken(): void {
        state.token = null
        localStorage.removeItem('token')
    },

    getUserId(): string | null {
        return state.userId
    },

    setUserId(userId: string): void {
        state.userId = userId
        localStorage.setItem('userId', userId)
    },

    getRole(): string | null {
        return state.role
    },

    setRole(role: string): void {
        state.role = role
        localStorage.setItem('role', role)
    },

    hasRole(...roles: string[]): boolean {
        return state.token !== null && state.role !== null && roles.includes(state.role)
    },

    hasPermission(permission: Permission): boolean {
        if (state.token === null) return false
        return permissionsForRole(state.role).has(permission)
    },

    hasAnyPermission(...permissions: Permission[]): boolean {
        if (state.token === null) return false
        const set = permissionsForRole(state.role)
        return permissions.some(p => set.has(p))
    },

    isImpersonating(): boolean {
        return sessionStorage.getItem(ORIGINAL_TOKEN_KEY) !== null
    },

    getImpersonatedLabel(): string | null {
        return state.impersonatedLabel
    },

    startImpersonation(payload: { token: string; userId: string; role: string; label: string }): void {
        // Stash current session so we can restore it.
        if (state.token) sessionStorage.setItem(ORIGINAL_TOKEN_KEY, state.token)
        if (state.userId) sessionStorage.setItem(ORIGINAL_USERID_KEY, state.userId)
        if (state.role) sessionStorage.setItem(ORIGINAL_ROLE_KEY, state.role)
        sessionStorage.setItem(IMPERSONATED_LABEL_KEY, payload.label)

        state.token = payload.token
        state.userId = payload.userId
        state.role = payload.role
        state.impersonatedLabel = payload.label
        localStorage.setItem('token', payload.token)
        localStorage.setItem('userId', payload.userId)
        localStorage.setItem('role', payload.role)
    },

    stopImpersonation(): boolean {
        const token = sessionStorage.getItem(ORIGINAL_TOKEN_KEY)
        const userId = sessionStorage.getItem(ORIGINAL_USERID_KEY)
        const role = sessionStorage.getItem(ORIGINAL_ROLE_KEY)
        if (!token) return false

        state.token = token
        state.userId = userId
        state.role = role
        state.impersonatedLabel = null
        if (token) localStorage.setItem('token', token); else localStorage.removeItem('token')
        if (userId) localStorage.setItem('userId', userId); else localStorage.removeItem('userId')
        if (role) localStorage.setItem('role', role); else localStorage.removeItem('role')

        sessionStorage.removeItem(ORIGINAL_TOKEN_KEY)
        sessionStorage.removeItem(ORIGINAL_USERID_KEY)
        sessionStorage.removeItem(ORIGINAL_ROLE_KEY)
        sessionStorage.removeItem(IMPERSONATED_LABEL_KEY)
        return true
    },

    logout(): void {
        state.token = null
        state.userId = null
        state.role = null
        state.impersonatedLabel = null
        localStorage.removeItem('token')
        localStorage.removeItem('userId')
        localStorage.removeItem('role')
        sessionStorage.removeItem(ORIGINAL_TOKEN_KEY)
        sessionStorage.removeItem(ORIGINAL_USERID_KEY)
        sessionStorage.removeItem(ORIGINAL_ROLE_KEY)
        sessionStorage.removeItem(IMPERSONATED_LABEL_KEY)
    }
}
