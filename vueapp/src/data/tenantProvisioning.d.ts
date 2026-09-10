// Types for tenantProvisioning.mjs (plain ESM so the doc exporter can run it under Node).
export type Owner = 'RidePass' | 'Track' | 'Both'
export type Automation = 'automatic' | 'assisted' | 'manual'

export interface Where {
    label: string
    /** In-app route (super-admin pages only; tenant admin paths are text). */
    to?: string
    /** External URL. */
    href?: string
}

export interface ProvisioningStep {
    id: string
    title: string
    owner: Owner
    automation: Automation
    where: Where
    steps: string[]
    verify: string
    notes?: string
    cost?: string
    stagingOnly?: boolean
    optional?: boolean
    added: string
}

export interface ProvisioningPhase {
    id: string
    title: string
    intro: string
    steps: ProvisioningStep[]
}

export const provisioningPhases: ProvisioningPhase[]
