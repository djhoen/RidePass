import { onBeforeUnmount } from 'vue'
import { KioskPushService, KIOSK_PUSH_OPEN } from '@/services/KioskService'

/**
 * A gate screen keeps following the kiosk requests it sent after the Send to kiosk dialog is closed: every
 * 5 s while at least one is still open, it re-checks them, and calls onDone (with what was tracked) when the
 * kiosk finishes, so the screen still refreshes. Requests that end any other way are dropped.
 */
export function useKioskPushWatch<T>(onDone: (context: T, pushId: string) => void, intervalMs = 5000) {
    const service = new KioskPushService()
    const open = new Map<string, T>()
    let timer: ReturnType<typeof setInterval> | null = null
    let checking = false

    function sync() {
        if (open.size && !timer) timer = setInterval(() => { void check() }, intervalMs)
        else if (!open.size && timer) { clearInterval(timer); timer = null }
    }

    async function check() {
        if (checking) return
        checking = true
        try {
            for (const [id, context] of [...open]) {
                try {
                    const p = (await service.status(id)).data.data
                    if ((KIOSK_PUSH_OPEN as readonly string[]).includes(p.status)) continue
                    open.delete(id)
                    if (p.status === 'done') onDone(context, id)
                } catch (err: any) {
                    if (err.response?.status === 404) open.delete(id)
                }
            }
        } finally {
            checking = false
            sync()
        }
    }

    /** Follow a request just sent from this screen. */
    function track(pushId: string, context: T) { open.set(pushId, context); sync() }
    /** The dialog already reported it (done while open): don't report it twice. */
    function untrack(pushId: string) { open.delete(pushId); sync() }
    function clear() { open.clear(); sync() }

    onBeforeUnmount(clear)
    return { track, untrack, clear }
}
