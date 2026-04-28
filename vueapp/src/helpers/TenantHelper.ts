const ROOT_DOMAIN = (import.meta.env.VITE_ROOT_DOMAIN || 'ridepass.local').toLowerCase()

export default {
    getSubdomain(): string | null {
        const host = window.location.hostname.toLowerCase()
        if (host === ROOT_DOMAIN || host === 'localhost') return null
        if (/^(\d+\.){3}\d+$/.test(host)) return null
        const suffix = '.' + ROOT_DOMAIN
        if (host.endsWith(suffix)) {
            const prefix = host.slice(0, host.length - suffix.length)
            if (prefix && !prefix.includes('.')) return prefix
        }
        return null
    },

    rootDomain(): string {
        return ROOT_DOMAIN
    }
}
