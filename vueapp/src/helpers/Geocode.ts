// Client-side geocoding via Nominatim (OpenStreetMap).
// No API key, CORS-friendly. Usage policy asks for ≤1 req/sec and a meaningful
// Referer (which the browser sets automatically). Don't bulk-query from here.

export interface GeocodeResult {
    lat: number
    lng: number
    displayName: string
}

export async function geocode(query: string): Promise<GeocodeResult | null> {
    const trimmed = query.trim()
    if (!trimmed) return null
    const url = `https://nominatim.openstreetmap.org/search?format=json&limit=1&addressdetails=0&q=${encodeURIComponent(trimmed)}`
    const response = await fetch(url, { headers: { Accept: 'application/json' } })
    if (!response.ok) return null
    const items = await response.json()
    if (!Array.isArray(items) || items.length === 0) return null
    const top = items[0]
    const lat = parseFloat(top.lat)
    const lng = parseFloat(top.lon)
    if (Number.isNaN(lat) || Number.isNaN(lng)) return null
    return { lat, lng, displayName: top.display_name }
}

export async function browserGeolocate(): Promise<{ lat: number; lng: number } | null> {
    if (!('geolocation' in navigator)) return null
    return new Promise(resolve => {
        navigator.geolocation.getCurrentPosition(
            pos => resolve({ lat: pos.coords.latitude, lng: pos.coords.longitude }),
            () => resolve(null),
            { enableHighAccuracy: false, timeout: 10_000, maximumAge: 60_000 },
        )
    })
}
