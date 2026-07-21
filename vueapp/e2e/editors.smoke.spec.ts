import { test, expect } from '@playwright/test'

// Verifies two of the UX-audit fixes shipped this session.

test('RichTextEditor link button opens the themed dialog (not a native prompt)', async ({ page }) => {
    // A native window.prompt would block Playwright and require a dialog handler; the fix uses a
    // DOM dialog. If we ever regress to window.prompt, this fails/hangs, which is the signal.
    let nativePromptFired = false
    page.on('dialog', async (d) => { nativePromptFired = true; await d.dismiss() })

    await page.goto('/Admin/Blog/New')
    await expect(page).not.toHaveURL(/\/Login/)

    // The link button carries aria-label="Insert link".
    const linkBtn = page.getByRole('button', { name: /Insert link/i })
    await expect(linkBtn).toBeVisible()
    await linkBtn.click()

    // The themed dialog appears with a URL field.
    await expect(page.getByText(/Insert link|Edit link/i)).toBeVisible()
    await expect(page.getByLabel(/Link URL/i)).toBeVisible()
    expect(nativePromptFired, 'a native window.prompt fired — the themed dialog regressed').toBe(false)
    await page.screenshot({ path: 'e2e/results/richtext-link-dialog.png' })
})

test('EventRiders CSV export is an authed button, not a bare href', async ({ page }) => {
    await page.goto('/Admin/Reports')
    await expect(page).not.toHaveURL(/\/Login/)
    const eventRiders = page.getByText(/Event Riders/i).first()
    if (!(await eventRiders.isVisible().catch(() => false))) {
        test.skip(true, 'Event Riders report not available for this account/tenant')
        return
    }
    await eventRiders.click()
    const csvBtn = page.getByRole('button', { name: /Export Trackside CSV/i })
    await expect(csvBtn).toBeVisible()
    // The fix replaced the <a href> with a real button (no href attribute).
    await expect(csvBtn).not.toHaveAttribute('href', /.*/)
})
