import { test, expect } from '@playwright/test'

// Survey lifecycle: create -> appears in the admin list.
//
// NOT self-cleaning: there is no delete for a whole survey anywhere in the admin UI or in
// SurveyService (only individual question delete via SurveyService.deleteQuestion, used on
// the SurveyEdit screen). So this test leaves one inert PWTEST survey behind in 'draft'
// status forever. That's low-risk residue: a draft survey is never published, never sent as
// an invite to any customer, collects no responses, and is invisible to riders — it only
// clutters the admin Surveys list.

test('survey create then appears in list', async ({ page }) => {
    await page.goto('/Admin/Surveys')
    await expect(page).not.toHaveURL(/\/Login/)

    const name = `PWTEST Survey ${Date.now()}`
    const title = `PWTEST Survey Title ${Date.now()}`

    // Create: only Internal name + Title are required (SurveyEdit.create() validates both).
    await page.getByRole('button', { name: 'New Survey' }).click()
    const dlg = page.getByRole('dialog')
    await dlg.getByRole('textbox', { name: 'Internal name', exact: true }).fill(name)
    await dlg.getByRole('textbox', { name: 'Title', exact: true }).fill(title)
    await dlg.getByRole('button', { name: 'Create', exact: true }).click()

    // A successful create navigates straight to the new survey's edit page.
    await expect(page).toHaveURL(/\/Admin\/Surveys\/[^/]+$/)

    // Go back to the list and confirm it's there (name is shown bold, title as a caption below it).
    await page.goto('/Admin/Surveys')
    const row = page.locator('tr', { hasText: name })
    await expect(row).toBeVisible()
    await expect(row).toContainText(title)
})
