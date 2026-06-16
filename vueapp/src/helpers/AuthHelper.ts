import { reactive, computed } from 'vue'
import { permissionsForRoles, type Permission } from './TenantPermissions'

interface AuthState {
    token: string | null
    userId: string | null
    role: string | null         // primary role (highest-privilege), for display/routing
    roles: string[]             // full set; permissions = union
    impersonatedLabel: string | null // "Alice Acme <alice@...>" when impersonating
}

// The JWT carries one "role" claim per role; repeated claims decode to an array, a single
// claim to a string. Normalize to a string[] either way.
function rolesFromDecoded(decoded: Record<string, any> | null): string[] {
    if (!decoded) return []
    const raw = decoded.role
    if (Array.isArray(raw)) return raw.filter(Boolean)
    if (typeof raw === 'string' && raw) return [raw]
    return []
}

function readStoredRoles(): string[] {
    try {
        const json = localStorage.getItem('roles')
        if (json) return JSON.parse(json)
    } catch { /* fall through */ }
    const single = localStorage.getItem('role')
    return single ? [single] : []
}

const ORIGINAL_TOKEN_KEY = 'original_token'
const ORIGINAL_USERID_KEY = 'original_userId'
const ORIGINAL_ROLE_KEY = 'original_role'
const ORIGINAL_ROLES_KEY = 'original_roles'
const IMPERSONATED_LABEL_KEY = 'impersonated_label'

const state = reactive<AuthState>({
    token: localStorage.getItem('token'),
    userId: localStorage.getItem('userId'),
    role: localStorage.getItem('role'),
    roles: readStoredRoles(),
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

// Store the full role set (and the derived primary) in state + localStorage.
function persistRoles(roles: string[]): void {
    state.roles = roles
    state.role = roles[0] ?? null
    localStorage.setItem('roles', JSON.stringify(roles))
    if (state.role) localStorage.setItem('role', state.role); else localStorage.removeItem('role')
}

// Adopt a token into the session: store it and hydrate userId + the full role set from it.
function hydrateSessionFromToken(token: string): void {
    state.token = token
    localStorage.setItem('token', token)
    const decoded = decodeJwt(token)
    if (!decoded) return
    if (decoded.UserId) {
        state.userId = decoded.UserId
        localStorage.setItem('userId', decoded.UserId)
    }
    persistRoles(rolesFromDecoded(decoded))
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
            state.roles = []
            localStorage.removeItem('token')
            localStorage.removeItem('userId')
            localStorage.removeItem('role')
            localStorage.removeItem('roles')
        } else {
            if (!state.userId && decoded.UserId) {
                state.userId = decoded.UserId
                localStorage.setItem('userId', decoded.UserId)
            }
            // Trust the token for the role set (older sessions may have only 'role' stored).
            const fromToken = rolesFromDecoded(decoded)
            if (fromToken.length && state.roles.length === 0) persistRoles(fromToken)
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
        // Hydrate the full role set from the token so multi-role staff get all permissions.
        hydrateSessionFromToken(token)
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

    getRoles(): string[] {
        return state.roles
    },

    setRole(role: string): void {
        // Primary role only; the full set is hydrated from the token in setToken.
        state.role = role
        localStorage.setItem('role', role)
        if (state.roles.length === 0) {
            state.roles = [role]
            localStorage.setItem('roles', JSON.stringify(state.roles))
        }
    },

    // Adopt a token handed in out-of-band (the super-admin "Preview" bridge that
    // carries the JWT to a tenant subdomain via the URL fragment). Stores the
    // token and decodes role + userId from it so the session is fully hydrated.
    adoptToken(token: string): void {
        hydrateSessionFromToken(token)
    },

    hasRole(...roles: string[]): boolean {
        return state.token !== null && state.roles.some(r => roles.includes(r))
    },

    hasPermission(permission: Permission): boolean {
        if (state.token === null) return false
        return permissionsForRoles(state.roles).has(permission)
    },

    hasAnyPermission(...permissions: Permission[]): boolean {
        if (state.token === null) return false
        const set = permissionsForRoles(state.roles)
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
        sessionStorage.setItem(ORIGINAL_ROLES_KEY, JSON.stringify(state.roles))
        sessionStorage.setItem(IMPERSONATED_LABEL_KEY, payload.label)

        // Hydrate the impersonated session from its token (carries the full role set).
        hydrateSessionFromToken(payload.token)
        state.impersonatedLabel = payload.label
    },

    stopImpersonation(): boolean {
        const token = sessionStorage.getItem(ORIGINAL_TOKEN_KEY)
        const userId = sessionStorage.getItem(ORIGINAL_USERID_KEY)
        const role = sessionStorage.getItem(ORIGINAL_ROLE_KEY)
        if (!token) return false

        let roles: string[] = []
        try { roles = JSON.parse(sessionStorage.getItem(ORIGINAL_ROLES_KEY) ?? '[]') } catch { /* ignore */ }
        if (roles.length === 0 && role) roles = [role]

        state.token = token
        state.userId = userId
        state.impersonatedLabel = null
        if (token) localStorage.setItem('token', token); else localStorage.removeItem('token')
        if (userId) localStorage.setItem('userId', userId); else localStorage.removeItem('userId')
        persistRoles(roles)

        sessionStorage.removeItem(ORIGINAL_TOKEN_KEY)
        sessionStorage.removeItem(ORIGINAL_USERID_KEY)
        sessionStorage.removeItem(ORIGINAL_ROLE_KEY)
        sessionStorage.removeItem(ORIGINAL_ROLES_KEY)
        sessionStorage.removeItem(IMPERSONATED_LABEL_KEY)
        return true
    },

    logout(): void {
        state.token = null
        state.userId = null
        state.role = null
        state.roles = []
        state.impersonatedLabel = null
        localStorage.removeItem('token')
        localStorage.removeItem('userId')
        localStorage.removeItem('role')
        localStorage.removeItem('roles')
        sessionStorage.removeItem(ORIGINAL_TOKEN_KEY)
        sessionStorage.removeItem(ORIGINAL_USERID_KEY)
        sessionStorage.removeItem(ORIGINAL_ROLE_KEY)
        sessionStorage.removeItem(ORIGINAL_ROLES_KEY)
        sessionStorage.removeItem(IMPERSONATED_LABEL_KEY)
    }
}
