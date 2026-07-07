// Silent receipt printing to an Epson network receipt printer (e.g. TM-m30) via its ePOS-Print
// HTTP service. We POST an ePOS-Print XML document straight to the printer's IP, so it prints
// automatically with NO browser print dialog. Configure the printer's URL per tablet (it sits next
// to that POS), e.g. "https://192.168.1.50".
//
// Mixed-content note: the POS is served over HTTPS, so the printer must be reachable over HTTPS too
// (the TM-m30 supports it). Trust the printer's certificate on the tablet once, or browsers will
// block the request. A plain http:// printer URL will be blocked from the HTTPS page.

export interface ReceiptLine {
    quantity: number
    name: string
    variantLabel: string | null
    modifierLabels: string[]
    notes: string | null
    lineTotal: number
}

export interface Receipt {
    header: string            // tenant name
    orderNumber: number | null
    lines: ReceiptLine[]
    subtotalCents: number      // pre-tax, pre-discount subtotal
    discountCents: number      // total discount/comp knocked off (0 = none)
    discountLabel: string | null
    taxCents: number
    pricesIncludeTax: boolean  // true = tax already in line prices (labeled "incl.")
    tipCents: number
    totalCents: number
    method: string            // "Cash" | "Card"
}

const money = (c: number) => `$${(c / 100).toFixed(2)}`
const esc = (s: string | null) => (s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')

function buildEposXml(r: Receipt): string {
    let b = ''
    b += `<text align="center" width="2" height="2">${esc(r.header)}&#10;</text>`
    b += `<text align="center" width="3" height="3">#${r.orderNumber ?? '--'}&#10;</text>`
    b += `<text align="left" width="1" height="1"></text>`
    b += `<feed line="1"/>`
    for (const l of r.lines) {
        b += `<text>${l.quantity}x ${esc(l.name)}${l.variantLabel ? ' (' + esc(l.variantLabel) + ')' : ''}  ${money(l.lineTotal)}&#10;</text>`
        for (const m of l.modifierLabels) b += `<text>  + ${esc(m)}&#10;</text>`
        if (l.notes) b += `<text>  "${esc(l.notes)}"&#10;</text>`
    }
    b += `<text>--------------------------------&#10;</text>`
    b += `<text>Subtotal  ${money(r.subtotalCents)}&#10;</text>`
    if (r.discountCents) b += `<text>${esc(r.discountLabel || 'Discount')}  -${money(r.discountCents)}&#10;</text>`
    if (r.taxCents) b += `<text>Tax${r.pricesIncludeTax ? ' (incl.)' : ''}  ${money(r.taxCents)}&#10;</text>`
    if (r.tipCents) b += `<text>Tip  ${money(r.tipCents)}&#10;</text>`
    b += `<text width="2" height="2">Total  ${money(r.totalCents)}&#10;</text>`
    b += `<text>Paid: ${esc(r.method)}&#10;</text>`
    b += `<feed line="3"/><cut type="feed"/>`
    return `<?xml version="1.0" encoding="utf-8"?>`
        + `<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body>`
        + `<epos-print xmlns="http://www.epson-pos.com/schemas/2011/03/epos-print">${b}</epos-print>`
        + `</s:Body></s:Envelope>`
}

// POSTs the receipt to the printer. Resolves on success; throws an informative Error otherwise so the
// caller can surface it (we never fall back to a dialog).
export async function printReceipt(printerUrl: string, r: Receipt): Promise<void> {
    const base = printerUrl.trim().replace(/\/+$/, '')
    if (!base) throw new Error('No receipt printer is configured on this tablet.')
    const url = `${base}/cgi-bin/epos/service.cgi?devid=local_printer&timeout=10000`
    let res: Response
    try {
        res = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'text/xml; charset=utf-8', 'SOAPAction': '""' },
            body: buildEposXml(r),
        })
    } catch {
        // Network/mixed-content/cert failures land here (no HTTP status).
        throw new Error('Could not reach the printer. Check it is on, on this network, and reachable over HTTPS (trust its certificate on the tablet).')
    }
    if (!res.ok) throw new Error(`Printer returned HTTP ${res.status}.`)
    const body = await res.text()
    if (!/success="true"/.test(body)) {
        const code = body.match(/code="([^"]*)"/)?.[1]
        throw new Error(`Printer reported an error${code ? ` (${code})` : ''}.`)
    }
}
