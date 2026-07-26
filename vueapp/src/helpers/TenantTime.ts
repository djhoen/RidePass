import dayjs from 'dayjs'
import { branding } from '@/stores/branding'

/**
 * Tenant-timezone time formatting. Every timestamp the app RENDERS must go through
 * these (or an equivalent .utc().tz(branding.timezone) chain): timestamps are stored
 * UTC, and an admin checking their track from another timezone (or a hosted server in
 * UTC) must still see the track's own clock. The dayjs utc + timezone plugins are
 * registered in main.ts.
 *
 * DATE-ONLY values (valid_from_date, birthdate, business_date...) must NOT go through
 * these: they carry no time or zone, and converting them as if they were UTC midnight
 * shifts them a day for western timezones. Format those directly with dayjs(d).
 */
function tz(): string {
    return branding.timezone || 'UTC'
}

/** A dayjs instance of a UTC timestamp, converted to the tenant's timezone. */
export function tenantDayjs(utc: string | Date | number) {
    return dayjs.utc(utc).tz(tz())
}

/** "2026-07-24 09:15" style date+time; '' for null/undefined. */
export function formatTenantDateTime(utc: string | Date | null | undefined, fmt = 'YYYY-MM-DD HH:mm'): string {
    return utc ? tenantDayjs(utc).format(fmt) : ''
}

/** Time-of-day only ("09:15"); '' for null/undefined. */
export function formatTenantTime(utc: string | Date | null | undefined, fmt = 'HH:mm'): string {
    return utc ? tenantDayjs(utc).format(fmt) : ''
}

/** Calendar date of a TIMESTAMP in tenant tz ("Jul 24, 2026"); '' for null/undefined. */
export function formatTenantDate(utc: string | Date | null | undefined, fmt = 'MMM D, YYYY'): string {
    return utc ? tenantDayjs(utc).format(fmt) : ''
}

/**
 * The INVERSE of the formatters above: turn a wall-clock reading at the track back into a real
 * instant. Use these for every `<input type="datetime-local">` / `type="date"` value in an admin
 * screen, because the browser gives you a naive "YYYY-MM-DDTHH:mm" with no zone and `new Date()`
 * would silently read it in the BROWSER's zone.
 *
 * That difference is not academic: a shop whose tenant timezone is America/New_York, operated by
 * someone whose laptop is on Mountain time, is two hours out. The Rental Board would draw a bar at
 * 2pm and the booking it produced would ask the server about 4pm, so gear that was plainly free on
 * screen came back "not available for this window".
 */
export function tenantWallClockToMs(wallClock: string): number {
    return dayjs.tz(wallClock, tz()).valueOf()
}

/** Same, as an ISO instant for the wire. Throws on an empty/invalid string, so guard first. */
export function tenantWallClockToIso(wallClock: string): string {
    return dayjs.tz(wallClock, tz()).toISOString()
}

/** "Now" as a wall clock at the track, for seeding a datetime-local input. */
export function tenantWallClockNow(fmt = 'YYYY-MM-DDTHH:mm'): string {
    return tenantDayjs(new Date()).format(fmt)
}
