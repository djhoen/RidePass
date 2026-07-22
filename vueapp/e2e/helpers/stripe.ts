import { Page, Frame, Locator } from '@playwright/test'

// Fills a Stripe Payment Element with a TEST card (4242…). The modern Payment Element nests its
// card fields inside cross-origin Stripe iframes, and when the connected account has more than one
// payment method enabled it renders an accordion — the card fields don't exist in the DOM until the
// "Card" section is expanded. So we (1) expand Card if needed, then (2) scan every frame (nested
// included, via page.frames()) for the fields by their stable attributes. Test mode only.
export async function fillStripeCard(page: Page, opts: {
    number?: string; exp?: string; cvc?: string; zip?: string
} = {}) {
    const { number = '4242424242424242', exp = '1234', cvc = '123', zip = '12345' } = opts

    // First visible match for any selector across all frames, polling up to timeoutMs while Stripe mounts.
    const find = async (selectors: string[], timeoutMs = 8000): Promise<Locator | null> => {
        for (let attempt = 0; attempt < Math.ceil(timeoutMs / 400); attempt++) {
            for (const frame of page.frames() as Frame[]) {
                for (const sel of selectors) {
                    const loc = frame.locator(sel).first()
                    if ((await loc.count().catch(() => 0)) && (await loc.isVisible().catch(() => false))) return loc
                }
            }
            await page.waitForTimeout(400)
        }
        return null
    }

    const numberSel = ['input[name="number"]', 'input[autocomplete="cc-number"]', 'input[placeholder*="1234"]']

    // If the card field isn't immediately present, the accordion is probably collapsed on another
    // method — expand the Card section by clicking its header, then look again.
    let num = await find(numberSel, 4000)
    if (!num) {
        for (const frame of page.frames() as Frame[]) {
            const header = frame.getByText(/^\s*(Card|Credit or debit card)\s*$/i).first()
            if ((await header.count().catch(() => 0)) && (await header.isVisible().catch(() => false))) {
                await header.click().catch(() => {})
                break
            }
        }
        num = await find(numberSel, 8000)
    }
    if (!num) throw new Error('Stripe card number field not found (accordion not expandable to Card?)')
    await num.fill(number)

    const expiry = await find(['input[name="expiry"]', 'input[autocomplete="cc-exp"]', 'input[placeholder*="MM"]'])
    await expiry?.fill(exp)

    const cvc2 = await find(['input[name="cvc"]', 'input[autocomplete="cc-csc"]', 'input[placeholder*="CVC"]'])
    await cvc2?.fill(cvc)

    // Postal code only appears when Stripe is configured to collect it.
    const postal = await find(['input[name="postalCode"]', 'input[autocomplete="postal-code"]'], 2000)
    if (postal) await postal.fill(zip).catch(() => {})
}
