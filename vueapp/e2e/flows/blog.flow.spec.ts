import { test, expect } from '@playwright/test'
import { confirmPrompt } from '../helpers/ui'

// Blog post lifecycle: create -> appears in the list -> delete -> gone. Self-cleaning, so it
// leaves no stage residue. The title is uniquely tagged so a failed run is easy to spot and remove.

test('blog post create then delete', async ({ page }) => {
    await page.goto('/Admin/Blog')
    await expect(page).not.toHaveURL(/\/Login/)

    const title = `PWTEST Post ${Date.now()}`

    // Create: only the title is required to save a draft (titleError is the only save gate;
    // the rich-text body, slug, and excerpt are all optional). "New post" is a router-link
    // styled as a v-btn (`to="/Admin/Blog/New"`), so it renders as an <a>, not a <button>.
    await page.getByRole('link', { name: 'New post' }).click()
    await expect(page).toHaveURL(/\/Admin\/Blog\/New/)
    await page.getByRole('textbox', { name: 'Title' }).fill(title)
    await page.getByRole('button', { name: 'Create post' }).click()

    // Saving a new post redirects the SPA to the edit URL (/Admin/Blog/:id) once the create
    // request resolves. Wait for that before navigating away with page.goto, which does a real
    // browser navigation and would otherwise abort the still-in-flight create request.
    await expect(page).toHaveURL(/\/Admin\/Blog\/(?!New$)[^/]+$/)
    await page.goto('/Admin/Blog')
    const row = page.locator('tr', { hasText: title })
    await expect(row).toBeVisible()

    // Delete it and confirm the shared themed prompt.
    await row.getByRole('button', { name: 'Delete', exact: true }).click()
    await confirmPrompt(page)

    await expect(page.locator('tr', { hasText: title })).toHaveCount(0)
})
