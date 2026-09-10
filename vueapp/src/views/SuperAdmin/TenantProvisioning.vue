<template>
    <v-container>
        <div class="d-flex align-center flex-wrap ga-3 mb-2">
            <h1 class="text-h4">Tenant provisioning</h1>
            <v-spacer></v-spacer>
            <v-btn variant="tonal" prepend-icon="mdi-content-copy" @click="copyChecklist">Copy as checklist</v-btn>
        </div>
        <p class="text-body-2 text-medium-emphasis mb-4">
            Every step it takes to stand up a track, including the ones we still do by hand. The list lives in
            <code>vueapp/src/data/tenantProvisioning.mjs</code>; edit it there and run
            <code>npm run provisioning:doc</code> so <code>docs/tenant-provisioning.md</code> matches this page.
        </p>

        <!-- The automation backlog, up front: manual steps are the ones that cost us time per tenant. -->
        <v-alert type="warning" variant="tonal" density="comfortable" class="mb-4">
            <div class="font-weight-medium mb-1">Still manual today ({{ manualSteps.length }})</div>
            <ul class="pl-4 mb-0">
                <li v-for="s in manualSteps" :key="s.id">
                    <a :href="'#' + s.id" @click.prevent="jumpTo(s.id)">{{ s.title }}</a>
                    <span class="text-medium-emphasis"> ({{ s.owner }}<template v-if="s.stagingOnly">, staging only</template>)</span>
                </li>
            </ul>
        </v-alert>

        <div class="d-flex flex-wrap ga-2 mb-4">
            <v-chip size="small" label variant="outlined" prepend-icon="mdi-account-hard-hat">Owner: who does it</v-chip>
            <v-chip size="small" label :color="automationColor('automatic')">automatic: happens on its own</v-chip>
            <v-chip size="small" label :color="automationColor('assisted')">assisted: UI exists, a person drives it</v-chip>
            <v-chip size="small" label :color="automationColor('manual')">manual: outside system, by hand</v-chip>
        </div>

        <v-expansion-panels v-model="openPhases" multiple variant="accordion">
            <v-expansion-panel v-for="phase in phases" :key="phase.id" :value="phase.id">
                <v-expansion-panel-title>
                    <div class="d-flex align-center ga-3 flex-wrap">
                        <span class="text-subtitle-1 font-weight-medium">{{ phase.title }}</span>
                        <v-chip size="x-small" label>{{ phase.steps.length }} steps</v-chip>
                        <v-chip v-if="countManual(phase)" size="x-small" label color="error">{{ countManual(phase) }} manual</v-chip>
                    </div>
                </v-expansion-panel-title>
                <v-expansion-panel-text>
                    <p class="text-body-2 text-medium-emphasis mb-3">{{ phase.intro }}</p>
                    <v-card v-for="s in phase.steps" :id="s.id" :key="s.id" variant="outlined" class="mb-3">
                        <v-card-title class="d-flex align-center flex-wrap ga-2 text-subtitle-1">
                            <span>{{ s.title }}</span>
                            <v-chip size="x-small" label variant="outlined">{{ s.owner }}</v-chip>
                            <v-chip size="x-small" label :color="automationColor(s.automation)">{{ s.automation }}</v-chip>
                            <v-chip v-if="s.optional" size="x-small" label>optional</v-chip>
                            <v-chip v-if="s.stagingOnly" size="x-small" label color="warning">staging only</v-chip>
                            <v-chip v-if="s.cost" size="x-small" label color="error" prepend-icon="mdi-cash">{{ s.cost }}</v-chip>
                        </v-card-title>
                        <v-card-text>
                            <div class="mb-2">
                                <span class="font-weight-medium">Where: </span>
                                <router-link v-if="s.where.to" :to="s.where.to">{{ s.where.label }}</router-link>
                                <a v-else-if="s.where.href" :href="s.where.href" target="_blank" rel="noopener">{{ s.where.label }}
                                    <v-icon size="x-small">mdi-open-in-new</v-icon></a>
                                <span v-else>{{ s.where.label }}</span>
                            </div>
                            <ol class="pl-5 mb-2">
                                <li v-for="(line, i) in s.steps" :key="i" class="mb-1">{{ line }}</li>
                            </ol>
                            <div class="mb-1"><span class="font-weight-medium">Verify: </span>{{ s.verify }}</div>
                            <div v-if="s.notes" class="text-body-2 text-medium-emphasis">
                                <span class="font-weight-medium">Notes: </span>{{ s.notes }}
                            </div>
                            <div class="text-caption text-medium-emphasis mt-1">Added {{ s.added }}</div>
                        </v-card-text>
                    </v-card>
                </v-expansion-panel-text>
            </v-expansion-panel>
        </v-expansion-panels>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { provisioningPhases, type Automation, type ProvisioningPhase, type ProvisioningStep } from '@/data/tenantProvisioning.mjs'

const phases: ProvisioningPhase[] = provisioningPhases
const openPhases = ref<string[]>(phases.map(p => p.id))

const manualSteps = computed<ProvisioningStep[]>(() =>
    phases.flatMap(p => p.steps.filter(s => s.automation === 'manual')))

function countManual(phase: ProvisioningPhase): number {
    return phase.steps.filter(s => s.automation === 'manual').length
}

function automationColor(a: Automation): string {
    switch (a) {
        case 'automatic': return 'success'
        case 'assisted': return 'info'
        default: return 'error'
    }
}

function jumpTo(id: string) {
    const phase = phases.find(p => p.steps.some(s => s.id === id))
    if (phase && !openPhases.value.includes(phase.id)) openPhases.value = [...openPhases.value, phase.id]
    // Let the panel open before scrolling to the card inside it.
    setTimeout(() => document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 150)
}

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

// A plain-text checklist for a ticket or a shared note: one box per step, grouped by phase.
async function copyChecklist() {
    const lines: string[] = []
    for (const p of phases) {
        lines.push(p.title)
        for (const s of p.steps) {
            const tags = [s.owner, s.automation, s.optional ? 'optional' : '', s.stagingOnly ? 'staging only' : ''].filter(Boolean)
            lines.push(`  [ ] ${s.title} (${tags.join(', ')})`)
        }
        lines.push('')
    }
    try {
        await navigator.clipboard.writeText(lines.join('\n'))
        snackbarText.value = 'Checklist copied.'
        snackbarColor.value = 'success'
    } catch {
        snackbarText.value = 'Could not access the clipboard. Select the page text and copy it instead.'
        snackbarColor.value = 'error'
    }
    snackbar.value = true
}
</script>
