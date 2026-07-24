/*
 * Embeddable widget catalog — the single source of truth for which RidePass
 * widgets a track can drop on their own website.
 *
 * Consumed by:
 *  - the super-admin Embedded tab (snippet builder)
 *  - documentation / future self-serve embed UI
 *
 * The standalone loader (public/embed.js) is plain, un-bundled JS so it can't
 * import this module; keep the `type -> path` mapping here in sync with the
 * switch in embed.js (kept tiny on purpose).
 */

// Where the pasteable snippet points. Tracks paste this on their production
// site, so it always targets the production apex (not the dev host).
export const EMBED_SCRIPT_SRC = 'https://ridepass.io/embed.js'

export interface EmbedWidgetParam {
    // The data-* attribute name (without the leading "data-").
    attr: string
    label: string
    placeholder?: string
    required?: boolean
    hint?: string
}

export interface EmbedWidgetDef {
    key: string
    label: string
    description: string
    icon: string
    // Extra data-* attributes (beyond data-ridepass + data-tenant) this widget accepts.
    params?: EmbedWidgetParam[]
}

export const EMBED_WIDGETS: EmbedWidgetDef[] = [
    {
        key: 'events',
        label: 'Events list',
        description: 'A carousel of upcoming events. Each card opens registration + checkout inline.',
        icon: 'mdi-calendar-month',
        params: [
            {
                attr: 'limit', label: 'Max events (optional)', placeholder: '6',
                hint: 'Cap how many events show. Leave blank for all upcoming.',
            },
            {
                attr: 'event-type', label: 'Event type code (optional)', placeholder: 'race',
                hint: 'Show only one event type (its code). Leave blank for all types.',
            },
        ],
    },
    {
        key: 'calendar',
        label: 'Calendar + events',
        description: 'A carousel of upcoming events above a month calendar. Clicking any event opens registration + checkout inline.',
        icon: 'mdi-calendar-text',
        params: [
            {
                attr: 'limit', label: 'Max events in carousel (optional)', placeholder: '10',
                hint: 'Cap the upcoming-events carousel. The calendar always shows every event for the month.',
            },
            {
                attr: 'event-type', label: 'Event type code (optional)', placeholder: 'race',
                hint: 'Show only one event type (its code). Leave blank for all types.',
            },
        ],
    },
    {
        key: 'order',
        label: 'Food & beverage ordering',
        description: 'The full order-ahead F&B menu with cart and payment. Visitors browse the menu freely; signing in happens inline at checkout.',
        icon: 'mdi-silverware-fork-knife',
    },
    {
        key: 'status',
        label: 'Daily status strip',
        description: 'A slim open/closed banner with the day\'s status message and today\'s hours. Made for the top of the track\'s own homepage.',
        icon: 'mdi-list-status',
    },
    {
        key: 'shop',
        label: 'Bike shop storefront',
        description: 'The retail storefront: browse products anonymously, sign in at checkout.',
        icon: 'mdi-storefront-outline',
    },
    {
        key: 'rentals',
        label: 'Rental booking',
        description: 'Reserve rental bikes online: pick gear and dates, pay the fee and deposit hold, sign in inline. Waiver + pickup at the shop.',
        icon: 'mdi-bike-fast',
    },
    {
        key: 'giftcard',
        label: 'Gift cards',
        description: 'Sell gift cards from the track\'s own site. The form is open to everyone; buying signs in inline.',
        icon: 'mdi-wallet-giftcard',
    },
    {
        key: 'membership',
        label: 'Membership signup',
        description: 'The membership price card and purchase flow. Visitors see the offer; buying signs in inline.',
        icon: 'mdi-card-account-details',
    },
    {
        key: 'blog',
        label: 'News feed',
        description: 'Latest published blog posts as cards; each opens the full article on the hosted site.',
        icon: 'mdi-post',
        params: [
            {
                attr: 'limit', label: 'Max posts (optional)', placeholder: '6',
                hint: 'Cap how many posts show. Defaults to 6.',
            },
        ],
    },
    {
        key: 'feedback',
        label: 'Feedback form',
        description: 'The public feedback / contact form.',
        icon: 'mdi-message-text',
    },
    {
        key: 'event',
        label: 'Single event',
        description: 'Registration + checkout for one specific event.',
        icon: 'mdi-ticket-confirmation',
        params: [
            {
                attr: 'event', label: 'Event ID', placeholder: '00000000-0000-0000-0000-000000000000',
                required: true, hint: "The event's ID (from the event's admin page URL).",
            },
        ],
    },
    {
        key: 'seasonpasses',
        label: 'Season passes',
        description: 'The full season pass lineup with checkout inline.',
        icon: 'mdi-wallet-membership',
    },
    {
        key: 'seasonpass',
        label: 'Single season pass',
        description: 'Checkout for one specific season pass.',
        icon: 'mdi-card-account-details-star',
        params: [
            {
                attr: 'pass', label: 'Season pass ID', placeholder: '00000000-0000-0000-0000-000000000000',
                required: true, hint: "The pass's ID (copy it from Admin > Season Passes).",
            },
        ],
    },
]

