import { test } from '@playwright/test'
import * as fs from 'fs'

/**
 * Whole-site usability sweep.
 *
 * Visits every STATIC route in the router at desktop (and the customer-facing ones at mobile),
 * capturing a screenshot plus a machine-readable signal record per route.
 *
 * The signals are the point. 117 routes x 2 widths is far too many screenshots to review by eye,
 * and doing so would miss the things that matter most anyway: a page that throws in the console, a
 * page whose data call 500s while the UI renders a cheerful empty state, a page that renders
 * nothing at all. So each route records how it actually behaved, the report is triaged first, and
 * only the flagged routes get looked at.
 *
 * READ-ONLY. Navigation and observation only: nothing here clicks a button that writes.
 */

const DIR = 'e2e/usability/shots/fullsite'
const REPORT = 'e2e/usability/fullsite-report.json'
// Each record is appended as it is captured. An in-memory array only survives if the test finishes,
// and this sweep is exactly the kind of long run that gets cut short, so partial results must persist.
const LOG = 'e2e/usability/fullsite-signals.jsonl'
const DESKTOP = { width: 1440, height: 900 }
const MOBILE = { width: 390, height: 844 }

// Every static route in src/router/router.ts. SuperAdmin is included deliberately: the tenant-admin
// session should be REFUSED there, and silently rendering it would itself be the finding.
const ROUTES: string[] = [
    '/', '/Discover', '/Events', '/SeasonPasses', '/Membership', '/GiftCard', '/Shop', '/Rentals',
    '/Order', '/Blog', '/Feedback', '/ForTracks', '/Waiver', '/Login', '/SignUp', '/ResetPassword',
    '/VerifyEmail', '/EmailUnsubscribe', '/NotFound',

    '/User/Profile', '/User/MyPasses', '/User/MyOrders', '/User/SeasonPasses', '/User/Upcoming',

    '/Admin/Dashboard', '/Admin/Events', '/Admin/EventTypes', '/Admin/Blackouts', '/Admin/Instructors',
    '/Admin/Counter', '/Admin/RedeemTickets', '/Admin/Scanner', '/Admin/AddOnCheckIn',
    '/Admin/RiderReport', '/Admin/SpectatorReport', '/Admin/Reports', '/Admin/Purchases',
    '/Admin/Customers', '/Admin/Users', '/Admin/StoreCredit', '/Admin/Payouts',
    '/Admin/SeasonPasses', '/Admin/PassUpgrades', '/Admin/EmployeePasses', '/Admin/BuddyPassUsage',
    '/Admin/Packages', '/Admin/Extras', '/Admin/Coupons',
    '/Admin/Concessions', '/Admin/ConcessionPos', '/Admin/ConcessionKitchen', '/Admin/ConcessionMenu',
    '/Admin/ConcessionPickupBoard', '/Admin/ConcessionOrders', '/Admin/ConcessionInventory',
    '/Admin/ConcessionComps',
    '/Admin/BikeShop', '/Admin/BikeShop/Register', '/Admin/BikeShop/Rentals',
    '/Admin/BikeShop/RentalBoard', '/Admin/BikeShop/WorkOrders', '/Admin/BikeShop/Sales',
    '/Admin/BikeShop/Settings',
    '/Admin/Waiver', '/Admin/SignedWaivers', '/Admin/WaiverCompliance', '/Admin/WaiverRequests',
    '/Admin/Blog', '/Admin/Blog/New', '/Admin/Pages', '/Admin/Pages/New', '/Admin/Branding',
    '/Admin/Campaigns', '/Admin/Automations', '/Admin/Subscribers', '/Admin/Suppression',
    '/Admin/Surveys', '/Admin/Feedback', '/Admin/Inbox', '/Admin/StaffActivity',
    '/Admin/Settings/General', '/Admin/Settings/Features', '/Admin/Settings/Discounts',
    '/Admin/Settings/StaffAccess', '/Admin/Settings/HomePage', '/Admin/Settings/Branding',
    '/Admin/Settings/Payments', '/Admin/Settings/QuickBooks', '/Admin/Settings/Membership',
    '/Admin/Settings/Sms',

    '/SuperAdmin', '/SuperAdmin/Tenants', '/SuperAdmin/Users', '/SuperAdmin/Analytics',
    '/SuperAdmin/Payouts', '/SuperAdmin/Refunds', '/SuperAdmin/Disputes', '/SuperAdmin/Reconcile',
    '/SuperAdmin/Audit', '/SuperAdmin/Marketing', '/SuperAdmin/HomePage', '/SuperAdmin/ForTracks',
    '/SuperAdmin/MiscSettings', '/SuperAdmin/Bootstrap',

    '/embed/events', '/embed/calendar', '/embed/blog', '/embed/shop', '/embed/order',
    '/embed/rentals', '/embed/seasonpasses', '/embed/membership', '/embed/giftcard',
    '/embed/feedback', '/embed/status',
]

