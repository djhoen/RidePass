import { Page, expect } from '@playwright/test'

// Credentials come from env so we never hardcode a real password. admin@acme.test is
// the seeded demo tenant admin (promoted to tenant_admin in Script0003_PromoteDemoAdmins).
// The seed leaves password hashes as placeholders, so set a known one first: either
// run /ResetPassword in the app, or update the hash directly in the dev DB, then export:
//   $env:E2E_ADMIN_EMAIL="admin@acme.test"; $env:E2E_ADMIN_PASSWORD="..."
export const ADMIN = {
  email: process.env.E2E_ADMIN_EMAIL || 'admin@acme.test',
  password: process.env.E2E_ADMIN_PASSWORD || 'Password123!',
}

// Stripe's universal test card. Always succeeds, no 3DS. See stripe.com/docs/testing.
export const TEST_CARD = {
  number: '4242 4242 4242 4242',
  expiry: '12 / 34',
  cvc: '123',
  zip: '12345',
}

/**
 * Log in through the real UI form and wait for the post-login route.
 * Login.vue routes tenant staff to /Admin/Dashboard, riders to /, super_admin to /SuperAdmin.
 */
export async function login(page: Page, email: string, password: string) {
  await page.goto('/Login')
  // The footer's NewsletterSignup also has an email field, so a bare
  // input[type=email] is ambiguous. Scope to the login form, which is the only
  // form on the page containing a password input.
  const form = page.locator('form').filter({ has: page.locator('input[type="password"]') })
  await form.locator('input[type="email"]').fill(email)
  await form.locator('input[type="password"]').fill(password)
  await form.getByRole('button', { name: 'Login' }).click()
  // Leaving /Login is the signal the credentials were accepted; the error path
  // keeps us on /Login and shows a snackbar instead.
  await expect(page).not.toHaveURL(/\/Login/, { timeout: 10_000 })
}

/**
 * Fill the Stripe Payment Element. The Element renders inside a cross-origin iframe,
 * which is exactly the case Playwright's frameLocator handles cleanly (auto-waits for
 * the frame and its fields). Placeholders can shift with Stripe's JS version or locale,
 * so we fall back across a couple of known labels.
 */
export async function fillStripePaymentElement(page: Page, card = TEST_CARD) {
  const frame = page.frameLocator('iframe[title="Secure payment input frame"]')

  const cardNumber = frame.getByPlaceholder('1234 1234 1234 1234')
  await cardNumber.waitFor({ state: 'visible', timeout: 15_000 })
  await cardNumber.fill(card.number)

  await frame.getByPlaceholder('MM / YY').fill(card.expiry)
  await frame.getByPlaceholder('CVC').fill(card.cvc)

  // The Payment Element shows a ZIP field for US cards; it may not always be present.
  const zip = frame.getByPlaceholder('12345')
  if (await zip.count()) {
    await zip.fill(card.zip)
  }
}

/**
 * Draw a quick stroke on the SignaturePad canvas. The pad listens for pointer
 * events, which Playwright's mouse actions emit, so this is enough to mark the
 * waiver as signed and enable the "sign & continue" button.
 */
export async function drawSignature(page: Page) {
  const canvas = page.locator('canvas').first()
  await canvas.waitFor({ state: 'visible' })
  const box = await canvas.boundingBox()
  if (!box) return
  await page.mouse.move(box.x + 20, box.y + box.height / 2)
  await page.mouse.down()
  await page.mouse.move(box.x + box.width * 0.4, box.y + 20)
  await page.mouse.move(box.x + box.width * 0.7, box.y + box.height - 20)
  await page.mouse.move(box.x + box.width - 20, box.y + box.height / 2)
  await page.mouse.up()
}
