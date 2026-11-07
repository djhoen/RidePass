import { onMounted, onBeforeUnmount } from 'vue'

// Gate scanning for USB / Bluetooth keyboard-wedge scanners. A wedge scanner "types" what it reads
// and presses Enter. Two ways a scan is recognised:
//
// - In a box marked data-gate-scan ("search" or "codes"): Enter on text that parses as a ticket or
//   pass token / redeem link is a scan at ANY typing speed, so slow Bluetooth scanners and pastes
//   work too. A wristband code counts at any speed in the "codes" (paste) box, but in the "search"
//   box only when it arrived as a scanner-speed burst, since a phone number typed there is a search.
// - Anywhere else that is not a text field (nothing focused, a button, the page): only a fast burst
//   (every key within maxGapMs, at least minBurstLength characters) ending in Enter that parses as
//   a token, link or wristband code.
//
// Plain words (letters only, the way names are typed) are never a scan. Other inputs (a dialog's
// email field, the wristband link box) keep their keystrokes; a scan there is that field's business.

const GUID_SRC = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'
const GUID_ALL = new RegExp(GUID_SRC, 'gi')
const REDEEM_ALL = new RegExp(`/redeem/(${GUID_SRC})`, 'gi')
// A band code: one run of letters/digits and a few separators, with at least one digit, no '@'.
const BAND_CODE = /^(?=.*\d)[a-z0-9][a-z0-9\-_.:#]{2,63}$/i
const PLAIN_WORDS = /^[\p{L}\s'.-]+$/u

export type GateScan =
    | { kind: 'token'; token: string }
    | { kind: 'band'; code: string }

export type GateScanSource = 'search' | 'codes' | 'page'

/** The redemption token in a scan or paste: the LAST GUID after /redeem/ in a link, else the last
 *  GUID. "Last" so text left in a box followed by a new scan resolves to the new one. */
export function extractGateToken(raw: string): string | null {
    const links = [...(raw ?? '').matchAll(REDEEM_ALL)]
    if (links.length) return links[links.length - 1][1].toLowerCase()
    const guids = (raw ?? '').match(GUID_ALL)
    return guids ? guids[guids.length - 1].toLowerCase() : null
}

/** Letters, spaces, apostrophes and hyphens only: how a name is typed, never a scan. */
export function isPlainWords(raw: string): boolean {
    return PLAIN_WORDS.test((raw ?? '').trim())
}

/**
 * What a scanned or pasted string means at the gate:
 * - a bare redemption-token GUID, or a /redeem/{token} link: a ticket or season pass token;
 * - a band-code-shaped string (letters/digits with at least one digit, no spaces or '@'), when the
 *   tenant uses wristbands: a wristband code;
 * - otherwise null (not something the gate can look up).
 */
export function parseGateScan(raw: string, wristbandsEnabled: boolean): GateScan | null {
    // Some scanners prefix/suffix control characters (STX/ETX, CR, Tab); none belong in a code.
    const text = (raw ?? '').replace(/[\u0000-\u001f\u007f]/g, '').trim()
    if (!text || isPlainWords(text)) return null
    const token = extractGateToken(text)
    if (token) return { kind: 'token', token }
    if (wristbandsEnabled && BAND_CODE.test(text)) return { kind: 'band', code: text }
    return null
}

export interface GateScannerOptions {
    /** Called when a scan completes. raw is the scanned text (the burst, or the box's value). */
    onScan: (scan: GateScan, raw: string, source: GateScanSource) => void
    /** Whether wristband codes are a thing for this tenant. */
    wristbandsEnabled: () => boolean
    /** Return false to ignore keystrokes, e.g. while a dialog is open. */
    enabled?: () => boolean
    /** Shortest burst treated as a scan outside the boxes. */
    minBurstLength?: number
    /** Longest gap between two keystrokes of one burst. People type at 100ms+ per key; scanners at 5-50ms. */
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

function scanBox(el: Element | null): 'search' | 'codes' | null {
    const v = el?.closest('[data-gate-scan]')?.getAttribute('data-gate-scan')
    return v === 'search' || v === 'codes' ? v : null
}

export function useGateScanner(opts: GateScannerOptions) {
    const minBurstLength = opts.minBurstLength ?? 6
    const maxGapMs = opts.maxGapMs ?? 100

    let buffer = ''
    let lastAt = 0

    function onKeyDown(e: KeyboardEvent) {
        if (opts.enabled && !opts.enabled()) { buffer = ''; return }
        const target = e.target instanceof Element ? e.target : null
        const box = scanBox(target)
        // Typing in some other field: leave it alone entirely.
        if (target && isEditable(target) && !box) { buffer = ''; return }
        if (e.ctrlKey || e.metaKey || e.altKey) { buffer = ''; return }

        const now = e.timeStamp
        if (e.key === 'Enter') {
            const burst = buffer
            buffer = ''
            const fast = burst.length >= minBurstLength && now - lastAt <= maxGapMs
            const wb = opts.wristbandsEnabled()
            let raw: string
            let scan: GateScan | null
            if (box) {
                raw = fast ? burst : ((target as HTMLInputElement).value ?? '')
                scan = parseGateScan(raw, wb)
                if (scan?.kind === 'band' && box === 'search' && !fast) scan = null
            } else {
                if (!fast) return
                raw = burst
                scan = parseGateScan(raw, wb)
            }
            if (!scan) return
            // Ours: don't let the Enter also press a focused button or submit the box.
            e.preventDefault()
            e.stopPropagation()
            opts.onScan(scan, raw, box ?? 'page')
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
