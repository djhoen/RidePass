<template>
    <v-container fluid>
        <div class="d-flex align-center mb-2 flex-wrap ga-2">
            <h1 class="text-h5">Automations</h1>
            <v-spacer />
            <v-btn variant="text" prepend-icon="mdi-refresh" :loading="loading" @click="load">Refresh</v-btn>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openNew">New automation</v-btn>
        </div>
        <p class="text-body-2 text-medium-emphasis mb-4">
            Emails that send themselves. Pick something that happens (a rider buys a pass, or a
            ticket to an event), decide when each email goes out, and it keeps running from then on.
        </p>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <v-card variant="outlined" :loading="loading">
            <v-table density="compact">
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Starts when a rider</th>
                        <th>First email</th>
                        <th class="text-center">Status</th>
                        <th class="text-right">Sent</th>
                        <th class="text-right">Skipped</th>
                        <th class="text-right">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-if="!loading && items.length === 0">
                        <td colspan="7" class="text-center text-medium-emphasis py-6">
                            No automations yet. Two common first ones: a welcome email two days after
                            a pass sale, or a "what to bring" email a week before a camp.
                        </td>
                    </tr>
                    <tr v-for="a in items" :key="a.id">
                        <td>
                            <div class="font-weight-medium">{{ a.name }}</div>
                            <div v-if="a.stepCount > 1" class="text-caption text-medium-emphasis">{{ a.stepCount }} emails</div>
                        </td>
                        <td>{{ a.triggerLabel }}</td>
                        <td>{{ a.firstStepLabel ?? 'No emails yet' }}</td>
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
                        <td class="text-right">
                            <v-tooltip text="Riders who bought after an email's send time, or were suppressed">
                                <template #activator="{ props }">
                                    <span v-bind="props">{{ a.skipped }}</span>
                                </template>
                            </v-tooltip>
                        </td>
                        <td class="text-right text-no-wrap">
                            <v-tooltip text="How each email is doing">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" icon="mdi-chart-box-outline" variant="text" size="small"
                                        @click="openReport(a)" />
                                </template>
                            </v-tooltip>
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
                    <v-alert v-if="editingActive" type="info" variant="tonal" density="compact" class="mb-4">
                        This automation is on. Changes apply to emails that have not gone out yet; riders who
                        already received an email will not get it again. Deleting an email drops its history.
                    </v-alert>

                    <v-row>
                        <v-col cols="12" md="7">
                            <v-text-field v-model="form.name" label="Name (only you see this)"
                                density="compact" placeholder="Camp prep series" />

                            <div class="text-subtitle-2 mt-6 mb-2">When it starts</div>
                            <v-select v-model="form.triggerKind" :items="triggerItems" item-title="title" item-value="value"
                                label="Trigger" density="compact" @update:model-value="onTriggerChanged" />

                            <!-- Pass trigger: one product, or any. -->
                            <v-select v-if="form.triggerKind === 'season_pass_purchased'" v-model="form.fromProductId"
                                :items="productOptions" item-title="title" item-value="value"
                                label="Which pass" density="compact" class="mt-4" clearable />

                            <!-- Event trigger: this event, or any event of a type. -->
                            <template v-else-if="form.triggerKind === 'event_ticket_purchased'">
                                <v-radio-group v-model="eventScope" inline density="compact" hide-details class="mt-2">
                                    <v-radio label="This event" value="event" />
                                    <v-radio label="Any event of a type" value="event_type" />
                                </v-radio-group>
                                <v-autocomplete v-if="eventScope === 'event'" v-model="form.eventId" :items="eventItems"
                                    item-title="title" item-value="id" label="Event" density="compact" class="mt-4"
                                    :loading="!options" no-data-text="No events in the last two years" />
                                <v-autocomplete v-else v-model="form.eventTypeId" :items="options?.eventTypes ?? []"
                                    item-title="name" item-value="id" label="Event type" density="compact" class="mt-4"
                                    :loading="!options" />
                            </template>

                            <div class="text-subtitle-2 mt-6 mb-2">Emails</div>
                            <v-card v-for="(s, i) in form.steps" :key="i" variant="outlined" class="pa-3 mb-3">
                                <div class="d-flex align-center mb-2">
                                    <span class="text-subtitle-2">Email {{ i + 1 }}</span>
                                    <span class="text-caption text-medium-emphasis ml-3">{{ stepLabel(s) }}</span>
                                    <v-spacer />
                                    <v-btn v-if="form.steps.length > 1" icon="mdi-delete" variant="text"
                                        size="small" color="error" @click="form.steps.splice(i, 1)" />
                                </div>
                                <!-- Timing as a sentence: [N] days [before/after] [anchor], or [on date]. -->
                                <div class="d-flex flex-wrap align-center ga-2">
                                    <v-select v-model="s.anchor" :items="anchorItems" item-title="title" item-value="value"
                                        label="Send" density="compact" style="max-width: 260px" hide-details
                                        @update:model-value="onAnchorChanged(s)" />
                                    <template v-if="s.anchor !== 'fixed_date'">
                                        <v-text-field v-model.number="s.days" type="number" min="0" max="3650"
                                            label="Days" density="compact" style="max-width: 110px" hide-details />
                                        <v-select v-model="s.direction" :items="directionItems(s)" item-title="title"
                                            item-value="value" density="compact" style="max-width: 130px" hide-details />
                                    </template>
                                    <v-text-field v-else v-model="s.sendOn" type="date" label="On" density="compact"
                                        style="max-width: 190px" hide-details />
                                </div>
                                <div v-if="s.anchor === 'purchase' && s.days === 0" class="text-caption text-medium-emphasis mt-1">
                                    Sends on the next hourly run after {{ anchorPhrase('purchase') }}.
                                </div>
                                <div v-else-if="s.anchor !== 'purchase' && s.anchor !== 'fixed_date'" class="text-caption text-medium-emphasis mt-1">
                                    Riders who buy after this time are skipped, not emailed late.
                                </div>
                                <v-select v-if="templates.length" :items="templates" item-title="name" item-value="id"
                                    label="Insert a saved template" density="compact" class="mt-4" clearable hide-details
                                    :model-value="null" @update:model-value="(id: string | null) => applyTemplateToStep(s, id)" />
                                <v-text-field v-model="s.subject" label="Subject line" density="compact" class="mt-4" />
                                <v-text-field v-model="s.previewText" label="Preview text (optional)" density="compact" class="mt-4"
                                    hint="The snippet inboxes show under the subject line; merge fields work here too" persistent-hint />
                                <div class="d-flex align-center mt-4 mb-1">
                                    <span class="text-caption text-medium-emphasis">Message</span>
                                    <v-spacer />
                                    <v-btn size="small" variant="text" prepend-icon="mdi-cellphone" @click="openPreview(s)">Preview</v-btn>
                                </div>
                                <RichTextEditor v-model="s.bodyHtml" :upload-image="uploadInlineImage" email-buttons />
                            </v-card>
                            <v-btn variant="text" prepend-icon="mdi-plus" @click="addStep">Add another email</v-btn>

                            <div class="text-subtitle-2 mt-6 mb-2">Stop sending when</div>
                            <template v-if="form.triggerKind === 'season_pass_purchased'">
                                <v-checkbox v-model="form.stopOnUpgrade" density="compact" hide-details
                                    label="They take the upgrade" />
                                <v-checkbox v-model="form.stopWhenUsedUp" density="compact" hide-details
                                    label="Their pass expires or is used up" />
                            </template>
                            <template v-else-if="form.triggerKind === 'event_ticket_purchased'">
                                <v-checkbox :model-value="true" disabled density="compact" hide-details
                                    label="Their ticket is refunded or cancelled (always on)" />
                                <v-checkbox :model-value="true" disabled density="compact" hide-details
                                    label="The event is cancelled (always on)" />
                            </template>
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

                            <v-alert v-if="form.triggerKind === 'season_pass_purchased'" type="info" variant="tonal" density="compact">
                                Selling the upgrade this email markets? Set its price on
                                <router-link to="/Admin/PassUpgrades">Pass Upgrades</router-link>.
                                Without an upgrade offer, <code>{{ tokenText('upgrade_price') }}</code> comes out empty.
                            </v-alert>
                            <v-alert v-else-if="form.triggerKind === 'event_ticket_purchased'" type="info" variant="tonal" density="compact">
                                A camp series usually mixes timings: a welcome the day after they buy,
                                "what to bring" a week before the event starts, and a thank-you two
                                days after it ends. Each email is its own step.
                            </v-alert>
                            <v-alert v-else type="info" variant="tonal" density="compact">
                                A welcome series for new subscribers: straight away, then a few days
                                later. Anyone who unsubscribes drops out before the next email.
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
                                    <td>Matching purchases in the last 30 days</td>
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

        <!-- ── Email preview (phone / desktop) ────────────────────────────── -->
        <v-dialog v-model="previewOpen" :max-width="previewMode === 'phone' ? 460 : 760">
            <v-card>
                <v-card-title class="d-flex align-center ga-2">
                    <span>Preview</span>
                    <v-btn-toggle v-model="previewMode" mandatory density="compact" variant="outlined" divided class="ml-2"
                        @update:model-value="loadPreview">
                        <v-btn value="phone" size="small" prepend-icon="mdi-cellphone">Phone</v-btn>
                        <v-btn value="desktop" size="small" prepend-icon="mdi-monitor">Desktop</v-btn>
                    </v-btn-toggle>
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="previewOpen = false" />
                </v-card-title>
                <v-divider />
                <v-card-text class="pa-2">
                    <div class="email-preview-frame" :class="{ phone: previewMode === 'phone' }">
                        <div v-if="previewLoading" class="text-center py-8"><v-progress-circular indeterminate size="24" /></div>
                        <div v-else-if="previewError" class="text-error text-body-2 pa-4">{{ previewError }}</div>
                        <iframe v-else :srcdoc="previewHtml" title="Email preview" sandbox=""></iframe>
                    </div>
                    <div class="text-caption text-medium-emphasis mt-2 px-2">
                        Merge fields are filled with sample values here; a test send uses a real rider.
                    </div>
                </v-card-text>
            </v-card>
        </v-dialog>

        <!-- ── Per-email report ───────────────────────────────────────────── -->
        <v-dialog v-model="reportOpen" max-width="760">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ reportTarget?.name }}</span>
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="reportOpen = false" />
                </v-card-title>
                <v-divider />
                <v-card-text>
                    <v-alert v-if="reportError" type="error" variant="tonal" density="compact" class="mb-4">
                        {{ reportError }}
                    </v-alert>
                    <v-progress-circular v-if="reportLoading" indeterminate size="24" />
                    <template v-else-if="report">
                        <div class="text-body-2 text-medium-emphasis mb-3">
                            {{ report.triggerLabel }}. Counts are lifetime; a rider is counted once per email.
                        </div>
                        <v-table density="compact">
                            <thead>
                                <tr>
                                    <th>Email</th>
                                    <th>When</th>
                                    <th class="text-right">Sent</th>
                                    <th class="text-right">Skipped</th>
                                    <th class="text-right">Failed</th>
                                    <th class="text-right">Opens</th>
                                    <th class="text-right">Clicks</th>
                                    <th>Last sent</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr v-for="s in report.steps" :key="s.id">
                                    <td>
                                        <div class="font-weight-medium">Email {{ s.stepOrder + 1 }}</div>
                                        <div class="text-caption text-medium-emphasis">{{ s.subject }}</div>
                                    </td>
                                    <td>{{ s.label }}</td>
                                    <td class="text-right">{{ s.sent }}</td>
                                    <td class="text-right">
                                        <v-tooltip v-if="s.skipped > 0" location="top">
                                            <template #activator="{ props }">
                                                <span v-bind="props" class="text-decoration-underline">{{ s.skipped }}</span>
                                            </template>
                                            <div v-for="r in s.skipReasons.filter(x => x.status === 'skipped')" :key="r.reason">
                                                {{ r.count }}: {{ r.reason }}
                                            </div>
                                        </v-tooltip>
                                        <span v-else>0</span>
                                    </td>
                                    <td class="text-right">
                                        <v-tooltip v-if="s.failed > 0" location="top">
                                            <template #activator="{ props }">
                                                <span v-bind="props" class="text-error text-decoration-underline">{{ s.failed }}</span>
                                            </template>
                                            <div v-for="r in s.skipReasons.filter(x => x.status === 'failed')" :key="r.reason">
                                                {{ r.count }}: {{ r.reason }}
                                            </div>
                                        </v-tooltip>
                                        <span v-else>0</span>
                                    </td>
                                    <td class="text-right">
                                        <v-tooltip text="Distinct people who opened; includes Apple Mail's automatic opens" location="top">
                                            <template #activator="{ props }"><span v-bind="props">{{ s.uniqueOpens }}</span></template>
                                        </v-tooltip>
                                    </td>
                                    <td class="text-right">{{ s.uniqueClicks }}</td>
                                    <td class="text-no-wrap">{{ s.lastSentAtUtc ? formatWhen(s.lastSentAtUtc) : '-' }}</td>
                                </tr>
                            </tbody>
                        </v-table>
                        <div class="text-caption text-medium-emphasis mt-3">
                            Hover a skipped or failed count for the reasons. "Bought after this email's send
                            time" is by design: late buyers are not emailed late.
                        </div>
                    </template>
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="reportOpen = false">Close</v-btn>
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
                        We'll fill the merge fields from a real purchase and tell you the date this
                        email would have gone out for that rider.
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

        <v-snackbar v-model="toast" :timeout="6000" :color="toastColor" location="top">{{ toastText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import RichTextEditor from '@/components/RichTextEditor.vue'
import { useConfirm } from '@/composables/useConfirm'
import { CampaignService } from '@/services/CampaignService'
import { EmailTemplateService, type EmailTemplateItem } from '@/services/EmailTemplateService'
import { branding } from '@/stores/branding'
import {
    AutomationService,
    type AutomationAnchor,
    type AutomationDetail,
    type AutomationEstimate,
    type AutomationListItem,
    type AutomationTriggerKind,
    type AutomationTriggerOptions,
    type MergeFieldItem,
    type UpsertAutomationRequest,
} from '@/services/AutomationService'

const service = new AutomationService()
const campaignService = new CampaignService()
const templateService = new EmailTemplateService()
const templates = ref<EmailTemplateItem[]>([])
const confirm = useConfirm()

async function applyTemplateToStep(s: StepForm, id: string | null) {
    if (!id) return
    const t = templates.value.find(x => x.id === id)
    if (!t) return
    const hasBody = s.bodyHtml.trim() && s.bodyHtml !== '<p></p>'
    if (hasBody && !await confirm({
        title: 'Replace this email?',
        message: `Replace this email's subject and body with the "${t.name}" template?`,
        confirmText: 'Replace',
    })) return
    if (t.subject) s.subject = t.subject
    if (t.previewText) s.previewText = t.previewText
    s.bodyHtml = t.bodyHtml
}

// Images go to the shared marketing upload; the send path makes them absolute and email-safe.
async function uploadInlineImage(file: File): Promise<string> {
    const resp = await campaignService.uploadImage(file)
    return resp.data.data.imageUrl
}
const tz = () => branding.timezone || 'UTC'

// The editor's step model: the sentence "[days] [before/after] [anchor]" is friendlier to edit
// than a signed offset, so the sign lives in `direction` until save.
interface StepForm {
    id?: string | null
    previewText: string
    anchor: AutomationAnchor
    days: number
    direction: 'before' | 'after'
    sendOn: string
    subject: string
    bodyHtml: string
    bodyText?: string | null
}
interface EditorForm {
    name: string
    triggerKind: AutomationTriggerKind
    fromProductId: string | null
    eventId: string | null
    eventTypeId: string | null
    stopOnUpgrade: boolean
    stopWhenUsedUp: boolean
    sendWindowStart: string | null
    sendWindowEnd: string | null
    steps: StepForm[]
}

const items = ref<AutomationListItem[]>([])
const options = ref<AutomationTriggerOptions | null>(null)
const loading = ref(false)
const loadError = ref('')

const editorOpen = ref(false)
const editingId = ref<string | null>(null)
const editingActive = ref(false)
const eventScope = ref<'event' | 'event_type'>('event')
const useWindow = ref(false)
const saving = ref(false)
const editorError = ref('')
const form = ref<EditorForm>(emptyForm())

const activateOpen = ref(false)
const activateTarget = ref<AutomationListItem | null>(null)
const newPurchasesOnly = ref(true)
const estimate = ref<AutomationEstimate | null>(null)
const estimating = ref(false)
const activating = ref(false)
const activateError = ref('')

const previewOpen = ref(false)
const previewMode = ref<'phone' | 'desktop'>('phone')
const previewStep = ref<StepForm | null>(null)
const previewHtml = ref('')
const previewLoading = ref(false)
const previewError = ref('')

function openPreview(s: StepForm) {
    previewStep.value = s
    previewOpen.value = true
    loadPreview()
}
async function loadPreview() {
    const s = previewStep.value
    if (!s) return
    previewLoading.value = true
    previewError.value = ''
    try {
        const r = await campaignService.preview({ bodyHtml: s.bodyHtml, previewText: s.previewText || null, triggerKind: form.value.triggerKind })
        previewHtml.value = (r.data as any).data.html
    } catch (err: any) {
        previewError.value = err.response?.data?.error || 'Could not build the preview. Close this and try again.'
    } finally {
        previewLoading.value = false
    }
}

const reportOpen = ref(false)
const reportTarget = ref<AutomationListItem | null>(null)
const report = ref<AutomationDetail | null>(null)
const reportLoading = ref(false)
const reportError = ref('')

const testOpen = ref(false)
const testTarget = ref<AutomationListItem | null>(null)
const testEmail = ref('')
const testStepIndex = ref(0)
const testing = ref(false)
const testError = ref('')

const toast = ref(false)
const toastText = ref('')
const toastColor = ref<'success' | 'error'>('success')

const triggerItems = computed(() =>
    (options.value?.triggers ?? []).map(t => ({ title: t.label, value: t.kind })))
const currentTrigger = computed(() =>
    options.value?.triggers.find(t => t.kind === form.value.triggerKind) ?? null)
const mergeFields = computed<MergeFieldItem[]>(() => currentTrigger.value?.mergeFields ?? [])
const anchorItems = computed(() =>
    (currentTrigger.value?.anchors ?? []).map(a => ({
        value: a.value,
        title: a.value === 'fixed_date' ? 'On a date I choose' : `Relative to when ${a.phrase}`,
    })))
const productOptions = computed(() => [
    { title: 'Any pass', value: null as string | null },
    ...(options.value?.passProducts ?? []).map(p => ({
        title: p.isActive ? p.name : `${p.name} (inactive)`,
        value: p.id as string | null,
    })),
])
const eventItems = computed(() => (options.value?.events ?? []).map(e => ({
    id: e.id,
    title: `${e.title} (${dayjs(e.startsAtUtc).tz(tz()).format('MMM D, YYYY')}${e.status === 'cancelled' ? ', cancelled' : ''})`,
})))
const testStepOptions = computed(() =>
    Array.from({ length: testTarget.value?.stepCount ?? 1 },
        (_, i) => ({ title: `Email ${i + 1}`, value: i })))

function emptyForm(): EditorForm {
    return {
        name: '',
        triggerKind: 'season_pass_purchased',
        fromProductId: null,
        eventId: null,
        eventTypeId: null,
        stopOnUpgrade: true,
        stopWhenUsedUp: true,
        sendWindowStart: null,
        sendWindowEnd: null,
        steps: [{ previewText: '', anchor: 'purchase', days: 2, direction: 'after', sendOn: '', subject: '', bodyHtml: '' }],
    }
}

// "Before" only makes sense against something in the future; the purchase is the past.
function directionItems(s: StepForm) {
    return s.anchor === 'purchase'
        ? [{ title: `after ${anchorPhrase('purchase')}`, value: 'after' }]
        : [
            { title: `before ${anchorPhrase(s.anchor)}`, value: 'before' },
            { title: `after ${anchorPhrase(s.anchor)}`, value: 'after' },
        ]
}
function anchorPhrase(anchor: AutomationAnchor) {
    return currentTrigger.value?.anchors.find(a => a.value === anchor)?.phrase ?? anchor
}
function stepLabel(s: StepForm) {
    if (s.anchor === 'fixed_date') return s.sendOn ? `On ${dayjs(s.sendOn).format('MMMM D, YYYY')}` : 'Pick a date'
    if (!s.days) return `Straight away after ${anchorPhrase(s.anchor)}`
    return `${s.days} day${s.days === 1 ? '' : 's'} ${s.direction} ${anchorPhrase(s.anchor)}`
}
function onAnchorChanged(s: StepForm) {
    if (s.anchor === 'purchase') s.direction = 'after'
}
function onTriggerChanged() {
    // Anchors that the new trigger does not offer fall back to "after purchase".
    const allowed = new Set((currentTrigger.value?.anchors ?? []).map(a => a.value))
    for (const s of form.value.steps) {
        if (!allowed.has(s.anchor)) { s.anchor = 'purchase'; s.direction = 'after' }
    }
}

function money(cents: number) { return `$${(cents / 100).toFixed(2)}` }

// Built here rather than inline: a literal "{{" inside a template interpolation is a parse error.
function tokenText(token: string) { return `{${'{'}${token}}${'}'}` }

function flash(text: string, color: 'success' | 'error' = 'success') {
    toastText.value = text
    toastColor.value = color
    toast.value = true
}

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        const [list, opts] = await Promise.all([service.list(), service.triggerOptions()])
        items.value = list.data.data
        options.value = opts.data.data
        // Templates are optional furniture; a failure here must not hide the automations.
        try { templates.value = (await templateService.list()).data.data } catch { templates.value = [] }
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            ?? 'Could not load your automations. Use Refresh to try again.'
    } finally {
        loading.value = false
    }
}

