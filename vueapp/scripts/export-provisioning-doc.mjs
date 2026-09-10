// Regenerates docs/tenant-provisioning.md from src/data/tenantProvisioning.mjs so the repo doc
// and the super-admin page never drift. Run from anywhere: `npm run provisioning:doc` (vueapp).
import { writeFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { provisioningPhases } from '../src/data/tenantProvisioning.mjs'

const here = dirname(fileURLToPath(import.meta.url))
const out = resolve(here, '../../docs/tenant-provisioning.md')

const lines = []
lines.push('# Tenant provisioning checklist')
lines.push('')
lines.push('GENERATED from `vueapp/src/data/tenantProvisioning.mjs` by `npm run provisioning:doc` (run in')
lines.push('`vueapp`). Edit the data file, not this document. The same list renders in the app at')
lines.push('super admin, Tenant provisioning (`/SuperAdmin/TenantProvisioning`).')
lines.push('')
lines.push('Legend: **owner** is who does the step; **automation** is `automatic` (happens on its own once')
lines.push('triggered), `assisted` (there is a UI but a person drives it), or `manual` (done by hand in an')
lines.push('outside system such as DNS, SendGrid, Twilio, or an env file). Manual steps are the automation')
lines.push('backlog.')
lines.push('')

const manual = provisioningPhases.flatMap(p => p.steps.filter(s => s.automation === 'manual').map(s => ({ phase: p.title, step: s })))
lines.push('## Still manual today')
lines.push('')
for (const { phase, step } of manual) {
    lines.push(`- **${step.title}** (${phase}; ${step.owner}${step.stagingOnly ? '; staging only' : ''})`)
}
lines.push('')

for (const phase of provisioningPhases) {
    lines.push(`## ${phase.title}`)
    lines.push('')
    lines.push(phase.intro)
    lines.push('')
    for (const s of phase.steps) {
        const tags = [s.owner, s.automation]
        if (s.optional) tags.push('optional')
        if (s.stagingOnly) tags.push('staging only')
        if (s.cost) tags.push(`cost: ${s.cost}`)
        lines.push(`### ${s.title}`)
        lines.push('')
        lines.push(`_${tags.join(' | ')}_ (added ${s.added})`)
        lines.push('')
        const where = s.where.href ? `[${s.where.label}](${s.where.href})` : s.where.to ? `${s.where.label} (\`${s.where.to}\`)` : s.where.label
        lines.push(`**Where:** ${where}`)
        lines.push('')
        s.steps.forEach((line, i) => lines.push(`${i + 1}. ${line}`))
        lines.push('')
        lines.push(`**Verify:** ${s.verify}`)
        if (s.notes) {
            lines.push('')
            lines.push(`**Notes:** ${s.notes}`)
        }
        lines.push('')
    }
}

writeFileSync(out, lines.join('\n') + '\n', 'utf8')
console.log(`wrote ${out} (${provisioningPhases.length} phases, ${provisioningPhases.reduce((n, p) => n + p.steps.length, 0)} steps, ${manual.length} manual)`)
