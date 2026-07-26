<template>
    <v-container fluid>
        <div class="d-flex align-center mb-2 flex-wrap ga-2">
            <h1 class="text-h5">Automations</h1>
            <v-spacer />
            <v-btn variant="text" prepend-icon="mdi-refresh" :loading="loading" @click="load">Refresh</v-btn>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openNew">New automation</v-btn>
        </div>
        <p class="text-body-2 text-medium-emphasis mb-4">
            Emails that send themselves. Pick something that happens (a rider buys a pass), wait a
            while, then send. Unlike a campaign, an automation keeps running.
        </p>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <v-card variant="outlined" :loading="loading">
            <v-table density="compact">
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>When it sends</th>
                        <th class="text-center">Status</th>
                        <th class="text-right">Sent</th>
                        <th class="text-right">Upgrades</th>
                        <th class="text-right">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-if="!loading && items.length === 0">
                        <td colspan="6" class="text-center text-medium-emphasis py-6">
                            No automations yet. A common first one: tell pass holders about an
                            upgrade 30 days after they buy.
                        </td>
                    </tr>
                    <tr v-for="a in items" :key="a.id">
                        <td>
                            <div class="font-weight-medium">{{ a.name }}</div>
                            <div class="text-caption text-medium-emphasis">
                                {{ a.fromProductName ?? 'Any pass' }}
                                <span v-if="a.stepCount > 1"> · {{ a.stepCount }} emails</span>
                            </div>
                        </td>
                        <td>{{ scheduleLabel(a) }}</td>
                        <td class="text-center">
                            <v-chip size="small" :color="a.isActive ? 'success' : undefined">
                                {{ a.isActive ? 'On' : 'Off' }}
                            </v-chip>
                        </td>
                        <td class="text-right">
                            {{ a.sent }}
                            <v-tooltip v-if="a.failed > 0" text="Emails that could not be delivered">
                                <template #activator="{ props }">
                                    <span v-bind="props" class="text-error text-caption ml-1">
                                        ({{ a.failed }} failed)
                                    </span>
                                </template>
                            </v-tooltip>
                        </td>
                        <td class="text-right">{{ a.conversions }}</td>
                        <td class="text-right text-no-wrap">
                            <v-tooltip text="Send yourself a test">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" icon="mdi-email-fast-outline" variant="text" size="small"
                                        @click="openTest(a)" />
                                </template>
                            </v-tooltip>
                            <v-tooltip :text="a.isActive ? 'Turn off' : 'Turn on'">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" variant="text" size="small"
                                        :icon="a.isActive ? 'mdi-pause' : 'mdi-play'"
                                        :color="a.isActive ? undefined : 'success'"
                                        @click="a.isActive ? deactivate(a) : openActivate(a)" />
                                </template>
                            </v-tooltip>
                            <v-tooltip text="Edit">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" icon="mdi-pencil" variant="text" size="small"
                                        @click="openEdit(a)" />
                                </template>
                            </v-tooltip>
                            <v-tooltip text="Delete">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" icon="mdi-delete" variant="text" size="small"
                                        color="error" @click="remove(a)" />
                                </template>
                            </v-tooltip>
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <!-- ── Editor ──────────────────────────────────────────────────────── -->
        <v-dialog v-model="editorOpen" fullscreen transition="dialog-bottom-transition">
            <v-card class="d-flex flex-column" style="height: 100%">
                <v-card-title class="d-flex align-center flex-0-0">
                    <span>{{ editingId ? 'Edit automation' : 'New automation' }}</span>
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="editorOpen = false" />
                </v-card-title>
                <v-divider />
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                    <v-alert v-if="editorError" type="error" variant="tonal" density="compact" class="mb-4">
                        {{ editorError }}
                    </v-alert>

                    <v-row>
                        <v-col cols="12" md="7">
                            <v-text-field v-model="form.name" label="Name (only you see this)"
                                density="compact" placeholder="Upgrade nudge" />

                            <div class="text-subtitle-2 mt-6 mb-2">When it starts</div>
                            <v-select v-model="triggerKind" :items="[{ title: 'A rider buys a season pass', value: 'season_pass_purchased' }]"
                                label="Trigger" density="compact" disabled
                                hint="More triggers are coming; for now every automation starts with a pass sale."
                                persistent-hint />
                            <v-select v-model="form.fromProductId" :items="productOptions" item-title="title"
                                item-value="value" label="Which pass" density="compact" class="mt-4" clearable />

                            <div class="text-subtitle-2 mt-6 mb-2">Emails</div>
                            <v-card v-for="(s, i) in form.steps" :key="i" variant="outlined" class="pa-3 mb-3">
                                <div class="d-flex align-center mb-2">
                                    <span class="text-subtitle-2">Email {{ i + 1 }}</span>
                                    <v-spacer />
                                    <v-btn v-if="form.steps.length > 1" icon="mdi-delete" variant="text"
                                        size="small" color="error" @click="form.steps.splice(i, 1)" />
                                </div>
                                <v-text-field v-model.number="s.delayDays" type="number" min="0"
                                    label="Days after they buy" density="compact"
                                    hint="0 sends on the next sweep after purchase." persistent-hint />
                                <v-text-field v-model="s.subject" label="Subject line" density="compact" class="mt-4" />
                                <div class="text-caption text-medium-emphasis mt-4 mb-1">Message</div>
                                <RichTextEditor v-model="s.bodyHtml" />
                            </v-card>
                            <v-btn variant="text" prepend-icon="mdi-plus" @click="addStep">Add another email</v-btn>

                            <div class="text-subtitle-2 mt-6 mb-2">Stop sending when</div>
                            <v-checkbox v-model="form.stopOnUpgrade" density="compact" hide-details
                                label="They take the upgrade" />
                            <v-checkbox v-model="form.stopWhenUsedUp" density="compact" hide-details
                                label="Their pass expires or is used up" />
                            <!-- Not a setting; shown so the tenant can see it is covered. -->
                            <v-checkbox :model-value="true" disabled density="compact" hide-details
                                label="They unsubscribe (always on)" />

                            <div class="text-subtitle-2 mt-6 mb-2">Send window</div>
                            <v-switch v-model="useWindow" color="primary" density="compact" hide-details
                                label="Only send during certain hours" />
                            <div v-if="useWindow" class="d-flex ga-3 mt-3">
                                <v-text-field v-model="form.sendWindowStart" type="time" label="From"
                                    density="compact" style="max-width: 160px" />
                                <v-text-field v-model="form.sendWindowEnd" type="time" label="To"
                                    density="compact" style="max-width: 160px" />
                            </div>
                            <div v-if="useWindow" class="text-caption text-medium-emphasis mt-1">
                                Times are your track's local time. An email that comes due outside
                                the window waits rather than being skipped.
                            </div>
                        </v-col>

                        <v-col cols="12" md="5">
                            <v-card variant="tonal" class="pa-3 mb-4">
                                <div class="text-subtitle-2 mb-2">
                                    <v-icon size="18" class="mr-1">mdi-code-braces</v-icon>
                                    Merge fields
                                </div>
                                <div class="text-caption text-medium-emphasis mb-3">
                                    Paste these into the subject or the message and they're replaced
                                    with each rider's own details. A misspelled one comes out blank.
                                </div>
                                <div v-for="f in mergeFields" :key="f.token" class="merge-row">
                                    <code class="merge-token">{{ tokenText(f.token) }}</code>
                                    <span class="text-caption text-medium-emphasis">{{ f.description }}</span>
                                </div>
                            </v-card>

                            <v-alert type="info" variant="tonal" density="compact">
                                Selling the upgrade this email markets? Set its price on
                                <router-link to="/Admin/PassUpgrades">Pass Upgrades</router-link>.
                                Without an upgrade offer, <code>{{ tokenText('upgrade_price') }}</code> comes out empty.
                            </v-alert>
                        </v-col>
                    </v-row>
                </v-card-text>
                <v-divider />
                <v-card-actions class="flex-0-0">
                    <v-spacer />
                    <v-btn variant="text" @click="editorOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="save">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- ── Turn on, with the bill first ───────────────────────────────── -->
        <v-dialog v-model="activateOpen" max-width="560">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Turn on "{{ activateTarget?.name }}"</span>
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="activateOpen = false" />
                </v-card-title>
                <v-divider />
                <v-card-text>
                    <v-alert v-if="activateError" type="error" variant="tonal" density="compact" class="mb-4">
                        {{ activateError }}
                    </v-alert>

                    <!-- The sharp edge: a "30 days after purchase" automation at a track with two
                         seasons of history matches every holder who ever bought. -->
                    <v-switch v-model="newPurchasesOnly" color="primary" density="compact" hide-details
                        label="Only riders who buy from now on" @update:model-value="loadEstimate" />
                    <div class="text-caption text-medium-emphasis mb-4">
                        Turn this off to also email everyone who already qualifies.
                    </div>

                    <v-progress-circular v-if="estimating" indeterminate size="24" />
                    <template v-else-if="estimate">
                        <v-table density="compact">
                            <tbody>
                                <tr>
                                    <td>Goes out on the first run</td>
                                    <td class="text-right">{{ estimate.backlogCount }} emails</td>
                                    <td class="text-right">{{ money(estimate.backlogChargeCents) }}</td>
                                </tr>
                                <tr>
                                    <td>Passes sold in the last 30 days</td>
                                    <td class="text-right">{{ estimate.last30DayRate }} emails</td>
                                    <td class="text-right">{{ money(estimate.ongoingChargeCents) }}/mo</td>
                                </tr>
                            </tbody>
                        </v-table>
                        <div class="text-caption text-medium-emphasis mt-2">
                            Email costs come off your payout. The monthly figure is a forecast based
                            on recent sales, not a fixed charge.
                        </div>
                    </template>
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="activateOpen = false">Cancel</v-btn>
                    <v-btn color="success" :loading="activating" @click="confirmActivate">Turn on</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- ── Test send ──────────────────────────────────────────────────── -->
        <v-dialog v-model="testOpen" max-width="480">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Send yourself a test</span>
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="testOpen = false" />
                </v-card-title>
                <v-divider />
                <v-card-text>
                    <v-alert v-if="testError" type="error" variant="tonal" density="compact" class="mb-4">
                        {{ testError }}
                    </v-alert>
                    <p class="text-body-2 mb-4">
                        We'll fill the merge fields from a real pass so you can see what a rider
                        actually gets.
                    </p>
                    <v-text-field v-model="testEmail" type="email" label="Send to" density="compact" />
                    <v-select v-if="(testTarget?.stepCount ?? 0) > 1" v-model="testStepIndex"
                        :items="testStepOptions" label="Which email" density="compact" class="mt-4" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="testOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="testing" @click="sendTest">Send test</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="toast" :timeout="5000" :color="toastColor" location="top">{{ toastText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import RichTextEditor from '@/components/RichTextEditor.vue'