function openNew() {
    editingId.value = null
    editingActive.value = false
    form.value = emptyForm()
    eventScope.value = 'event'
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
        editingActive.value = d.isActive
        form.value = {
            name: d.name,
            triggerKind: d.triggerKind,
            fromProductId: d.fromProductId,
            eventId: d.eventId,
            eventTypeId: d.eventTypeId,
            stopOnUpgrade: d.stopOnUpgrade,
            stopWhenUsedUp: d.stopWhenUsedUp,
            sendWindowStart: d.sendWindowStart,
            sendWindowEnd: d.sendWindowEnd,
            steps: d.steps.map(s => ({
                id: s.id,
                anchor: s.anchor,
                days: Math.abs(s.offsetDays),
                direction: s.offsetDays < 0 ? 'before' : 'after',
                sendOn: s.sendOn ?? '',
                subject: s.subject,
                bodyHtml: s.bodyHtml,
                bodyText: s.bodyText,
                previewText: s.previewText ?? '',
            })),
        }
        eventScope.value = d.eventTypeId ? 'event_type' : 'event'
        useWindow.value = !!d.sendWindowStart
        editorOpen.value = true
    } catch (err: any) {
        flash(err.response?.data?.error
            ?? 'Could not open that automation. Refresh the list and try again.', 'error')
    }
}