export function getEmbedWidget(key: string): EmbedWidgetDef | undefined {
    return EMBED_WIDGETS.find(w => w.key === key)
}

/**
 * Build the chromeless route path (with query) a widget points at, e.g.
 * "/embed/calendar?limit=10&type=race". Mirrors the switch in public/embed.js
 * (keep both in sync). Used for the in-dashboard live preview. Returns null when
 * a required param is missing (e.g. the single-event widget needs an event id).
 */
export function buildEmbedPath(widgetKey: string, values: Record<string, string> = {}): string | null {
    if (widgetKey === 'event') {
        const id = (values['event'] ?? '').trim()
        return id ? `/embed/event/${encodeURIComponent(id)}` : null
    }
    if (widgetKey === 'seasonpass') {
        const id = (values['pass'] ?? '').trim()
        return id ? `/embed/seasonpass/${encodeURIComponent(id)}` : null
    }
    if (widgetKey === 'seasonpasses') return '/embed/seasonpasses'
    if (widgetKey === 'order') return '/embed/order'
    if (widgetKey === 'status') return '/embed/status'
    if (widgetKey === 'shop') return '/embed/shop'
    if (widgetKey === 'rentals') return '/embed/rentals'
    if (widgetKey === 'giftcard') return '/embed/giftcard'
    if (widgetKey === 'membership') return '/embed/membership'
    if (widgetKey === 'feedback') return '/embed/feedback'
    if (widgetKey === 'blog') {
        const limit = (values['limit'] ?? '').trim()
        return '/embed/blog' + (limit ? `?limit=${encodeURIComponent(limit)}` : '')
    }
    const base = widgetKey === 'calendar' ? '/embed/calendar' : '/embed/events'
    const qs: string[] = []
    const limit = (values['limit'] ?? '').trim()
    const etype = (values['event-type'] ?? '').trim()
    if (limit) qs.push('limit=' + encodeURIComponent(limit))
    if (etype) qs.push('type=' + encodeURIComponent(etype))
    return base + (qs.length ? '?' + qs.join('&') : '')
}

/**
 * Build the paste-able HTML snippet for a widget instance.
 * `values` maps a param's `attr` to the chosen value (blank/omitted dropped).
 */
export function buildEmbedSnippet(widgetKey: string, tenantSubdomain: string,
    values: Record<string, string> = {}): string {
    const sub = tenantSubdomain || 'yourtrack'
    const attrs: string[] = [`data-ridepass="${widgetKey}"`, `data-tenant="${sub}"`]
    const def = getEmbedWidget(widgetKey)
    for (const p of def?.params ?? []) {
        const v = (values[p.attr] ?? '').trim()
        if (v) attrs.push(`data-${p.attr}="${v}"`)
    }
    return `<div ${attrs.join(' ')}></div>\n<script src="${EMBED_SCRIPT_SRC}" async></scr` + `ipt>`
}
