// Rider height and weight (Script0317), stored as whole inches and whole pounds, entered as
// feet + inches and pounds. Mirrors Services.Riders.RiderMeasurementRules on the server, which
// is authoritative; this only gives the same message before a round-trip.

export const MIN_HEIGHT_IN = 24   // 2 ft 0 in
export const MAX_HEIGHT_IN = 96   // 8 ft 0 in
export const MIN_WEIGHT_LB = 20
export const MAX_WEIGHT_LB = 450

export interface Measurement {
    heightIn: number | null
    weightLb: number | null
}

export type HeightWeightMode = 'off' | 'optional' | 'required'

/** Feet + inches to total inches; null when feet is blank or inches is outside 0-11. */
export function toInches(feet: number | string | null | undefined, inches: number | string | null | undefined): number | null {
    if (feet === null || feet === undefined || feet === '') return null
    const f = Number(feet)
    const i = inches === null || inches === undefined || inches === '' ? 0 : Number(inches)
    if (!Number.isInteger(f) || !Number.isInteger(i) || f < 0 || i < 0 || i > 11) return null
    return f * 12 + i
}

export function fromInches(total: number | null | undefined): { feet: number | null; inches: number | null } {
    if (total === null || total === undefined) return { feet: null, inches: null }
    return { feet: Math.floor(total / 12), inches: total % 12 }
}

/** "5 ft 7 in, 150 lb", or '' when either is unknown. */
export function formatMeasurement(m: Measurement | null | undefined): string {
    if (!m || m.heightIn == null || m.weightLb == null) return ''
    const { feet, inches } = fromInches(m.heightIn)
    return `${feet} ft ${inches} in, ${m.weightLb} lb`
}

/** Null when acceptable: both or neither, both when required, and within range. */
export function measurementError(m: Measurement, required: boolean, who?: string | null): string | null {
    const whose = who && who.trim() ? `${who.trim()}'s` : 'your'
    if (m.heightIn == null && m.weightLb == null) {
        return required ? `Enter ${whose} height and weight so the shop can size the bike.` : null
    }
    if (m.heightIn == null || m.weightLb == null) return `Enter both ${whose} height and weight.`
    if (m.heightIn < MIN_HEIGHT_IN || m.heightIn > MAX_HEIGHT_IN) return 'Height must be between 2 ft 0 in and 8 ft 0 in.'
    if (!Number.isInteger(m.weightLb) || m.weightLb < MIN_WEIGHT_LB || m.weightLb > MAX_WEIGHT_LB) {
        return `Weight must be between ${MIN_WEIGHT_LB} and ${MAX_WEIGHT_LB} lb.`
    }
    return null
}