function addStep() {
    const last = form.value.steps[form.value.steps.length - 1]
    form.value.steps.push({
        anchor: last?.anchor ?? 'purchase',
        days: last && last.anchor !== 'fixed_date' ? last.days + 7 : 7,
        direction: last?.direction ?? 'after',
        sendOn: '',
        subject: '',
        bodyHtml: '',
        previewText: '',
    })
}

function toPayload(): UpsertAutomationRequest {
    const isEvent = form.value.triggerKind === 'event_ticket_purchased'
    return {
        name: form.value.name,
        triggerKind: form.value.triggerKind,
        fromProductId: isEvent ? null : form.value.fromProductId,
        eventId: isEvent && eventScope.value === 'event' ? form.value.eventId : null,
        eventTypeId: isEvent && eventScope.value === 'event_type' ? form.value.eventTypeId : null,
        stopOnUpgrade: form.value.stopOnUpgrade,
        stopWhenUsedUp: form.value.stopWhenUsedUp,
        sendWindowStart: useWindow.value ? form.value.sendWindowStart : null,
        sendWindowEnd: useWindow.value ? form.value.sendWindowEnd : null,
        steps: form.value.steps.map(s => ({
            id: s.id ?? null,
            anchor: s.anchor,
            offsetDays: s.anchor === 'fixed_date' ? 0 : (s.direction === 'before' ? -1 : 1) * Math.abs(Number(s.days) || 0),
            sendOn: s.anchor === 'fixed_date' ? (s.sendOn || null) : null,
            subject: s.subject,
            bodyHtml: s.bodyHtml,
            bodyText: s.bodyText ?? null,
            previewText: s.previewText?.trim() ? s.previewText.trim() : null,
        })),
    }
}

