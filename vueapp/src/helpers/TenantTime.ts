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