import { useConfirm } from '@/composables/useConfirm'
import {
    AutomationService,
    type AutomationListItem,
    type AutomationEstimate,
    type MergeFieldItem,
    type UpsertAutomationRequest,
} from '@/services/AutomationService'

const service = new AutomationService()
const confirm = useConfirm()

const items = ref<AutomationListItem[]>([])
const productOptions = ref<{ title: string; value: string | null }[]>([])
const mergeFields = ref<MergeFieldItem[]>([])
const loading = ref(false)
const loadError = ref('')

const editorOpen = ref(false)
const editingId = ref<string | null>(null)
const triggerKind = ref('season_pass_purchased')
const useWindow = ref(false)
const saving = ref(false)
const editorError = ref('')
const form = ref<UpsertAutomationRequest>(emptyForm())

const activateOpen = ref(false)
const activateTarget = ref<AutomationListItem | null>(null)
const newPurchasesOnly = ref(true)
const estimate = ref<AutomationEstimate | null>(null)
const estimating = ref(false)
const activating = ref(false)
const activateError = ref('')

const testOpen = ref(false)
const testTarget = ref<AutomationListItem | null>(null)
const testEmail = ref('')
const testStepIndex = ref(0)
const testing = ref(false)
const testError = ref('')

const toast = ref(false)
const toastText = ref('')
const toastColor = ref<'success' | 'error'>('success')

