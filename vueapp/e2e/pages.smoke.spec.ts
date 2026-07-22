import { test, expect } from '@playwright/test'

// Whole-app render smoke: visit every static (non-parameterized) page as the signed-in admin and
// fail any that throws an uncaught runtime error or redirects to login unexpectedly. This is the
// cheap safety net that catches crashes, broken imports (e.g. a case-sensitive import that only
// fails on Linux), and blank-screen setup errors across the app — not deep behavior.
//
// Excluded on purpose: parameterized routes (:id/:token/:slug need real data), pre-auth pages
// (/Login, /SignUp, /ResetPassword, /VerifyEmail), /embed/* iframe routes, and /SuperAdmin/* (the
// QA login is a tenant admin, not a platform super-admin, so those correctly redirect/403).

// Public + customer-facing pages.
const PUBLIC = [
    '/', '/Events', '/Discover', '/Blog', '/SeasonPasses', '/Shop', '/ForTracks',
    '/Feedback', '/Membership', '/GiftCard', '/Order', '/Waiver',
]

// Signed-in rider pages.
const USER = [
    '/User/Profile', '/User/MyPasses', '/User/Upcoming', '/User/Rewards', '/User/SeasonPasses',
]

// Tenant-admin pages (motoland has every feature enabled).
const ADMIN = [
    '/Admin/Dashboard', '/Admin/Events', '/Admin/EventTypes', '/Admin/Blackouts',
    '/Admin/Extras', '/Admin/SeasonPasses',
    '/Admin/Instructors', '/Admin/Waiver',
    '/Admin/BikeShop', '/Admin/BikeShop/WorkOrders', '/Admin/BikeShop/Sales',
    '/Admin/BikeShop/Rentals', '/Admin/BikeShop/Settings', '/Admin/BikeShop/Register',
    '/Admin/Concessions', '/Admin/ConcessionMenu', '/Admin/ConcessionPos', '/Admin/ConcessionKitchen',
    '/Admin/ConcessionOrders', '/Admin/ConcessionPickupBoard', '/Admin/ConcessionComps',
    '/Admin/Counter', '/Admin/Scanner', '/Admin/RedeemTickets',
    '/Admin/Purchases', '/Admin/Payouts', '/Admin/StoreCredit', '/Admin/Coupons', '/Admin/Rewards',
    '/Admin/Customers', '/Admin/Users', '/Admin/Subscribers', '/Admin/Suppression',
    '/Admin/Campaigns', '/Admin/Inbox', '/Admin/Surveys', '/Admin/Feedback',
    '/Admin/Blog', '/Admin/Blog/New', '/Admin/Pages', '/Admin/Pages/New',
    '/Admin/Reports',
    '/Admin/Settings/General', '/Admin/Settings/Branding', '/Admin/Settings/Features',
    '/Admin/Settings/HomePage', '/Admin/Settings/Membership', '/Admin/Settings/Payments',
    '/Admin/Settings/QuickBooks', '/Admin/Settings/Sms',
]

const ROUTES = [...new Set([...PUBLIC, ...USER, ...ADMIN])]

test('every static page renders without a runtime error', async ({ page }) => {
    test.setTimeout(240_000)
    const broken: string[] = []

    for (const route of ROUTES) {
        const errors: string[] = []
        const onError = (e: Error) => errors.push(e.message)
        page.on('pageerror', onError)
        try {
            await page.goto(route, { waitUntil: 'domcontentloaded' })
            await page.waitForTimeout(700) // let the view mount / async setup run
            const url = page.url()
            if (/\/Login(\?|$)/.test(url)) {
                broken.push(`${route} -> redirected to Login`)
            } else if (errors.length) {
                broken.push(`${route} -> pageerror: ${errors[0].slice(0, 140)}`)
            }
        } catch (e: any) {
            broken.push(`${route} -> navigation failed: ${String(e.message).slice(0, 140)}`)
        } finally {
            page.off('pageerror', onError)
        }
    }

    if (broken.length) console.log('BROKEN PAGES:\n' + broken.join('\n'))
    else console.log(`All ${ROUTES.length} pages rendered clean.`)
    expect(broken, `pages that failed to render:\n${broken.join('\n')}`).toEqual([])
})
