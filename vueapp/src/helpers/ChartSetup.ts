import { ref } from 'vue'
import {
    Chart as ChartJS,
    CategoryScale,
    LinearScale,
    PointElement,
    LineElement,
    BarElement,
    Title,
    Tooltip,
    Legend,
    Filler,
} from 'chart.js'

let registered = false

// Design-system chart palette. Read from the --rp-* tokens on <html> so charts
// follow the active light/dark mode; the constants below are getters, not frozen
// values, and `applyChartTheme()` refreshes Chart.js's global defaults whenever
// App.vue switches modes. Call the getters when building a chart's options/datasets
// (inside a computed or on each render), not at module load.
// Bumped by applyChartTheme() on every mode switch. Every getter reads it, so a Vue
// computed that builds chart data/options through the getters re-evaluates when the
// theme changes (getComputedStyle alone would leave the computed cached forever).
const chartThemeVersion = ref(0)

function token(name: string, fallback: string): string {
    void chartThemeVersion.value
    if (typeof document === 'undefined') return fallback
    const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim()
    return v || fallback
}
export const chartInk = () => token('--rp-ink', '#f5f5f5')
export const chartMuted = () => token('--rp-muted', '#a6a6a6')
export const chartMuted2 = () => token('--rp-muted2', '#6e6e6e')
export const chartGrid = () => token('--rp-line', 'rgba(255, 255, 255, 0.08)')
export const chartGreen = () => token('--rp-green', '#5dd39e')
export const chartRed = () => token('--rp-red', '#ff6b6b')
export const chartBlue = () => token('--rp-blue', '#5aa8ff')
export const chartPurple = () => token('--rp-purple', '#b28cff')
/** Monochrome series ramp (ink → muted), then the semantic blue/purple for extra series. */
export const chartSeries = () => [chartInk(), chartMuted(), chartMuted2(), token('--rp-raise', '#3d3d3d'), chartBlue(), chartPurple()]
/** The tenant's brand color (or ink on the apex site) for a highlighted series. */
export const chartTenant = () => token('--rp-tenant', '#f5f5f5')

/** Push the current mode's colors into Chart.js's global defaults. Idempotent, and
 *  safe to call before any chart page has registered the plugins (App.vue calls it on
 *  every theme switch; the tooltip/legend defaults only exist once registered). */
export function applyChartTheme() {
    chartThemeVersion.value++
    ChartJS.defaults.color = chartMuted()
    ChartJS.defaults.borderColor = chartGrid()
    ChartJS.defaults.font.family = "'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif"
    const tooltip = ChartJS.defaults.plugins?.tooltip
    if (tooltip) {
        tooltip.backgroundColor = token('--rp-raise', '#1e1e1e')
        tooltip.titleColor = chartInk()
        tooltip.bodyColor = chartMuted()
        tooltip.borderColor = token('--rp-line2', 'rgba(255, 255, 255, 0.14)')
        tooltip.borderWidth = 1
        tooltip.cornerRadius = 10
    }
    const legendLabels = ChartJS.defaults.plugins?.legend?.labels
    if (legendLabels) {
        legendLabels.boxWidth = 10
        legendLabels.boxHeight = 10
    }
}

export function registerChartJs() {
    if (registered) return
    ChartJS.register(
        CategoryScale,
        LinearScale,
        PointElement,
        LineElement,
        BarElement,
        Title,
        Tooltip,
        Legend,
        Filler,
    )
    applyChartTheme()
    registered = true
}