// Rendered at phone width too: everything a paying customer might open on their phone at the gate.
const MOBILE_ROUTES = new Set([
    '/', '/Discover', '/Events', '/SeasonPasses', '/Membership', '/GiftCard', '/Shop', '/Rentals',
    '/Order', '/Login', '/SignUp', '/User/Profile', '/User/MyPasses', '/User/MyOrders',
    '/User/Upcoming', '/Admin/Scanner', '/Admin/RedeemTickets', '/Admin/ConcessionPos',
])

// Copy that means the page told the user something went wrong.
const ERROR_COPY = /couldn'?t|could not|failed|went wrong|unable to|error|not authorized|forbidden|no tenant/i
// Copy that means "there is nothing here", which is only correct when nothing ALSO failed.
const EMPTY_COPY = /no .{0,30}(yet|found|scheduled|match)|nothing .{0,20}(yet|here|scheduled)|is empty|none yet/i

interface RouteSignal {
    route: string
    width: 'desktop' | 'mobile'
    finalUrl: string
    redirected: boolean
    title: string
    textLength: number
    consoleErrors: string[]
    pageErrors: string[]
    failedRequests: { url: string; status: number }[]
    showsError: boolean
    showsEmpty: boolean
    shot: string
}

const signals: RouteSignal[] = []

async function visit(page: any, route: string, width: 'desktop' | 'mobile') {
    const consoleErrors: string[] = []
    const pageErrors: string[] = []
    const failedRequests: { url: string; status: number }[] = []

    const onConsole = (m: any) => { if (m.type() === 'error') consoleErrors.push(m.text().slice(0, 300)) }
    const onPageError = (e: any) => pageErrors.push(String(e).slice(0, 300))
    const onResponse = (r: any) => {
        // Only our own API matters; third-party noise (Stripe, fonts) is not a site defect.
        if (r.status() >= 400 && r.url().includes('/api/')) {
            failedRequests.push({ url: r.url().replace(/^https?:\/\/[^/]+/, '').slice(0, 160), status: r.status() })
        }
    }
    page.on('console', onConsole)
    page.on('pageerror', onPageError)
    page.on('response', onResponse)

    let finalUrl = ''
    try {
        await page.goto(route, { waitUntil: 'domcontentloaded', timeout: 30_000 })
        await page.waitForTimeout(2200)   // let the async views settle
        finalUrl = page.url()
    } catch (e) {
        pageErrors.push('NAVIGATION FAILED: ' + String(e).slice(0, 200))
        finalUrl = page.url()
    }

    const slug = route.replace(/^\//, '').replace(/\//g, '_') || 'root'
    const shot = `${width}-${slug}.png`
    try { await page.screenshot({ path: `${DIR}/${shot}`, fullPage: true }) } catch { /* keep going */ }

    let text = ''
    try { text = await page.locator('body').innerText({ timeout: 5000 }) } catch { /* blank */ }

    page.off('console', onConsole)
    page.off('pageerror', onPageError)
    page.off('response', onResponse)

    const path = finalUrl.replace(/^https?:\/\/[^/]+/, '').split('?')[0]
    const record = {
        route, width, finalUrl: path,
        redirected: path.toLowerCase() !== route.toLowerCase(),
        title: await page.title().catch(() => ''),
        textLength: text.trim().length,
        consoleErrors, pageErrors, failedRequests,
        showsError: ERROR_COPY.test(text),
        showsEmpty: EMPTY_COPY.test(text),
        shot,
    }
    signals.push(record)
    fs.appendFileSync(LOG, JSON.stringify(record) + String.fromCharCode(10))
}

test.describe.configure({ retries: 0 })

test('full site sweep - desktop', async ({ page }) => {
    test.setTimeout(30 * 60 * 1000)
    fs.mkdirSync(DIR, { recursive: true })
    fs.writeFileSync(LOG, '')
    await page.setViewportSize(DESKTOP)
    for (const route of ROUTES) await visit(page, route, 'desktop')
})

test('full site sweep - mobile', async ({ page }) => {
    test.setTimeout(15 * 60 * 1000)
    await page.setViewportSize(MOBILE)
    for (const route of ROUTES.filter(r => MOBILE_ROUTES.has(r))) await visit(page, route, 'mobile')
})

test.afterAll(() => {
    // Read back the append log so the report reflects every route captured across both tests,
    // even if one of them was cut short.
    const all: RouteSignal[] = fs.existsSync(LOG)
        ? fs.readFileSync(LOG, 'utf8').split(String.fromCharCode(10)).filter(Boolean).map(l => JSON.parse(l))
        : signals
    fs.writeFileSync(REPORT, JSON.stringify(all, null, 2))
    const flagged = all.filter(s =>
        s.pageErrors.length || s.failedRequests.length || s.consoleErrors.length ||
        s.textLength < 200 || (s.showsError && s.showsEmpty))
    // eslint-disable-next-line no-console
    console.log(`\nSWEPT ${signals.length} page loads, ${flagged.length} flagged. Report: ${REPORT}`)
})
