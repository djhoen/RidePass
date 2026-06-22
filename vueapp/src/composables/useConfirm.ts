import { reactive } from 'vue'

/**
 * Singleton confirm-dialog state. One ConfirmDialog instance is mounted in
 * App.vue and binds to this state; useConfirm() opens it and returns a
 * promise that resolves true/false on user choice.
 *
 * Why a singleton instead of per-view <v-dialog>: we never need more than
 * one confirm at a time, and centralizing the chrome means buttons / spacing
 * / focus handling stay consistent across the whole admin app. Per-view
 * dialogs are still fine for non-confirm flows (forms, multi-step wizards).
 */
interface ConfirmState {
    open: boolean
    title: string
    message: string
    confirmText: string
    cancelText: string
    confirmColor: string
    resolver: ((ok: boolean) => void) | null
}

export const confirmState = reactive<ConfirmState>({
    open: false,
    title: '',
    message: '',
    confirmText: 'Confirm',
    cancelText: 'Cancel',
    confirmColor: 'primary',
    resolver: null,
})

export interface ConfirmOptions {
    /** Optional title. When omitted the dialog renders message-only. */
    title?: string
    message: string
    /** Defaults to "Confirm". */
    confirmText?: string
    /** Defaults to "Cancel". */
    cancelText?: string
    /** Vuetify color for the confirm button (use 'error' for destructive). */
    confirmColor?: string
}

/**
 * Promise-returning confirm. Always replaces native window.confirm in this
 * codebase — see ~/.claude/skills/no-native-confirm-alert/SKILL.md.
 *
 * Usage:
 *   const ok = await useConfirm()({
 *       title: 'Release number?',
 *       message: 'This is permanent.',
 *       confirmText: 'Release',
 *       confirmColor: 'error',
 *   })
 *   if (!ok) return
 */
export function useConfirm() {
    return function confirm(opts: ConfirmOptions): Promise<boolean> {
        return new Promise((resolve) => {
            // If a confirm is already open, settle its promise as cancelled before we
            // overwrite the resolver, so the prior `await confirm(...)` never hangs.
            if (confirmState.resolver) confirmState.resolver(false)
            confirmState.title = opts.title ?? ''
            confirmState.message = opts.message
            confirmState.confirmText = opts.confirmText ?? 'Confirm'
            confirmState.cancelText = opts.cancelText ?? 'Cancel'
            confirmState.confirmColor = opts.confirmColor ?? 'primary'
            confirmState.resolver = resolve
            confirmState.open = true
        })
    }
}

/** Called only by the ConfirmDialog component to resolve and close. */
export function _resolveConfirm(ok: boolean) {
    const r = confirmState.resolver
    confirmState.resolver = null
    confirmState.open = false
    r?.(ok)
}
