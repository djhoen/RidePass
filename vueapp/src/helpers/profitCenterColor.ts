/**
 * Profit-center colors, shared by every screen that draws one.
 *
 * The color itself always comes from the API (a profit center owns its color, and the same hex
 * reaches the settings page, the reports and the charts). What lives here is the small amount of
 * presentation logic around it: the dark-surface step, and the ink color for text sitting on top
 * of a filled swatch.
 */

/** Slot 1 of the categorical palette, reserved for a total / all-revenue series. */
export const TOTAL_SERIES_COLOR = '#2a78d6'
export const TOTAL_SERIES_COLOR_DARK = '#3987e5'

/** Fallback for a center with no color set. Mirrors ProfitCenterPalette.Unassigned. */
export const UNASSIGNED_COLOR = '#8a8a8a'

/**
 * Light hex -> the step chosen for the dark surface. Not a computed lightening: these are the
 * validated dark steps of the same hues. Reusing the light values on dark genuinely fails (violet
 * #4a3aa7 lands at 1.88:1 against Vuetify's dark surface, which is unreadable).
 *
 * A CUSTOM color a tenant picked is not in this table and passes through unchanged — there is no
 * principled way to re-step an arbitrary hex, and guessing would be worse than honoring their choice.
 */
const DARK_STEP: Record<string, string> = {
    '#2a78d6': '#3987e5',   // blue (totals)
    '#eb6834': '#d95926',   // orange
    '#1baf7a': '#199e70',   // aqua
    '#eda100': '#c98500',   // yellow
    '#e87ba4': '#d55181',   // magenta
    '#008300': '#008300',   // green, same step both modes
    '#4a3aa7': '#9085e9',   // violet
    '#e34948': '#e66767',   // red
    '#8a8a8a': '#9a9a9a',   // unassigned gray
}

/** The color to actually paint with, given the theme currently in force. */
export function seriesColor(color: string | null | undefined, isDark: boolean): string {
    const hex = (color || UNASSIGNED_COLOR).trim().toLowerCase()
    return isDark ? (DARK_STEP[hex] ?? hex) : hex
}

/** Same color at a given alpha, for area fills and hover washes. */
export function withAlpha(color: string, alpha: number): string {
    const hex = (color || UNASSIGNED_COLOR).trim()
    const m = /^#([0-9a-f]{6})$/i.exec(hex)
    if (!m) return hex
    const n = parseInt(m[1], 16)
    return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${alpha})`
}

/**
 * Black or white ink for text on a filled swatch of this color, by relative luminance. Only for
 * text that sits ON the color (a chip's own label); text BESIDE a color keeps its normal ink.
 */
export function inkOn(color: string): string {
    const m = /^#([0-9a-f]{6})$/i.exec((color || UNASSIGNED_COLOR).trim())
    if (!m) return '#ffffff'
    const n = parseInt(m[1], 16)
    const srgb = [(n >> 16) & 255, (n >> 8) & 255, n & 255].map(v => {
        const c = v / 255
        return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4)
    })
    const luminance = 0.2126 * srgb[0] + 0.7152 * srgb[1] + 0.0722 * srgb[2]
    return luminance > 0.42 ? '#0b0b0b' : '#ffffff'
}
