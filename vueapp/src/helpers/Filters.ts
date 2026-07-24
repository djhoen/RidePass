import { formatTenantDate, formatTenantDateTime } from '@/helpers/TenantTime'

export default {
    currency(value: number): string {
        if (!value && value !== 0) return ''
        return '$' + value.toFixed(2).replace(/\d(?=(\d{3})+\.)/g, '$&,')
    },

    date(value: string): string {
        if (!value) return ''
        return formatTenantDate(value)
    },

    dateTime(value: string): string {
        if (!value) return ''
        return formatTenantDateTime(value, 'MMM D, YYYY h:mm A')
    },

    truncate(value: string, length: number): string {
        if (!value) return ''
        if (value.length <= length) return value
        return value.substring(0, length) + '...'
    }
}
