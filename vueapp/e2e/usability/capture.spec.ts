import { test } from '@playwright/test'

// Captures full-page screenshots of the key flows at desktop and mobile widths for the usability
// review. Read-only (no data changes). Screenshots land in e2e/usability/shots/ and are evaluated
// against usability heuristics separately.

const EVENT_ID = 'f6b0692d-e6ec-442f-80bb-7c20771c4a01' // seeded "Upcoming Practice"
const DIR = 'e2e/usability/shots'
const DESKTOP = { width: 1280, height: 900 }
const MOBILE = { width: 390, height: 844 }

async function shot(page: any, name: string) {
    await page.waitForTimeout(1500) // let async views settle
    await page.screenshot({ path: `${DIR}/${name}.png`, fullPage: true })
}

const CUSTOMER: [string, string][] = [
    ['home', '/'],
    ['events', '/Events'],
    ['event-detail', `/Event/${EVENT_ID}`],
    ['season-passes', '/SeasonPasses'],
    ['gift-card', '/GiftCard'],
    ['order-food', '/Order'],
    ['membership', '/Membership'],
]
const ACCOUNT: [string, string][] = [
    ['user-profile', '/User/Profile'],
    ['user-mypasses', '/User/MyPasses'],
    ['user-upcoming', '/User/Upcoming'],
]
const ADMIN: [string, string][] = [
    ['admin-dashboard', '/Admin/Dashboard'],
    ['admin-events', '/Admin/Events'],
    ['admin-workorders', '/Admin/BikeShop/WorkOrders'],
    ['admin-reports', '/Admin/Reports'],
]

test('capture desktop (authed)', async ({ page }) => {
    await page.setViewportSize(DESKTOP)
    for (const [name, route] of [...CUSTOMER, ...ACCOUNT, ...ADMIN]) {
        await page.goto(route)
        await shot(page, `desktop-${name}`)
    }
})

test('capture mobile (authed)', async ({ page }) => {
    await page.setViewportSize(MOBILE)
    for (const [name, route] of CUSTOMER) {
        await page.goto(route)
        await shot(page, `mobile-${name}`)
    }
})

test('capture event purchase steps', async ({ page }) => {
    await page.setViewportSize(DESKTOP)
    await page.goto(`/Event/${EVENT_ID}`)
    await page.waitForTimeout(1200)
    const specLine = page.locator('.evt-line', { hasText: 'Spectator Gate' }).first()
    if (await specLine.isVisible().catch(() => false)) {
        await specLine.getByRole('button').last().click()
        await shot(page, 'event-step1-select')
        await page.getByRole('button', { name: 'Continue', exact: true }).click()
        await shot(page, 'event-step2-details')
    }
})

test.describe('unauthed', () => {
    test.use({ storageState: { cookies: [], origins: [] } })
    test('capture auth pages', async ({ page }) => {
        await page.setViewportSize(DESKTOP)
        for (const [name, route] of [['login', '/Login'], ['signup', '/SignUp']] as [string, string][]) {
            await page.goto(route)
            await shot(page, `desktop-${name}`)
        }
    })
})
