import { test, expect } from '@playwright/test'
import { confirmPrompt } from '../helpers/ui'

// Custom page lifecycle: create -> appears in the list -> delete -> gone. Self-cleaning, so it
// leaves no stage residue. The title is uniquely tagged so a failed run is easy to spot and remove.

test('custom page create then delete', async ({ page }) => {
    await page.goto('/Admin/Pages')
    await expect(page).not.toHaveURL(/\/Login/)

    const title = `PWTEST Page ${Date.now()}`

    // Create: only the page title is required to save a draft (titleError is the only save
    // gate; the URL path auto-fills from the title, and the rich-text body is optional).
    // "New page" is a router-link styled as a v-btn (`to="/Admin/Pages/New"`), so it renders
    // as an <a>, not a <button>.
    await page.getByRole('link', { name: 'New page' }).click()
    await expect(page).toHaveURL(/\/Admin\/Pages\/New/)
    await page.getByRole('textbox', { name: 'Page title' }).fill(title)
    await page.getByRole('button', { name: 'Create page', exact: true }).click()

    // Saving a new page redirects the SPA to the edit URL (/Admin/Pages/:id) once the create
    // request resolves. Wait for that before navigating away with page.goto, which does a real
    // browser navigation and would otherwise abort the still-in-flight create request.
    await expect(page).toHaveURL(/\/Admin\/Pages\/(?!New$)[^/]+$/)
    await page.goto('/Admin/Pages')
    const row = page.locator('tr', { hasText: title })
    await expect(row).toBeVisible()

    // Delete it and confirm the shared themed prompt.
    await row.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmPrompt(page)

    await expect(page.locator('tr', { hasText: title })).toHaveCount(0)
})