async function save() {
    editorError.value = ''
    if (!form.value.name.trim()) { editorError.value = 'Give this automation a name.'; return }
    if (form.value.triggerKind === 'event_ticket_purchased') {
        if (eventScope.value === 'event' && !form.value.eventId) { editorError.value = 'Pick the event.'; return }
        if (eventScope.value === 'event_type' && !form.value.eventTypeId) { editorError.value = 'Pick the event type.'; return }
    }
    if (form.value.steps.some(s => !s.subject.trim() || !s.bodyHtml.trim())) {
        editorError.value = 'Every email needs a subject line and a message.'
        return
    }
    if (form.value.steps.some(s => s.anchor === 'fixed_date' && !s.sendOn)) {
        editorError.value = 'Every fixed-date email needs its date.'
        return
    }
    if (useWindow.value && (!form.value.sendWindowStart || !form.value.sendWindowEnd)) {
        editorError.value = 'A send window needs both a start and an end time.'
        return
    }

    saving.value = true
    try {
        const payload = toPayload()
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
        flash('Automation is on. Emails that are already due go out within the hour.')
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

function formatWhen(utc: string) {
    return dayjs(utc).tz(tz()).format('MMM D, YYYY h:mm A')
}

async function openReport(a: AutomationListItem) {
    reportTarget.value = a
    report.value = null
    reportError.value = ''
    reportOpen.value = true
    reportLoading.value = true
    try {
        const { data } = await service.get(a.id)
        report.value = data.data
    } catch (err: any) {
        reportError.value = err.response?.data?.error
            ?? 'Could not load the report for this automation. Close this and try again.'
    } finally {
        reportLoading.value = false
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
        const r = data.data
        testOpen.value = false
        const to = testEmail.value.trim()
        if (!r.usedRealSubject) {
            flash(`Test sent to ${to}. Nothing has sold yet, so it used sample details.`)
        } else if (r.wouldSkip) {
            flash(`Test sent to ${to}, filled in from ${r.sampleName}. For that rider this email would be skipped: they bought after its send time.`)
        } else {
            flash(`Test sent to ${to}, filled in from ${r.sampleName}. For that rider it would send on ${r.wouldSendOn}.`)
        }
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
.email-preview-frame {
    background: #f3f4f6;
    border: 1px solid #e5e7eb;
    border-radius: 6px;
    height: 600px;
    overflow: hidden;
}
.email-preview-frame.phone {
    width: 390px;
    max-width: 100%;
    margin: 0 auto;
}
.email-preview-frame iframe {
    width: 100%;
    height: 100%;
    border: 0;
    background: #f3f4f6;
}
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
