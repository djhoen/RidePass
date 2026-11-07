import { onMounted, onBeforeUnmount } from 'vue'

// Gate scanning for USB / Bluetooth keyboard-wedge scanners. A wedge scanner "types" what it
// reads as a very fast burst of keystrokes followed by Enter, so the gate screen can tell a scan
// from a person typing by timing alone and catch it wherever focus happens to be: nowhere, on a
// button, or in one of the boxes that accept scans. Other inputs (a dialog's email field, the
// wristband link box) keep their keystrokes; a scan there is that field's business.

const GUID = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i
const REDEEM_GUID = /\/redeem\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/i

export type GateScan =
    | { kind: 'token'; token: string }
    | { kind: 'band'; code: string }

/** The redemption token in a scan or paste: the GUID after /redeem/ in a link, else the first GUID. */
export function extractGateToken(raw: string): string | null {
    const fromUrl = raw.match(REDEEM_GUID)
    if (fromUrl) return fromUrl[1].toLowerCase()
    const direct = raw.match(GUID)
    return direct ? direct[0].toLowerCase() : null
}

/**
 * What a scanned or pasted string means at the gate:
 * - a bare redemption-token GUID, or a /redeem/{token} link: a ticket or season pass token;
 * - anything else that could be a band code, when the tenant uses wristbands: a wristband code;
 * - otherwise null (not something the gate can look up).
 */
export function parseGateScan(raw: string, wristbandsEnabled: boolean): GateScan | null {
    // Some scanners prefix/suffix control characters (STX/ETX, CR, Tab); none belong in a code.
    const text = (raw ?? '').replace(/[\u0000-\u001f\u007f]/g, '').trim()
    if (!text) return null
    const token = extractGateToken(text)
    if (token) return { kind: 'token', token }
    if (!wristbandsEnabled) return null
    // A web address that isn't a redeem link, or a sentence, is not a band code.
    if (/^[a-z][a-z0-9+.-]*:\/\//i.test(text) || /\s/.test(text) || text.length > 64) return null
    return { kind: 'band', code: text }
}

export interface GateScannerOptions {
    /** Called with the burst's characters (not the field's whole value) when a scan completes. */
    onScan: (raw: string, target: Element | null) => void
    /** Editable elements that also accept scans (default: inside an element with data-gate-scan). */
    isScanTarget?: (el: Element) => boolean
    /** Return false to ignore keystrokes, e.g. while a dialog is open. */
    enabled?: () => boolean
    /** Shortest burst treated as a scan. */
    minLength?: number
    /** Longest gap between two keystrokes of one scan. People type at 100ms+ per key; scanners at 5-30ms. */
    maxGapMs?: number
}

function isEditable(el: Element): boolean {
    if (el instanceof HTMLTextAreaElement || el instanceof HTMLSelectElement) return true
    if (el instanceof HTMLInputElement) {
        return !['checkbox', 'radio', 'button', 'submit', 'reset', 'file', 'range', 'color', 'image']
            .includes(el.type)
    }
    return (el as HTMLElement).isContentEditable === true
}

const defaultIsScanTarget = (el: Element) => el.closest('[data-gate-scan]') !== null

export function useGateScanner(opts: GateScannerOptions) {
    const minLength = opts.minLength ?? 4
    const maxGapMs = opts.maxGapMs ?? 50
    const isScanTarget = opts.isScanTarget ?? defaultIsScanTarget

    let buffer = ''
    let lastAt = 0

    function onKeyDown(e: KeyboardEvent) {
        if (opts.enabled && !opts.enabled()) { buffer = ''; return }
        const target = e.target instanceof Element ? e.target : null
        // Typing in some other field: leave it alone entirely.
        if (target && isEditable(target) && !isScanTarget(target)) { buffer = ''; return }
        if (e.ctrlKey || e.metaKey || e.altKey) { buffer = ''; return }

        const now = e.timeStamp
        if (e.key === 'Enter') {
            const burst = buffer
            buffer = ''
            if (burst.length >= minLength && now - lastAt <= maxGapMs) {
                // Ours: don't let the Enter also press a focused button or submit a box.
                e.preventDefault()
                e.stopPropagation()
                opts.onScan(burst, target)
            }
            return
        }
        // Shift (for upper-case characters) and other non-printing keys: skip without breaking the burst.
        if (e.key.length !== 1) return
        if (now - lastAt > maxGapMs) buffer = ''
        buffer += e.key
        lastAt = now
    }

    // Capture phase so the burst is seen before any field or button handles the keys.
    onMounted(() => window.addEventListener('keydown', onKeyDown, true))
    onBeforeUnmount(() => window.removeEventListener('keydown', onKeyDown, true))
}
