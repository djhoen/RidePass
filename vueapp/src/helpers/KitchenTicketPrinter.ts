// Silent kitchen ticket printing to an Epson network receipt printer, over the same ePOS-Print HTTP
// service the customer receipt uses (see ReceiptPrinter.ts). Separate from that file on purpose: a
// receipt is a money document (subtotal, tax, tip, total) and a kitchen ticket is a work order. The
// kitchen never wants prices, and the cook needs the quantity and the modifiers to be readable from
// arm's length across a hot line, so this prints far larger and carries no totals at all.
//
// Printing happens from the browser because it has to: the API runs in the cloud and cannot reach a
// printer sitting on the venue LAN. Whichever tablet takes the payment is the device that sends the
// tickets, so it must be able to reach every configured printer by IP and trust each certificate.
//
// Mixed-content note: the POS is served over HTTPS, so printer URLs must be https too. The admin
// screen rejects http:// at save time rather than letting the cashier find out at the counter.

export interface KitchenTicketLine {
    quantity: number
    name: string
    variantLabel: string | null
    modifierLabels: string[]
    notes: string | null
}

export interface KitchenTicket {
    /** Tenant name, small, just so a shared printer's output is identifiable. */
    header: string
    /** Null when the order number hasn't been assigned yet; prints as "--". */
    orderNumber: number | null
    /** Blank for an anonymous counter order. */
    customerName: string | null
    /** Printer's own name (e.g. "Grill"), so a cook knows the ticket reached the right station. */
    stationLabel: string | null
    /** Order placed time, printed so a cook can see how long a ticket has been sitting. */
    placedAt: Date
    lines: KitchenTicketLine[]
    /** Marks the ticket as a duplicate so the line doesn't cook an order twice. */
    isReprint?: boolean
}

function esc(s: string): string {
    return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;').replace(/'/g, '&apos;')
}

function timeLabel(d: Date): string {
    return d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
}

function buildEposXml(t: KitchenTicket): string {
    let b = ''

    if (t.isReprint) b += `<text align="center" width="2" height="2">*** REPRINT ***&#10;</text>`

    b += `<text align="center" width="1" height="1">${esc(t.header)}&#10;</text>`
    if (t.stationLabel) b += `<text align="center" width="2" height="2">${esc(t.stationLabel)}&#10;</text>`

    // The order number is what the cook and the pass call out, so it's the biggest thing on the page.
    b += `<text align="center" width="4" height="4">#${t.orderNumber ?? '--'}&#10;</text>`

    if (t.customerName) b += `<text align="center" width="2" height="2">${esc(t.customerName)}&#10;</text>`

    b += `<text align="left" width="1" height="1"></text>`
    b += `<text>${esc(timeLabel(t.placedAt))}&#10;</text>`
    b += `<text>--------------------------------&#10;</text>`

    for (const l of t.lines) {
        // Double height so quantity and item read from across the line. No prices: this is a work
        // order, and a cook reading a dollar figure is a cook reading the wrong document.
        b += `<text width="2" height="2">${l.quantity}x ${esc(l.name)}&#10;</text>`
        if (l.variantLabel) b += `<text>   (${esc(l.variantLabel)})&#10;</text>`
        for (const m of l.modifierLabels) b += `<text width="1" height="2">   + ${esc(m)}&#10;</text>`
        // Notes are where allergies and "no onions" live, so they get emphasis rather than a footnote.
        if (l.notes) b += `<text width="1" height="2" reverse="true">   ${esc(l.notes)}&#10;</text>`
        b += `<feed line="1"/>`
    }

    b += `<feed line="2"/><cut type="feed"/>`
    return `<?xml version="1.0" encoding="utf-8"?>`
        + `<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body>`
        + `<epos-print xmlns="http://www.epson-pos.com/schemas/2011/03/epos-print">${b}</epos-print>`
        + `</s:Body></s:Envelope>`
}

// POSTs one ticket to one printer. Resolves on success, throws an informative Error otherwise so the
// caller can name which printer failed.
export async function printKitchenTicket(printerUrl: string, t: KitchenTicket): Promise<void> {
    const base = printerUrl.trim().replace(/\/+$/, '')
    if (!base) throw new Error('No address configured for this printer.')
    const url = `${base}/cgi-bin/epos/service.cgi?devid=local_printer&timeout=10000`
    let res: Response
    try {
        res = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'text/xml; charset=utf-8', 'SOAPAction': '""' },
            body: buildEposXml(t),
        })
    } catch {
        // Network, mixed-content and certificate failures all land here with no HTTP status.
        throw new Error('could not be reached (check it is on, on this network, and trusted over HTTPS)')
    }
    if (!res.ok) throw new Error(`returned HTTP ${res.status}`)
    const body = await res.text()
    if (!/success="true"/.test(body)) {
        const code = body.match(/code="([^"]*)"/)?.[1]
        throw new Error(`reported an error${code ? ` (${code})` : ''}`)
    }
}
