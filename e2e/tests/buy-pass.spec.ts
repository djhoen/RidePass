import { test, expect } from '@playwright/test'
import { loginViaApi } from './helpers/api'
import { ensureFuturePurchasableEvent } from './helpers/data'
import { fillStripePaymentElement, drawSignature } from './helpers'

// End-to-end buy flow with no seed dependency: the helper reuses a future
// purchasable event, bumps a stale one's date, or creates one, whichever is
// needed. Then we walk the Vuetify stepper to the Stripe Payment Element (a
// cross-origin iframe) and fill the test card.
test('buy flow reaches and fills the Stripe payment element', async ({ page, request }) => {
  const { token } = await loginViaApi(request)
  const event = await ensureFuturePurchasableEvent(request, token)

  await page.goto(`/BuyPass?eventId=${event.id}`)

  // Choose the first pass (already pre-selected if there's only one).
  const firstRadio = page.locator('input[type="radio"]').first()
  if (await firstRadio.count()) {
    await firstRadio.check()
  }

  // Advance Select -> (Add-ons) -> Discounts -> (Waiver) -> Payment. Click the
  // primary action inside the stepper (scoped so we don't grab nav/footer
  // buttons), signing the waiver canvas if that step appears. Bounded so a
  // blocked flow can't loop forever.
  const stepper = page.locator('.v-stepper')
  const paymentElement = page.locator('#payment-element')
  for (let i = 0; i < 6 && !(await paymentElement.isVisible().catch(() => false)); i++) {
    if (await page.locator('canvas').first().isVisible().catch(() => false)) {
      await drawSignature(page)
    }
    const next = stepper.getByRole('button', { name: /continue|agree|^pay/i }).first()
    if (!(await next.isEnabled().catch(() => false))) break
    await next.click()
    await page.waitForTimeout(300)
  }

  await expect(paymentElement).toBeVisible({ timeout: 15_000 })
  await fillStripePaymentElement(page)

  // Stops short of clicking Pay so the test stays read-only against Stripe.
})