const testStepOptions = computed(() =>
    Array.from({ length: testTarget.value?.stepCount ?? 1 },
        (_, i) => ({ title: `Email ${i + 1}`, value: i })))

function emptyForm(): UpsertAutomationRequest {
    return {
        name: '',
        fromProductId: null,
        stopOnUpgrade: true,
        stopWhenUsedUp: true,
        sendWindowStart: null,
        sendWindowEnd: null,
        steps: [{ delayDays: 30, subject: '', bodyHtml: '' }],
    }
}

function money(cents: number) { return `$${(cents / 100).toFixed(2)}` }

// Built here rather than inline: a literal "{{" inside a template interpolation is a parse error.
function tokenText(token: string) { return `{${'{'}${token}}${'}'}` }

function scheduleLabel(a: AutomationListItem) {
    if (a.firstDelayDays === null) return 'No emails yet'
    const d = a.firstDelayDays
    const when = d === 0 ? 'Straight away' : `${d} day${d === 1 ? '' : 's'} after`
    return `${when} they buy${a.stepCount > 1 ? `, then ${a.stepCount - 1} more` : ''}`
}

function flash(text: string, color: 'success' | 'error' = 'success') {
    toastText.value = text
    toastColor.value = color
    toast.value = true
}

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        const [list, products, fields] = await Promise.all([
            service.list(),
            service.products(),
            service.mergeFields(),
        ])
        items.value = list.data.data
        productOptions.value = [
            { title: 'Any pass', value: null },
            ...products.data.data.map(p => ({
                title: p.isActive ? p.name : `${p.name} (inactive)`,
                value: p.id as string | null,
            })),
        ]
        mergeFields.value = fields.data.data
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            ?? 'Could not load your automations. Use Refresh to try again.'
    } finally {
        loading.value = false
    }
}

