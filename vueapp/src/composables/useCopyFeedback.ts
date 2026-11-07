import { onBeforeUnmount, ref } from 'vue'

/**
 * Copy text to the clipboard with a short-lived "Copied" flag for the button label. reset() clears the
 * flag (and its pending timer): call it whenever a dialog opens or a new value is shown, so a "Copied"
 * from the last link never shows next to a new one.
 */
export function useCopyFeedback(durationMs = 2000) {
    const copied = ref(false)
    let timer: ReturnType<typeof setTimeout> | null = null

    function reset() {
        if (timer) clearTimeout(timer)
        timer = null
        copied.value = false
    }

    async function copy(text: string) {
        reset()
        try {
            await navigator.clipboard.writeText(text)
            copied.value = true
            timer = setTimeout(() => { copied.value = false; timer = null }, durationMs)
        } catch {
            copied.value = false
        }
    }

    onBeforeUnmount(reset)
    return { copied, copy, reset }
}
