import axios from 'axios'

/** One recorded staff action. Mirrors webapi StaffActivityItem. */
export interface StaffActivityItem {
    id: string
    actorUserId: string | null
    /** Snapshotted when the action happened, so it survives the account being deleted. */
    actorEmail: string | null
    actorRole: string | null
    /** Dotted machine name, e.g. "purchase.refund". */
    action: string
    summary: string
    targetKind: string | null
    targetId: string | null
    /** Null, or "127.0.0.1" on entries written before the forwarded-headers fix. */
    ipAddress: string | null
    /** Raw JSON string of action-specific detail; shape varies by action. */
    metadata: string | null
    createdAtUtc: string
}

export class StaffActivityService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    /** Whole-tenant activity. Requires audit.view. */
    list(params: { action?: string | null; actorUserId?: string | null; fromUtc?: string | null; toUtc?: string | null; take?: number } = {}) {
        return axios.get<{ data: StaffActivityItem[] }>(`${this.apiUrl}/StaffActivity`, {
            params: {
                action: params.action || undefined,
                actorUserId: params.actorUserId || undefined,
                fromUtc: params.fromUtc || undefined,
                toUtc: params.toUtc || undefined,
                take: params.take ?? 200,
            },
        })
    }

    /** The signed-in staffer's own activity. No permission required. */
    mine(params: { fromUtc?: string | null; toUtc?: string | null; take?: number } = {}) {
        return axios.get<{ data: StaffActivityItem[] }>(`${this.apiUrl}/StaffActivity/Mine`, {
            params: {
                fromUtc: params.fromUtc || undefined,
                toUtc: params.toUtc || undefined,
                take: params.take ?? 200,
            },
        })
    }

    /** Distinct action names actually present for this tenant, for the filter dropdown. */
    actions() {
        return axios.get<{ data: string[] }>(`${this.apiUrl}/StaffActivity/Actions`)
    }
}