function openNew() {
    editingId.value = null
    form.value = emptyForm()
    useWindow.value = false
    editorError.value = ''
    editorOpen.value = true
}

async function openEdit(a: AutomationListItem) {
    editorError.value = ''
    try {
        const { data } = await service.get(a.id)
        const d = data.data
        editingId.value = d.id
        form.value = {
            name: d.name,
            fromProductId: d.fromProductId,
            stopOnUpgrade: d.stopOnUpgrade,
            stopWhenUsedUp: d.stopWhenUsedUp,
            sendWindowStart: d.sendWindowStart,
            sendWindowEnd: d.sendWindowEnd,
            steps: d.steps.map(s => ({
                delayDays: s.delayDays,
                subject: s.subject,
                bodyHtml: s.bodyHtml,
                bodyText: s.bodyText,
            })),
        }
        useWindow.value = !!d.sendWindowStart
        editorOpen.value = true
    } catch (err: any) {
        flash(err.response?.data?.error
            ?? 'Could not open that automation. Refresh the list and try again.', 'error')
    }
}

function addStep() {
    const last = form.value.steps[form.value.steps.length - 1]
    form.value.steps.push({ delayDays: (last?.delayDays ?? 0) + 7, subject: '', bodyHtml: '' })
}

async function save() {
    editorError.value = ''
    if (!form.value.name.trim()) { editorError.value = 'Give this automation a name.'; return }
    if (form.value.steps.some(s => !s.subject.trim() || !s.bodyHtml.trim())) {
        editorError.value = 'Every email needs a subject line and a message.'
        return
    }
    if (useWindow.value && (!form.value.sendWindowStart || !form.value.sendWindowEnd)) {
        editorError.value = 'A send window needs both a start and an end time.'
        return
    }

    saving.value = true
    try {
        const payload: UpsertAutomationRequest = {
            ...form.value,
            sendWindowStart: useWindow.value ? form.value.sendWindowStart : null,
            sendWindowEnd: useWindow.value ? form.value.sendWindowEnd : null,
        }
        if (editingId.value) await service.update(editingId.value, payload)
        else await service.create(payload)
        editorOpen.value = false
        flash('Automation saved. Turn it on when you\'re ready.')
        await load()
    } catch (err: any) {
        editorError.value = err.response?.data?.error
            ?? 'Could not save this automation. Nothing was changed; check the fields and try again.'
    } finally {
        saving.value = false
    }
}

