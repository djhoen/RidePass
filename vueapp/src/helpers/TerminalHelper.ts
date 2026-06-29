import { loadStripeTerminal, type Terminal, type Reader } from '@stripe/terminal-js'

// Thin wrapper around the Stripe Terminal JS SDK for the web POS + a networked smart reader
// (WisePOS E). The reader is the card device; the browser drives it. Card-present payments can't run
// in-browser without a reader, which is why the big-screen POS pairs one. The connection token (and
// its reader Location) come from our backend, which is connected-account aware for direct-charge
// tenants, so the charge lands on the right Stripe account automatically.

let terminalPromise: Promise<Terminal | null> | null = null

export async function getTerminal(fetchToken: () => Promise<string>): Promise<Terminal | null> {
    if (!terminalPromise) {
        terminalPromise = (async () => {
            const StripeTerminal = await loadStripeTerminal()
            if (!StripeTerminal) return null
            return StripeTerminal.create({
                onFetchConnectionToken: fetchToken,
                // The view watches connection state and prompts a reconnect; nothing to do here.
                onUnexpectedReaderDisconnect: () => { /* no-op */ },
            })
        })()
    }
    return terminalPromise
}

// Discover internet readers (WisePOS E) and connect the first one. `simulated: true` yields Stripe's
// built-in simulated reader for test mode. Returns the connected reader's label; throws on failure.
export async function discoverAndConnect(terminal: Terminal, simulated: boolean): Promise<string> {
    const discover: any = await terminal.discoverReaders({ simulated })
    if (discover.error) throw new Error(discover.error.message)
    const readers = discover.discoveredReaders as Reader[]
    if (!readers || readers.length === 0)
        throw new Error('No card reader found. Make sure the reader is powered on and on the same network.')
    const connect: any = await terminal.connectReader(readers[0])
    if (connect.error) throw new Error(connect.error.message)
    return readers[0].label ?? 'Reader'
}

// Collect + process a card-present PaymentIntent on the connected reader. Throws on failure so the
// caller can surface a real message; on success the PI is captured and our webhook finalizes the sale.
export async function collectAndProcess(terminal: Terminal, clientSecret: string): Promise<void> {
    const collect: any = await terminal.collectPaymentMethod(clientSecret)
    if (collect.error) throw new Error(collect.error.message)
    const result: any = await terminal.processPayment(collect.paymentIntent)
    if (result.error) throw new Error(result.error.message ?? 'The card was not charged. Try again.')
}