async function remove(a: AutomationListItem) {
    const extra = a.sent > 0 ? ` Its record of ${a.sent} sent email${a.sent === 1 ? '' : 's'} goes too.` : ''
    if (!await confirm({
        message: `Delete "${a.name}"?${extra}`,
        confirmText: 'Delete',
        confirmColor: 'error',
    })) return
    try {
        await service.remove(a.id)
        flash('Automation deleted.')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error
            ?? 'Could not delete that automation. It is still there; try again.', 'error')
    }
}

function openActivate(a: AutomationListItem) {
    activateTarget.value = a
    newPurchasesOnly.value = true
    estimate.value = null
    activateError.value = ''
    activateOpen.value = true
    loadEstimate()
}

async function loadEstimate() {
    if (!activateTarget.value) return
    estimating.value = true
    activateError.value = ''
    try {
        const { data } = await service.estimate(activateTarget.value.id, newPurchasesOnly.value)
        estimate.value = data.data
    } catch (err: any) {
        estimate.value = null
        activateError.value = err.response?.data?.error
            ?? 'Could not work out how many riders this would email. Nothing has been turned on.'
    } finally {
        estimating.value = false
    }
}

async function confirmActivate() {
    if (!activateTarget.value) return
    activating.value = true
    activateError.value = ''
    try {
        await service.activate(activateTarget.value.id, true, newPurchasesOnly.value)
        activateOpen.value = false
        flash('Automation is on. The first emails go out within the hour.')
        await load()
    } catch (err: any) {
        activateError.value = err.response?.data?.error
            ?? 'Could not turn this automation on. Nothing was changed; try again.'
    } finally {
        activating.value = false
    }
}

async function deactivate(a: AutomationListItem) {
    if (!await confirm({
        message: `Turn off "${a.name}"? Riders already emailed won't be emailed again if you turn it back on.`,
        confirmText: 'Turn off',
    })) return
    try {
        await service.activate(a.id, false, false)
        flash('Automation turned off.')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error
            ?? 'Could not turn this automation off. It is still running; try again.', 'error')
    }
}

function openTest(a: AutomationListItem) {
    testTarget.value = a
    testStepIndex.value = 0
    testError.value = ''
    testOpen.value = true
}

async function sendTest() {
    if (!testTarget.value) return
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(testEmail.value.trim())) {
        testError.value = 'Enter a valid email address.'
        return
    }
    testing.value = true
    testError.value = ''
    try {
        const { data } = await service.testSend(
            testTarget.value.id, testStepIndex.value, testEmail.value.trim())
        testOpen.value = false
        flash(data.data.usedRealPass
            ? `Test sent to ${testEmail.value.trim()}, filled in from a real ${data.data.sampleProduct} holder.`
            : `Test sent to ${testEmail.value.trim()}. No passes have sold yet, so it used sample details.`)
    } catch (err: any) {
        testError.value = err.response?.data?.error
            ?? 'Could not send the test email. Check the address and try again.'
    } finally {
        testing.value = false
    }
}

onMounted(load)
</script>

<style scoped>
.merge-row {
    display: flex;
    align-items: baseline;
    gap: 8px;
    padding: 3px 0;
}
.merge-token {
    font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
    font-size: 0.8rem;
    white-space: nowrap;
}
</style>
