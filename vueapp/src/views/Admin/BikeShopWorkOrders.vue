<template>
    <v-container>
        <div class="d-flex align-center mb-4 ga-3 flex-wrap">
            <h1 class="text-h4">Work Orders</h1>
            <v-spacer></v-spacer>
            <v-switch v-model="includeClosed" label="Include closed" color="primary" hide-details density="compact"
                @update:model-value="reload"></v-switch>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openNew">New work order</v-btn>
        </div>

        <!-- Job templates live here rather than in settings: they only exist to be dropped onto a
             work order, so they belong beside the work orders. -->
        <v-tabs v-model="tab" :height="40" class="mb-4 sub-tabs"
            hide-slider selected-class="sub-tab-active">
            <v-tab value="orders" class="sub-tab">Work orders</v-tab>
            <v-tab value="templates" class="sub-tab">Saved jobs</v-tab>
        </v-tabs>

        <div v-if="tab === 'templates'">
            <JobTemplatesTab />
        </div>

        <template v-else>
        <div class="d-flex mb-3 ga-2 flex-wrap align-center">
            <v-text-field v-model="search" density="compact" hide-details clearable
                prepend-inner-icon="mdi-magnify" label="Customer, phone or bike"
                style="max-width: 280px"></v-text-field>
            <!-- Empty = everything still open. Picked-up and cancelled are finished business and
                 would bury the handful of jobs anyone acts on. -->
            <v-select v-model="statusFilter" :items="statusFilterItems" item-title="title"
                item-value="value" density="compact" hide-details clearable chips closable-chips
                multiple label="Status" placeholder="Open jobs" persistent-placeholder
                style="min-width: 260px; max-width: 400px"></v-select>
            <v-select v-model="techFilter" :items="techFilterItems" item-title="title"
                item-value="value" density="compact" hide-details clearable
                label="Technician" style="max-width: 220px"></v-select>
            <v-btn-toggle v-model="dueFilter" density="compact" variant="outlined" divided>
                <v-btn value="all" size="small">All</v-btn>
                <v-btn value="overdue" size="small">Overdue</v-btn>
                <v-btn value="unpromised" size="small">No date</v-btn>
            </v-btn-toggle>
        </div>

        <v-card v-if="loading" class="pa-6 text-center"><v-progress-circular indeterminate color="primary" /></v-card>
        <v-alert v-else-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>
        <v-card v-else-if="orders.length === 0" class="pa-6 text-center text-medium-emphasis">
            No open work orders. Take in a repair to get started.
        </v-card>
        <v-card v-else-if="visibleOrders.length === 0" class="pa-6 text-center text-medium-emphasis">
            No work orders match those filters.
            <div v-if="statusFilter.length === 0" class="text-caption mt-1">
                Picked-up and cancelled jobs are hidden by default — pick a status to include them.
            </div>
        </v-card>
        <v-table v-else density="compact">
            <thead>
                <tr>
                    <th v-for="c in columns" :key="c.key"
                        :class="[c.align === 'right' ? 'text-right' : '', 'sortable-col']"
                        :style="c.width ? `width: ${c.width}` : ''"
                        @click="toggleSort(c.key)">
                        {{ c.label }}
                        <v-icon v-if="sortKey === c.key" size="14"
                            :icon="sortAsc ? 'mdi-arrow-up' : 'mdi-arrow-down'"></v-icon>
                    </th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
                <tr v-for="o in visibleOrders" :key="o.id">
                    <td>
                        {{ o.customerName }}
                        <v-tooltip v-if="o.groupId" text="Part of a multi-bike visit" location="top">
                            <template #activator="{ props }">
                                <v-icon v-bind="props" size="13" class="text-medium-emphasis ml-1">mdi-account-group</v-icon>
                            </template>
                        </v-tooltip>
                        <div class="text-caption text-medium-emphasis">{{ o.customerPhone }}</div>
                    </td>
                    <td class="text-caption">{{ o.customerBikeDesc || '(shop unit)' }}</td>
                    <td>
                        <v-chip size="x-small" :color="statusColor(o.status)">{{ statusLabel(o.status) }}</v-chip>
                        <!-- Overdue jobs get a red flag next to the (config-colored) status so staff
                             can spot them at a glance during a busy counter session. -->
                        <v-chip v-if="overdue(o)" size="x-small" color="error" variant="flat" class="ml-1">Overdue</v-chip>
                        <v-tooltip v-if="o.checkedByUserId" text="QC checked" location="top">
                            <template #activator="{ props }">
                                <v-icon v-bind="props" size="14" color="success" class="ml-1"
                                    icon="mdi-check-decagram"></v-icon>
                            </template>
                        </v-tooltip>
                    </td>
                    <!-- Age is how a bike quietly sits for three weeks: an open job with no
                         promised date has nothing else drawing the eye to it. -->
                    <td>
                        <span class="text-caption" :class="ageClass(o)">{{ ageLabel(o) }}</span>
                    </td>
                    <td class="text-caption" :class="overdue(o) ? 'text-error font-weight-medium' : ''">
                        {{ o.promisedAt ? formatDate(o.promisedAt) : '—' }}
                    </td>
                    <td class="text-right">{{ money(orderTotal(o)) }}</td>
                    <td class="text-right"><v-btn size="x-small" variant="text" icon="mdi-pencil" @click="openEdit(o)"></v-btn></td>
                </tr>
            </tbody>
        </v-table>
        </template>

        <!-- ── Editor dialog ───────────────────────────────────────────── -->
        <v-dialog v-model="editorOpen" max-width="720" persistent>
            <v-card class="d-flex flex-column" style="max-height: 90vh">
                <v-card-title class="d-flex align-center" style="flex: 0 0 auto">
                    <span>{{ editing ? 'Work order' : 'New work order' }}</span>
                    <v-chip v-if="editing" size="small" class="ml-2" :color="statusColor(form.status)">{{ statusLabel(form.status) }}</v-chip>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="editorOpen = false"></v-btn>
                </v-card-title>
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                    <div class="wo-group-label mb-1">Customer</div>
                    <v-text-field v-model="form.customerName" label="Name" density="compact" hide-details class="mt-2"></v-text-field>
                    <v-row dense class="mt-2">
                        <v-col cols="6"><v-text-field v-model="form.customerPhone" label="Phone" density="compact" hide-details></v-text-field></v-col>
                        <v-col cols="6"><v-text-field v-model="form.customerEmail" type="email" label="Email" density="compact" hide-details></v-text-field></v-col>
                    </v-row>
                    <!-- Serial first: it resolves the bike to a record that carries its repair
                         history, instead of retyping a description every visit. -->
                    <div class="d-flex ga-2 align-start mt-4">
                        <v-text-field v-model="bikeSerial" label="Bike serial (optional)"
                            density="compact" hide-details clearable style="max-width: 260px"
                            @keyup.enter="lookupBike"></v-text-field>
                        <v-btn variant="tonal" :loading="bikeLookingUp" :disabled="!bikeSerial?.trim()"
                            @click="lookupBike">Look up</v-btn>
                    </div>

                    <v-alert v-if="bikeMatch === 'known_bike'" type="success" variant="tonal"
                        density="compact" class="mt-2">
                        <strong>We've seen this bike before.</strong>
                        {{ linkedBikeName }} — {{ bikeHistory.length }} previous
                        job{{ bikeHistory.length === 1 ? '' : 's' }}.
                    </v-alert>
                    <v-alert v-else-if="bikeMatch === 'sold_by_us'" type="info" variant="tonal"
                        density="compact" class="mt-2">
                        <strong>We sold this bike.</strong> Details filled in from the sale — check them
                        and they'll be saved with the job.
                    </v-alert>
                    <v-alert v-else-if="bikeMatch === 'unknown'" type="info" variant="tonal"
                        density="compact" class="mt-2">
                        New bike. Fill in what you can; it'll be remembered for next time.
                    </v-alert>

                    <!-- Structured fields appear once a serial has been resolved; without one this
                         stays a plain description so quick intake isn't slowed down. -->
                    <v-btn v-if="!showBikeDetails" size="small" variant="text" class="mt-1"
                        prepend-icon="mdi-bike" @click="showBikeDetails = true">
                        Add bike details {{ linkedBikeId ? '' : '(needed for inspections)' }}
                    </v-btn>

                    <v-row v-if="showBikeDetails" dense class="mt-1">
                        <v-col cols="4"><v-text-field v-model="bikeForm.brand" label="Brand" density="compact" hide-details></v-text-field></v-col>
                        <v-col cols="5"><v-text-field v-model="bikeForm.model" label="Model" density="compact" hide-details></v-text-field></v-col>
                        <v-col cols="3"><v-text-field v-model.number="bikeForm.modelYear" type="number" label="Year" density="compact" hide-details></v-text-field></v-col>
                        <v-col cols="6"><v-text-field v-model="bikeForm.color" label="Color" density="compact" hide-details></v-text-field></v-col>
                        <v-col cols="6"><v-text-field v-model="bikeForm.size" label="Size" density="compact" hide-details></v-text-field></v-col>
                    </v-row>

                    <div v-if="bikeHistory.length > 0" class="mt-3">
                        <div class="text-caption text-medium-emphasis mb-1">Previous work on this bike</div>
                        <v-table density="compact">
                            <tbody>
                                <tr v-for="h in bikeHistory" :key="h.workOrderId">
                                    <td class="text-caption">{{ formatTsDate(h.createdAt) }}</td>
                                    <td><v-chip size="x-small" :color="statusColor(h.status)">{{ statusLabel(h.status) }}</v-chip></td>
                                    <td class="text-caption">{{ h.intakeNotes || '—' }}</td>
                                    <td class="text-caption text-right">{{ money(h.totalCents) }}</td>
                                </tr>
                            </tbody>
                        </v-table>
                    </div>

                    <!-- Inspections hang off the BIKE, so they need one linked. Saving the job
                         creates the bike record, which is why this asks you to save first. -->
                    <div v-if="editing && !linkedBikeId" class="mt-4">
                        <div class="text-subtitle-2 mb-1">Inspections</div>
                        <v-alert type="info" variant="tonal" density="compact">
                            Add this bike's details and save, then you can run a multi-point
                            inspection on it. Inspections attach to the bike, so its grading history
                            follows it across visits.
                        </v-alert>
                    </div>

                    <div v-if="linkedBikeId" class="mt-4">
                        <div class="d-flex align-center ga-2 mb-1">
                            <span class="text-subtitle-2">Inspections</span>
                            <v-spacer></v-spacer>
                            <v-btn size="small" variant="tonal" prepend-icon="mdi-clipboard-check-outline"
                                :loading="startingInspection" @click="startInspection">New inspection</v-btn>
                        </div>
                        <div v-if="bikeInspections.length === 0" class="text-caption text-medium-emphasis">
                            None yet for this bike.
                        </div>
                        <v-table v-else density="compact">
                            <tbody>
                                <tr v-for="ins in bikeInspections" :key="ins.id" class="insp-link"
                                    @click="openInspection(ins.id)">
                                    <td class="text-caption">{{ formatTsDate(ins.performedAt) }}</td>
                                    <td>
                                        <v-chip size="x-small" :color="ins.status === 'complete' ? 'success' : 'grey'">
                                            {{ ins.status === 'complete' ? 'Complete' : 'Draft' }}
                                        </v-chip>
                                    </td>
                                    <td class="text-caption">
                                        <span v-if="ins.attentionCount" class="text-error">
                                            {{ ins.attentionCount }} needs work
                                        </span>
                                        <span v-else-if="ins.monitorCount" class="text-warning">
                                            {{ ins.monitorCount }} monitor
                                        </span>
                                        <span v-else class="text-medium-emphasis">all good</span>
                                    </td>
                                    <td class="text-right"><v-icon size="16">mdi-chevron-right</v-icon></td>
                                </tr>
                            </tbody>
                        </v-table>
                    </div>

                    <v-text-field v-model="form.customerBikeDesc"
                        :label="bikeMatch ? 'Extra bike notes (optional)' : 'Bike (make, model, color)'"
                        density="compact" class="mt-4" hide-details></v-text-field>
                    <v-textarea v-model="form.intakeNotes" label="Intake notes / symptoms" rows="2" density="compact" class="mt-4" hide-details></v-textarea>
                    <v-textarea v-model="form.customerNotes" rows="2" density="compact" class="mt-4"
                        label="Customer notes (shown on the receipt)" persistent-hint
                        prepend-inner-icon="mdi-account-eye-outline"
                        hint="Prints on the claim tag and the bill. Keep bench chatter in the internal notes below."></v-textarea>
                    <v-row dense class="mt-2">
                        <v-col cols="4">
                            <v-select v-model="form.status" :items="statusItems" item-title="title" item-value="value"
                                label="Status" density="compact" hide-details></v-select>
                        </v-col>
                        <v-col cols="4">
                            <v-text-field v-model="form.promisedAt" type="date" label="Promised by" density="compact" hide-details></v-text-field>
                        </v-col>
                        <v-col cols="4">
                            <v-select v-model="form.assignedTechUserId" :items="technicians" item-title="name" item-value="id"
                                label="Technician" density="compact" hide-details clearable></v-select>
                        </v-col>
                    </v-row>
                    <p v-if="form.status === 'estimate'" class="text-caption text-medium-emphasis mt-1">
                        Estimate: parts are quoted but NOT taken from stock until the customer accepts
                        (switch to In take / In progress).
                    </p>

                    <template v-if="editing">
                        <v-divider class="my-4"></v-divider>
                        <!-- Customer visit: sibling tickets for other bikes the same customer dropped
                             off together. Each stays its own ticket; this just links and pre-fills. -->
                        <div class="d-flex align-center ga-2 mb-3 flex-wrap">
                            <v-icon size="18" class="text-medium-emphasis">mdi-account-group-outline</v-icon>
                            <span class="text-subtitle-2">Customer visit</span>
                            <template v-if="editing.groupMembers && editing.groupMembers.length">
                                <span class="text-caption text-medium-emphasis">also in for</span>
                                <v-chip v-for="m in editing.groupMembers" :key="m.id" size="small" variant="tonal"
                                    :color="statusColor(m.status)" style="cursor: pointer"
                                    @click="openSibling(m.id)">
                                    {{ m.bikeLabel }} · {{ statusLabel(m.status) }}
                                </v-chip>
                            </template>
                            <span v-else class="text-caption text-medium-emphasis">Just this bike.</span>
                            <v-spacer></v-spacer>
                            <v-btn size="small" variant="tonal" prepend-icon="mdi-plus"
                                :loading="addingBike" @click="addAnotherBike">Add another bike</v-btn>
                        </div>

                        <v-divider class="my-4"></v-divider>
                        <!-- QC sign-off: a second reviewer attests the finished job before pickup. -->
                        <div class="d-flex align-center ga-3 flex-wrap mb-4">
                            <v-icon size="18" :color="editing.checkedByUserId ? 'success' : undefined"
                                :icon="editing.checkedByUserId ? 'mdi-check-decagram' : 'mdi-check-decagram-outline'"></v-icon>
                            <v-select :model-value="editing.checkedByUserId" :items="technicians"
                                item-title="name" item-value="id" label="Checked by (QC)" density="compact"
                                hide-details clearable :loading="checkingQc" :disabled="checkingQc"
                                style="min-width: 240px; max-width: 300px"
                                @update:model-value="setQc"></v-select>
                            <span v-if="editing.checkedAt" class="text-caption text-medium-emphasis">
                                checked {{ noteTime(editing.checkedAt) }}
                            </span>
                            <span v-else class="text-caption text-medium-emphasis">Not yet checked.</span>
                        </div>

                        <v-divider class="my-4"></v-divider>
                        <!-- Internal notes thread: staff-only, timestamped, append-only. Never
                             printed for the customer (that's what "Customer notes" above is for). -->
                        <div class="d-flex align-center ga-2 mb-1">
                            <v-icon size="16" class="text-medium-emphasis">mdi-lock-outline</v-icon>
                            <span class="text-subtitle-2">Internal notes</span>
                            <span class="text-caption text-medium-emphasis">staff only</span>
                        </div>
                        <div class="d-flex align-start ga-2">
                            <v-textarea v-model="newNote" rows="1" auto-grow density="compact" hide-details
                                placeholder="Add an internal note (what you found, what you did)"
                                @keyup.ctrl.enter="addNote"></v-textarea>
                            <v-btn color="primary" variant="tonal" :loading="addingNote"
                                :disabled="!newNote.trim()" @click="addNote">Add</v-btn>
                        </div>
                        <div v-if="(editing.notes?.length ?? 0) === 0" class="text-caption text-medium-emphasis mt-2">
                            No internal notes yet.
                        </div>
                        <div v-for="n in editing.notes" :key="n.id" class="mt-2">
                            <div class="text-body-2" style="white-space: pre-wrap">{{ n.body }}</div>
                            <div class="text-caption text-medium-emphasis">
                                {{ n.createdByName || 'Staff' }} · {{ noteTime(n.createdAt) }}
                            </div>
                        </div>

                        <v-divider class="my-4"></v-divider>
                        <!-- Photograph what came in. Cheapest protection against "that scratch
                             was already there" once the bike leaves. -->
                        <ConditionPhotos :work-order-id="editing.id" stage="intake"
                            title="Intake photos"
                            hint="Photograph the bike as it arrived, especially any existing damage." />
                        <PhotoQrPanel kind="work-order" :id="editing.id" />

                        <v-divider class="my-4"></v-divider>
                        <div class="text-subtitle-2 mb-1">Repair authorization</div>
                        <p class="text-caption text-medium-emphasis mb-2">
                            Have the customer sign on this device before work starts.
                        </p>
                        <SignAgreementDialog kind="work_order_terms" :work-order-id="editing.id"
                            :default-signer-name="editing.customerName"
                            :default-signer-email="editing.customerEmail" />

                        <v-divider class="my-4"></v-divider>
                        <div class="d-flex align-center ga-2 mb-2 flex-wrap">
                            <div class="wo-group-label">Labor &amp; parts</div>
                            <!-- Per-line approve/decline: bulk-approve the pending ones in one click. -->
                            <v-btn v-if="!closed && hasPendingLines(editing)" size="x-small" variant="text"
                                color="success" prepend-icon="mdi-check-all" @click="approveAllLines">
                                Approve all
                            </v-btn>
                            <v-spacer></v-spacer>
                            <!-- Drop a whole saved job on rather than retyping the same lines. -->
                            <v-menu v-if="!closed && activeTemplates.length">
                                <template #activator="{ props: menuProps }">
                                    <v-btn v-bind="menuProps" size="small" variant="tonal"
                                        prepend-icon="mdi-clipboard-list-outline" :loading="applyingTemplate">
                                        Add saved job
                                    </v-btn>
                                </template>
                                <v-list density="compact">
                                    <v-list-item v-for="t in activeTemplates" :key="t.id"
                                        :title="t.name" :subtitle="t.fitsNote || undefined"
                                        @click="applyTemplate(t)"></v-list-item>
                                </v-list>
                            </v-menu>
                        </div>
                        <div v-for="l in editing.lines" :key="l.id" class="d-flex align-center ga-2 py-1"
                            :class="{ 'line-declined': l.approvalStatus === 'declined' }">
                            <v-icon size="16" :icon="l.lineKind === 'labor' ? 'mdi-account-wrench' : 'mdi-cog'" class="text-medium-emphasis"></v-icon>
                            <div class="flex-grow-1 text-body-2">
                                {{ lineName(l) }}<span v-if="l.quantity > 1" class="text-medium-emphasis"> × {{ l.quantity }}</span>
                                <span v-if="l.laborHours && l.laborRateCents" class="text-caption text-medium-emphasis ml-1">
                                    ({{ l.laborHours }} hr @ ${{ (l.laborRateCents / 100).toFixed(0) }}/hr)
                                </span>
                                <span v-if="l.estimatedMinutes" class="text-caption text-medium-emphasis ml-1">· est {{ l.estimatedMinutes }}m</span>
                                <v-chip v-if="l.approvalStatus === 'approved'" size="x-small" class="ml-1" color="success" variant="tonal">approved</v-chip>
                                <v-chip v-else-if="l.approvalStatus === 'declined'" size="x-small" class="ml-1" color="error" variant="tonal">declined</v-chip>
                                <v-chip v-if="l.lineKind === 'part' && l.poLineId && !l.arrivedAt" size="x-small" class="ml-1"
                                    color="warning" variant="tonal">on order</v-chip>
                                <v-chip v-else-if="l.lineKind === 'part' && l.arrivedAt" size="x-small" class="ml-1"
                                    color="success" variant="tonal">arrived</v-chip>
                                <v-chip v-else-if="l.lineKind === 'part' && !l.consumed" size="x-small" class="ml-1" variant="tonal">quoted</v-chip>
                            </div>
                            <span class="text-body-2">{{ money(l.unitPriceCents * l.quantity) }}</span>
                            <!-- Approve / decline this line. The lit button shows the current decision. -->
                            <template v-if="!closed">
                                <v-tooltip :text="l.approvalStatus === 'approved' ? 'Approved (click to clear)' : 'Approve line'" location="top">
                                    <template #activator="{ props }">
                                        <v-btn v-bind="props" icon="mdi-check" size="x-small" variant="text"
                                            :color="l.approvalStatus === 'approved' ? 'success' : undefined"
                                            @click="setLineApproval(l.id, l.approvalStatus === 'approved' ? 'pending' : 'approved')"></v-btn>
                                    </template>
                                </v-tooltip>
                                <v-tooltip :text="l.approvalStatus === 'declined' ? 'Declined (click to clear)' : 'Decline line'" location="top">
                                    <template #activator="{ props }">
                                        <v-btn v-bind="props" icon="mdi-cancel" size="x-small" variant="text"
                                            :color="l.approvalStatus === 'declined' ? 'error' : undefined"
                                            @click="setLineApproval(l.id, l.approvalStatus === 'declined' ? 'pending' : 'declined')"></v-btn>
                                    </template>
                                </v-tooltip>
                            </template>
                            <v-tooltip v-if="!closed && l.lineKind === 'part' && !l.poLineId && !l.arrivedAt" text="Order from supplier" location="top">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" icon="mdi-truck-delivery-outline" size="x-small" variant="text"
                                        @click="openOrderPart(l)"></v-btn>
                                </template>
                            </v-tooltip>
                            <v-btn v-if="!closed" icon="mdi-close" size="x-small" variant="text" @click="removeLine(l.id)"></v-btn>
                        </div>
                        <div v-if="editing.lines.length" class="d-flex justify-space-between mt-1">
                            <strong>Total (pre-tax)</strong><strong>{{ money(orderTotal(editing)) }}</strong>
                        </div>
                        <div v-if="declinedTotal(editing) > 0" class="d-flex justify-space-between text-caption text-medium-emphasis">
                            <span>Declined (not billed)</span><span>{{ money(declinedTotal(editing)) }}</span>
                        </div>

                        <!-- Labor time: one timer per ticket, estimate vs actual. -->
                        <div class="d-flex align-center ga-2 flex-wrap mt-3 pa-2" style="background: rgba(var(--v-theme-on-surface), 0.03); border-radius: 6px">
                            <v-icon size="18" class="text-medium-emphasis">mdi-timer-outline</v-icon>
                            <span class="text-subtitle-2">Labor time</span>
                            <v-btn v-if="!editing.timerStartedAt && !closed" size="small" color="success" variant="tonal"
                                prepend-icon="mdi-play" :loading="timerBusy" @click="startTimer">Start</v-btn>
                            <v-btn v-else-if="editing.timerStartedAt" size="small" color="error" variant="tonal"
                                prepend-icon="mdi-stop" :loading="timerBusy" @click="stopTimer">Stop</v-btn>
                            <span class="text-body-2">
                                Actual <strong>{{ fmtMins(liveActualMinutes) }}</strong>
                                <span v-if="editing.timerStartedAt" class="text-success"> · running</span>
                                <template v-if="estimatedTotalMinutes > 0"> / Est <strong>{{ fmtMins(estimatedTotalMinutes) }}</strong></template>
                            </span>
                            <v-chip v-if="timeVariance" size="x-small" :color="timeVariance.color" variant="tonal">{{ timeVariance.text }}</v-chip>
                            <v-spacer></v-spacer>
                            <template v-if="adjustingTime">
                                <v-text-field v-model.number="adjustMins" type="number" min="0" density="compact" hide-details
                                    style="max-width: 110px" suffix="min" autofocus @keyup.enter="saveAdjust"></v-text-field>
                                <v-btn size="x-small" variant="text" color="primary" :loading="timerBusy" @click="saveAdjust">Save</v-btn>
                                <v-btn size="x-small" variant="text" @click="adjustingTime = false">Cancel</v-btn>
                            </template>
                            <v-btn v-else-if="!closed" size="x-small" variant="text" @click="openAdjust">Adjust</v-btn>
                        </div>

                        <template v-if="!closed">
                            <v-row dense class="mt-3">
                                <v-col :cols="newLine.kind === 'labor' ? 3 : 4">
                                    <v-select v-model="newLine.kind" :items="[{title:'Labor',value:'labor'},{title:'Part',value:'part'}]"
                                        item-title="title" item-value="value" label="Add" density="compact" hide-details></v-select>
                                </v-col>
                                <v-col :cols="newLine.kind === 'labor' ? 6 : 8">
                                    <v-text-field v-if="newLine.kind === 'labor'" v-model="newLine.description"
                                        label="Work performed" density="compact" hide-details></v-text-field>
                                    <v-select v-else v-model="newLine.variantId" :items="partVariants" item-title="title" item-value="id"
                                        label="Part" density="compact" hide-details></v-select>
                                </v-col>
                                <v-col v-if="newLine.kind === 'labor'" cols="3">
                                    <v-text-field v-model.number="newLine.estMin" type="number" min="0"
                                        label="Est. min" suffix="m" density="compact" hide-details></v-text-field>
                                </v-col>
                            </v-row>
                            <v-row dense class="mt-2">
                                <!-- Hours only when the shop has a rate and this is a labor line. -->
                                <v-col v-if="newLine.kind === 'labor' && laborRateDollars != null" cols="4">
                                    <v-text-field v-model.number="newLine.hours" type="number" min="0" step="0.25"
                                        label="Hours" suffix="hr" density="compact" hide-details
                                        @update:model-value="onLaborHoursInput"></v-text-field>
                                </v-col>
                                <v-col v-else cols="4">
                                    <v-text-field v-model.number="newLine.qty" type="number" min="1" label="Qty" density="compact" hide-details></v-text-field>
                                </v-col>
                                <v-col cols="4">
                                    <v-text-field v-model.number="newLine.priceDollars" type="number" min="0" step="0.01"
                                        :label="newLine.kind === 'labor' ? 'Price' : 'Price (blank = shelf)'" prefix="$"
                                        density="compact" hide-details
                                        @update:model-value="onLaborPriceInput"></v-text-field>
                                </v-col>
                                <v-col cols="4" class="d-flex align-center">
                                    <v-btn color="primary" variant="tonal" block :loading="addingLine" @click="addLine">Add line</v-btn>
                                </v-col>
                            </v-row>
                            <div v-if="newLine.kind === 'labor' && laborRateDollars != null"
                                class="text-caption text-medium-emphasis mt-1">
                                Labor rate ${{ laborRateDollars.toFixed(0) }}/hr. Enter hours to fill the price, or type a price for a flat charge.
                            </div>
                        </template>

                        <v-divider class="my-4"></v-divider>
                        <div class="wo-group-label mb-2">Deposit</div>
                        <div class="d-flex align-center ga-2 flex-wrap">
                            <v-text-field v-model.number="depositDollars" type="number" min="0" step="0.01" prefix="$"
                                label="Amount" density="compact" hide-details style="max-width: 130px"
                                :disabled="!!editing.depositPaidAt"></v-text-field>
                            <template v-if="!editing.depositPaidAt">
                                <v-btn size="small" variant="tonal" :loading="depositBusy === 'save'" @click="saveDeposit">Set</v-btn>
                                <v-btn size="small" variant="tonal" color="primary"
                                    :disabled="!editing.customerEmail || editing.depositCents < 50"
                                    :loading="depositBusy === 'email'" @click="emailDepositRequest">Email payment link</v-btn>
                                <v-btn size="small" variant="tonal" color="secondary" :disabled="editing.depositCents <= 0"
                                    :loading="depositBusy === 'cash'" @click="recordCashDeposit">Cash received</v-btn>
                                <v-chip v-if="editing.depositRequestSentAt" size="small" color="info" variant="tonal">Link sent</v-chip>
                            </template>
                            <template v-else>
                                <v-chip v-if="editing.depositRefundedAt" size="small" color="error" variant="tonal">Refunded</v-chip>
                                <v-chip v-else size="small" color="success" variant="tonal">
                                    Paid {{ money(editing.depositCents) }} ({{ editing.depositPaymentMethod === 'cash' ? 'cash' : 'card' }})
                                </v-chip>
                                <v-btn v-if="!editing.depositRefundedAt" size="small" variant="text" color="error"
                                    :loading="depositBusy === 'refund'" @click="refundDeposit">Refund</v-btn>
                            </template>
                        </div>
                        <p v-if="!editing.depositPaidAt && !editing.customerEmail" class="text-caption text-medium-emphasis mt-1">
                            Add a customer email above to send a payment link.
                        </p>
                    </template>

                    <div v-if="editorError" class="text-error text-body-2 mt-3">{{ editorError }}</div>
                </v-card-text>
                <v-card-actions style="flex: 0 0 auto">
                    <v-tooltip v-if="editing && (editing.customerUserId || editing.customerEmail || editing.customerPhone)" text="Customer history" location="top">
                        <template #activator="{ props }">
                            <v-btn v-bind="props" icon="mdi-history" variant="text" size="small"
                                @click="historyOpen = true"></v-btn>
                        </template>
                    </v-tooltip>
                    <v-tooltip v-if="editing" text="Print claim tag" location="top">
                        <template #activator="{ props }">
                            <v-btn v-bind="props" icon="mdi-printer" variant="text" size="small"
                                @click="printClaimTag"></v-btn>
                        </template>
                    </v-tooltip>
                    <v-tooltip v-if="editing && editing.lines.length > 0" text="Print estimate" location="top">
                        <template #activator="{ props }">
                            <v-btn v-bind="props" icon="mdi-file-document-outline" variant="text"
                                size="small" @click="printEstimate"></v-btn>
                        </template>
                    </v-tooltip>
                    <v-btn v-if="editing && !closed && editing.lines.length > 0" color="secondary" variant="tonal"
                        :disabled="form.status === 'estimate'" @click="openBill">Bill &amp; pick up</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="saving" @click="editorOpen = false">Close</v-btn>
                    <v-btn v-if="!closed" color="primary" :loading="saving" @click="save">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- ── Bill dialog ─────────────────────────────────────────────── -->
        <v-dialog v-model="billOpen" max-width="440" persistent>
            <v-card v-if="editing">
                <v-card-title class="d-flex align-center">
                    <span>Bill &amp; pick up</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="billing" @click="billOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-3">
                        {{ money(orderTotal(editing)) }} pre-tax (parts taxed at checkout). How is
                        {{ editing.customerName }} paying?
                    </p>
                    <v-text-field v-model.number="tipDollars" type="number" min="0" step="0.01" prefix="$"
                        label="Tip (optional)" density="compact" hide-details class="mb-3"></v-text-field>
                    <p v-if="billDepositCredit > 0" class="text-body-2 text-success mb-3">
                        Deposit paid: {{ money(billDepositCredit) }} will be applied at checkout.
                    </p>
                    <template v-if="billExcessLikely">
                        <p class="text-body-2 mb-1">
                            The deposit looks larger than this bill. What should happen with the overage?
                        </p>
                        <v-radio-group v-model="excessAction" hide-details density="compact" class="mb-3">
                            <v-radio value="refund"
                                :label="editing.depositPaymentMethod === 'cash' ? 'Refund it (hand back cash)' : 'Refund it to their card'"></v-radio>
                            <v-radio value="credit" label="Keep it as store credit on their account"></v-radio>
                        </v-radio-group>
                    </template>
                    <div v-if="billError" class="text-error text-body-2 mb-2">{{ billError }}</div>
                    <div class="d-flex ga-2">
                        <v-btn color="secondary" size="large" class="flex-grow-1" :loading="billing && billMethod === 'cash'"
                            :disabled="billing" @click="bill('cash')">Cash</v-btn>
                        <v-btn color="primary" size="large" class="flex-grow-1" :loading="billing && billMethod === 'card'"
                            :disabled="billing" @click="bill('card')">Card</v-btn>
                    </div>
                </v-card-text>
            </v-card>
        </v-dialog>

        <!-- ── Customer shop history ────────────────────────────────────── -->
        <v-dialog v-model="historyOpen" max-width="560">
            <v-card v-if="editing" class="d-flex flex-column" style="max-height: 90vh">
                <v-card-title class="d-flex align-center" style="flex: 0 0 auto">
                    <span>{{ editing.customerName }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="historyOpen = false"></v-btn>
                </v-card-title>
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                    <ShopHistoryPanel :user-id="editing.customerUserId"
                        :query="editing.customerUserId ? null : (editing.customerEmail || editing.customerPhone)" />
                </v-card-text>
            </v-card>
        </v-dialog>

        <!-- ── Order a part from a supplier (special order) ─────────────── -->
        <v-dialog v-model="orderOpen" max-width="440">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Order part from supplier</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="ordering" @click="orderOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-3">{{ orderLine ? lineName(orderLine) : '' }}
                        <span v-if="orderLine && orderLine.quantity > 1">× {{ orderLine.quantity }}</span></p>
                    <v-select v-model="orderPoId" :items="orderPoItems" item-title="title" item-value="id"
                        label="Add to an open purchase order" density="compact" hide-details clearable></v-select>
                    <v-select v-if="!orderPoId" v-model="orderSupplierId" :items="orderSuppliers" item-title="name" item-value="id"
                        label="Supplier for a new PO (optional)" density="compact" hide-details clearable class="mt-4"></v-select>
                    <v-text-field v-model.number="orderCostDollars" type="number" min="0" step="0.01" prefix="$"
                        label="Unit cost (blank = last known)" density="compact" hide-details class="mt-4"></v-text-field>
                    <p class="text-caption text-medium-emphasis mt-2">
                        The job moves to Awaiting parts; when the PO line is received, the part is
                        consumed automatically and the customer gets an email.
                    </p>
                    <div v-if="orderError" class="text-error text-body-2 mt-2">{{ orderError }}</div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="ordering" @click="orderOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="ordering" @click="orderPart">Place on order</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- ── Card payment for the bill ───────────────────────────────── -->
        <v-dialog v-model="payOpen" persistent max-width="480">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Card payment</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="paying" @click="payOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div class="text-h6 mb-3">{{ money(pendingTotal) }}</div>
                    <div id="wo-payment-element" class="mb-4"></div>
                    <div v-if="payError" class="text-error text-body-2 mb-2">{{ payError }}</div>
                    <v-btn block color="primary" size="large" :loading="paying" :disabled="!stripeReady" @click="payCard">
                        Charge {{ money(pendingTotal) }}
                    </v-btn>
                </v-card-text>
            </v-card>
        </v-dialog>

        <InspectionDialog v-model="inspectionOpen" :inspection-id="activeInspectionId"
            :bike-name="linkedBikeName" @saved="reloadInspections" @flash="flash" />

        <v-snackbar v-model="snackbar" :color="snackColor" :timeout="3500">{{ snackText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'
import dayjs from 'dayjs'
import { formatTenantDate, formatTenantDateTime } from '@/helpers/TenantTime'
import ConditionPhotos from '@/components/bikeshop/ConditionPhotos.vue'
import PhotoQrPanel from '@/components/bikeshop/PhotoQrPanel.vue'
import SignAgreementDialog from '@/components/bikeshop/SignAgreementDialog.vue'
import { type ShopInspection, type ShopJobTemplate, BikeShopService, type ShopProduct, type ShopWorkOrder, type ShopWorkOrderLine, type UpsertShopWorkOrder, type ShopBikeHistoryRow, type ShopWorkOrderStatusDef } from '@/services/BikeShopService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'
import { useConfirm } from '@/composables/useConfirm'
import ShopHistoryPanel from '@/components/ShopHistoryPanel.vue'
import JobTemplatesTab from '@/components/bikeshop/JobTemplatesTab.vue'
import InspectionDialog from '@/components/bikeshop/InspectionDialog.vue'
import { useRoute } from 'vue-router'

// ?tab=templates so the old Bike Shop "Saved jobs" tab redirects straight here.
const route = useRoute()
const tab = ref(String(route.query.tab ?? '') === 'templates' ? 'templates' : 'orders')

const service = new BikeShopService()
const confirmDialog = useConfirm()
const historyOpen = ref(false)

const orders = ref<ShopWorkOrder[]>([])
const products = ref<ShopProduct[]>([])
const technicians = ref<{ id: string; name: string }[]>([])
const loading = ref(false)
const loadError = ref('')
const includeClosed = ref(false)

const snackbar = ref(false); const snackText = ref(''); const snackColor = ref<'success' | 'error'>('success')
function flash(t: string, c: 'success' | 'error' = 'success') { snackText.value = t; snackColor.value = c; snackbar.value = true }
function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }
// Date-only fields (promisedAt): no zone, format as stored.
function formatDate(iso: string): string { return dayjs(iso).format('MMM D') }
// UTC timestamps: render in the tenant's timezone.
function formatTsDate(iso: string): string { return formatTenantDate(iso, 'MMM D') }
// ── Repair-order aging ────────────────────────────────────────────────────────────────
// Closed orders stop aging: a picked-up job from March isn't "146 days old", it's done.
function ageDays(o: ShopWorkOrder): number | null {
    if (o.status === 'picked_up' || o.status === 'cancelled') return null
    return dayjs().diff(dayjs(o.createdAt), 'day')
}
function ageLabel(o: ShopWorkOrder): string {
    const d = ageDays(o)
    if (d === null) return '—'
    return d === 0 ? 'today' : `${d}d`
}
// Amber at a week, red at a fortnight. Arbitrary, but it makes a forgotten bike visible
// without needing anyone to configure a threshold first.
function ageClass(o: ShopWorkOrder): string {
    const d = ageDays(o)
    if (d === null) return 'text-medium-emphasis'
    if (d >= 14) return 'text-error font-weight-medium'
    if (d >= 7) return 'text-warning'
    return 'text-medium-emphasis'
}
function overdue(o: ShopWorkOrder): boolean {
    if (!o.promisedAt) return false
    if (o.status === 'picked_up' || o.status === 'cancelled') return false
    return dayjs(o.promisedAt).endOf('day').isBefore(dayjs())
}

// ── Filters + sorting ───────────────────────────────────────────────────────
const search = ref('')
// Empty = open jobs only. Selecting statuses replaces that default entirely.
const statusFilter = ref<string[]>([])
const techFilter = ref<string | null>(null)
const dueFilter = ref<'all' | 'overdue' | 'unpromised'>('all')

// Tenant-defined statuses drive the labels, colors and dropdowns. Behavior is the fixed system
// meaning; a status is "terminal" (hidden from the default open view) when done or cancelled.
const statuses = ref<ShopWorkOrderStatusDef[]>([])
const statusMap = computed(() => new Map(statuses.value.map(s => [s.code, s])))
function isTerminalStatus(code: string): boolean {
    const b = statusMap.value.get(code)?.behavior
    return b === 'done' || b === 'cancelled'
}

const statusFilterItems = computed(() =>
    statuses.value.map(s => ({ value: s.code, title: s.name })))
const techFilterItems = computed(() => [
    { value: '__none__', title: 'Unassigned' },
    ...technicians.value.map(t => ({ value: t.id, title: t.name })),
])

const columns = [
    { key: 'customer', label: 'Customer' },
    { key: 'bike', label: 'Bike' },
    { key: 'status', label: 'Status' },
    { key: 'age', label: 'Age', width: '90px' },
    { key: 'promised', label: 'Promised' },
    { key: 'total', label: 'Total', align: 'right' },
]
const sortKey = ref('age')
const sortAsc = ref(false)     // oldest first: the job quietly rotting is the one to see
function toggleSort(key: string) {
    if (sortKey.value === key) sortAsc.value = !sortAsc.value
    else { sortKey.value = key; sortAsc.value = true }
}

function sortValue(o: ShopWorkOrder, key: string): string | number {
    switch (key) {
        case 'customer': return (o.customerName ?? '').toLowerCase()
        case 'bike': return (o.customerBikeDesc ?? '').toLowerCase()
        case 'status': return o.status
        case 'age': return new Date(o.createdAt).getTime()
        // Undated jobs sort last ascending rather than pretending to be due in 1970.
        case 'promised': return o.promisedAt ? new Date(o.promisedAt).getTime() : Number.MAX_SAFE_INTEGER
        case 'total': return orderTotal(o)
        default: return ''
    }
}

const visibleOrders = computed(() => {
    const q = search.value?.trim().toLowerCase()
    const rows = orders.value.filter(o => {
        if (statusFilter.value.length > 0) { if (!statusFilter.value.includes(o.status)) return false }
        else if (isTerminalStatus(o.status)) return false

        if (techFilter.value === '__none__') { if (o.assignedTechUserId) return false }
        else if (techFilter.value && o.assignedTechUserId !== techFilter.value) return false

        if (dueFilter.value === 'overdue' && !overdue(o)) return false
        if (dueFilter.value === 'unpromised' && o.promisedAt) return false

        if (q) {
            const hay = [o.customerName, o.customerPhone, o.customerEmail, o.customerBikeDesc]
                .filter(Boolean).join(' ').toLowerCase()
            if (!hay.includes(q)) return false
        }
        return true
    })
    return [...rows].sort((a, b) => {
        const av = sortValue(a, sortKey.value), bv = sortValue(b, sortKey.value)
        if (av < bv) return sortAsc.value ? -1 : 1
        if (av > bv) return sortAsc.value ? 1 : -1
        return 0
    })
})

// Declined lines are excluded from the total: they won't be billed.
function orderTotal(o: ShopWorkOrder): number {
    return o.lines.reduce((s, l) => l.approvalStatus === 'declined' ? s : s + l.unitPriceCents * l.quantity, 0)
}
function declinedTotal(o: ShopWorkOrder): number {
    return o.lines.reduce((s, l) => l.approvalStatus === 'declined' ? s + l.unitPriceCents * l.quantity : s, 0)
}
function hasPendingLines(o: ShopWorkOrder): boolean { return o.lines.some(l => l.approvalStatus === 'pending') }

async function setLineApproval(lineId: string, status: 'pending' | 'approved' | 'declined') {
    if (!editing.value) return
    try {
        await service.setLineApproval(lineId, status)
        await reloadEditing(editing.value.id)
    } catch (e: any) {
        editorError.value = e.response?.data?.error || 'Could not update the line.'
    }
}
async function approveAllLines() {
    if (!editing.value) return
    try {
        await service.approveAllLines(editing.value.id)
        await reloadEditing(editing.value.id)
    } catch (e: any) {
        editorError.value = e.response?.data?.error || 'Could not approve the lines.'
    }
}
function statusLabel(s: string): string { return statusMap.value.get(s)?.name ?? s.replace(/_/g, ' ') }
function statusColor(s: string): string { return statusMap.value.get(s)?.color ?? 'grey' }
function lineName(l: ShopWorkOrderLine): string {
    if (l.lineKind === 'labor') return l.description ?? 'Labor'
    const v = products.value.flatMap(p => p.variants.map(x => ({ p, x }))).find(e => e.x.id === l.variantId)
    return v ? v.p.name : (l.description ?? 'Part')
}

const partVariants = computed(() =>
    products.value.filter(p => p.isActive).flatMap(p =>
        p.variants.filter(v => v.isActive && v.trackingKind === 'pool' && v.salePriceCents != null).map(v => ({
            id: v.id,
            title: `${p.name}${[v.size, v.color].filter(Boolean).length ? ' (' + [v.size, v.color].filter(Boolean).join('/') + ')' : ''} — ${money(v.salePriceCents!)}`,
        }))))

// Staff can set any active status except the terminal "done" (picked up) one, which is a billing
// outcome. Cancelled stays selectable.
const statusItems = computed(() =>
    statuses.value
        .filter(s => s.isActive && s.behavior !== 'done')
        .map(s => ({ title: s.name, value: s.code })))

// ── Editor ─────────────────────────────────────────────────────────────────
const editorOpen = ref(false)
const editing = ref<ShopWorkOrder | null>(null)
const saving = ref(false)
const editorError = ref('')
const form = ref<UpsertShopWorkOrder>(blankForm())
const closed = computed(() => editing.value?.status === 'picked_up' || editing.value?.status === 'cancelled')

// ── Customer bike (serial-first intake) ─────────────────────────────────────
const bikeSerial = ref('')
const bikeLookingUp = ref(false)
const bikeMatch = ref<'known_bike' | 'sold_by_us' | 'unknown' | null>(null)
// Structured bike fields are revealed by a lookup OR by asking for them, so a bike with an
// unreadable serial can still get a record (and therefore inspections).
const showBikeDetails = ref(false)
const hasBikeDetails = computed(() => {
    const b = bikeForm.value
    return !!(bikeSerial.value?.trim() || b.brand || b.model || b.color || b.size)
})
const bikeHistory = ref<ShopBikeHistoryRow[]>([])
const linkedBikeId = ref<string | null>(null)
const linkedBikeName = ref('')
const bikeForm = ref<{ brand: string | null; model: string | null; modelYear: number | null; color: string | null; size: string | null }>(
    { brand: null, model: null, modelYear: null, color: null, size: null })

function resetBike() {
    bikeInspections.value = []
    showBikeDetails.value = false
    bikeSerial.value = ''
    bikeMatch.value = null
    bikeHistory.value = []
    linkedBikeId.value = null
    linkedBikeName.value = ''
    bikeForm.value = { brand: null, model: null, modelYear: null, color: null, size: null }
}

async function lookupBike() {
    const serial = bikeSerial.value?.trim()
    if (!serial) return
    bikeLookingUp.value = true
    try {
        const r = await service.lookupBike(serial)
        const d = r.data.data
        bikeMatch.value = d.match
        showBikeDetails.value = true
        bikeHistory.value = d.history ?? []

        if (d.match === 'known_bike' && d.bike) {
            linkedBikeId.value = d.bike.id
            linkedBikeName.value = d.displayName || d.bike.serial || 'Bike'
            bikeForm.value = {
                brand: d.bike.brand, model: d.bike.model, modelYear: d.bike.modelYear,
                color: d.bike.color, size: d.bike.size,
            }
            // A known bike already knows its owner; don't make staff retype it.
            if (!form.value.customerName?.trim() && d.bike.customerName) form.value.customerName = d.bike.customerName
            if (!form.value.customerPhone?.trim() && d.bike.customerPhone) form.value.customerPhone = d.bike.customerPhone
        } else if (d.match === 'sold_by_us' && d.suggestion) {
            // Nothing persisted yet: this is a suggestion staff confirm by saving.
            linkedBikeId.value = null
            linkedBikeName.value = [d.suggestion.brand, d.suggestion.model].filter(Boolean).join(' ')
            bikeForm.value = {
                brand: d.suggestion.brand, model: d.suggestion.model,
                modelYear: null, color: null, size: null,
            }
            if (!form.value.customerName?.trim() && d.suggestion.customerName) {
                form.value.customerName = d.suggestion.customerName
            }
        } else {
            linkedBikeId.value = null
            linkedBikeName.value = ''
        }
    } catch (e: any) {
        editorError.value = e.response?.data?.error || 'Could not look that serial up. Try again.'
    } finally {
        bikeLookingUp.value = false
    }
}

// ── Inspections on this bike ────────────────────────────────────────────────
const bikeInspections = ref<ShopInspection[]>([])
const inspectionOpen = ref(false)
const activeInspectionId = ref<string | null>(null)
const startingInspection = ref(false)

async function reloadInspections() {
    if (!linkedBikeId.value) { bikeInspections.value = []; return }
    try {
        bikeInspections.value = (await service.inspectionsForBike(linkedBikeId.value)).data.data
    } catch {
        // History is context, not required to work the job.
        bikeInspections.value = []
    }
}

async function startInspection() {
    if (!linkedBikeId.value) return
    startingInspection.value = true
    try {
        const r = await service.startInspection({
            customerBikeId: linkedBikeId.value,
            workOrderId: editing.value?.id ?? null,
        })
        activeInspectionId.value = r.data.data.id
        inspectionOpen.value = true
        await reloadInspections()
    } catch (e: any) {
        editorError.value = e.response?.data?.error || 'Could not start an inspection.'
    } finally {
        startingInspection.value = false
    }
}

function openInspection(id: string) {
    activeInspectionId.value = id
    inspectionOpen.value = true
}

// Persist the bike (find-or-create on serial) and return its id to attach to the work order.
async function persistBike(): Promise<string | null> {
    const b = bikeForm.value
    // Fields never revealed and nothing typed: leave any existing bike record untouched rather
    // than overwriting it with a form the user was never shown.
    if (!showBikeDetails.value && !hasBikeDetails.value) return linkedBikeId.value
    // Nothing entered and nothing already linked: this job is just a free-text description.
    if (!hasBikeDetails.value && !linkedBikeId.value) return null
    try {
        const r = await service.upsertBike({
            id: linkedBikeId.value,
            serial: bikeSerial.value?.trim() || null,
            brand: b.brand || null,
            model: b.model || null,
            modelYear: b.modelYear || null,
            color: b.color || null,
            size: b.size || null,
            customerName: form.value.customerName?.trim() || null,
            customerPhone: form.value.customerPhone?.trim() || null,
        })
        linkedBikeId.value = r.data.data.id
        return r.data.data.id
    } catch (e: any) {
        // Saving the bike must not lose the work order: fall through and save the job with the
        // free-text description instead of blocking intake on a bike-record failure.
        flash(e.response?.data?.error || "Saved the job, but couldn't save the bike record.", 'error')
        return linkedBikeId.value
    }
}

function blankForm(): UpsertShopWorkOrder {
    return { customerName: '', customerPhone: null, customerEmail: null, customerBikeDesc: null,
        status: 'intake', intakeNotes: null, customerNotes: null, promisedAt: null, assignedTechUserId: null,
        groupId: null }
}
function openNew() {
    editing.value = null
    form.value = blankForm()
    // Start in the tenant's default status (falls back to the seeded 'intake' code).
    form.value.status = (statuses.value.find(s => s.isDefault)?.code ?? 'intake') as UpsertShopWorkOrder['status']
    resetBike()
    editorError.value = ''
    editorOpen.value = true
}
function openEdit(o: ShopWorkOrder) {
    editing.value = o
    resetBike()
    if (o.customerBikeId) {
        linkedBikeId.value = o.customerBikeId
        void loadBikeForEdit(o.customerBikeId)
        void reloadInspections()
    }
    form.value = {
        customerName: o.customerName, customerPhone: o.customerPhone, customerEmail: o.customerEmail,
        customerBikeDesc: o.customerBikeDesc,
        status: (o.status === 'picked_up' ? 'ready' : o.status) as UpsertShopWorkOrder['status'],
        intakeNotes: o.intakeNotes,
        customerNotes: o.customerNotes,
        promisedAt: o.promisedAt ? dayjs(o.promisedAt).format('YYYY-MM-DD') : null,
        assignedTechUserId: o.assignedTechUserId,
    }
    depositDollars.value = o.depositCents > 0 ? o.depositCents / 100 : null
    tipDollars.value = null
    editorError.value = ''
    newLine.value = { kind: 'labor', description: '', variantId: null, qty: 1, priceDollars: null, hours: null, estMin: null }
    editorOpen.value = true
}

// Editing an existing job: pull the linked bike so its details and history show.
async function loadBikeForEdit(bikeId: string) {
    try {
        const [hist, bikes] = await Promise.all([
            service.bikeHistory(bikeId),
            service.listCustomerBikes({ customerUserId: null, phone: form.value.customerPhone || null }),
        ])
        bikeHistory.value = hist.data.data
        const b = bikes.data.data.find(x => x.id === bikeId)
        if (b) {
            bikeMatch.value = 'known_bike'
            showBikeDetails.value = true
            bikeSerial.value = b.serial ?? ''
            linkedBikeName.value = b.displayName || b.serial || 'Bike'
            bikeForm.value = { brand: b.brand, model: b.model, modelYear: b.modelYear, color: b.color, size: b.size }
        }
    } catch { /* history is context, not required to edit the job */ }
}

async function save() {
    editorError.value = ''
    if (!form.value.customerName.trim()) { editorError.value = 'Customer name is required.'; return }
    if (!form.value.customerBikeDesc?.trim() && !hasBikeDetails.value && !linkedBikeId.value) {
        editorError.value = 'Describe the bike being serviced, or add its details below.'; return
    }
    saving.value = true
    try {
        const bikeId = await persistBike()
        const body: UpsertShopWorkOrder = {
            ...form.value,
            customerBikeId: bikeId,
            customerName: form.value.customerName.trim(),
            customerPhone: form.value.customerPhone?.trim() || null,
            customerEmail: form.value.customerEmail?.trim() || null,
            customerBikeDesc: form.value.customerBikeDesc?.trim() || null,
            intakeNotes: form.value.intakeNotes?.trim() || null,
            customerNotes: form.value.customerNotes?.trim() || null,
            promisedAt: form.value.promisedAt || null,
        }
        if (editing.value) {
            await service.updateWorkOrder(editing.value.id, body)
            flash('Work order saved.')
            await reloadEditing(editing.value.id)
            await reloadInspections()
        } else {
            const r = await service.createWorkOrder(body)
            flash('Work order created.')
            await reload()
            const created = orders.value.find(o => o.id === r.data.data.id)
            if (created) openEdit(created)
        }
        await reload()
    } catch (e: any) {
        editorError.value = e.response?.data?.error || 'Could not save the work order.'
    } finally { saving.value = false }
}

// ── Lines ──────────────────────────────────────────────────────────────────
const newLine = ref<{ kind: 'labor' | 'part'; description: string; variantId: string | null; qty: number; priceDollars: number | null; hours: number | null; estMin: number | null }>(
    { kind: 'labor', description: '', variantId: null, qty: 1, priceDollars: null, hours: null, estMin: null })

// The shop's $/hour, if one is configured. When set, labor lines offer an Hours field that fills
// the price; when not, labor stays a typed price exactly as before.
const laborRateDollars = computed(() =>
    branding.shopLaborRateCents == null ? null : branding.shopLaborRateCents / 100)

// Hours drive the price. Typing hours recomputes the price from the shop rate.
function onLaborHoursInput() {
    const rate = laborRateDollars.value
    const h = newLine.value.hours
    if (rate != null && h != null && !isNaN(h) && h > 0) {
        newLine.value.priceDollars = Math.round(h * rate * 100) / 100
    }
}
// Typing a price directly means a flat charge, so drop the hours (they'd contradict the price).
function onLaborPriceInput() {
    if (newLine.value.kind === 'labor') newLine.value.hours = null
}

// ── Labor time (timer + estimate vs actual) ─────────────────────────────────
const timerBusy = ref(false)
const adjustingTime = ref(false)
const adjustMins = ref(0)
// Ticks so the running timer's displayed elapsed advances without a reload.
const nowMs = ref(Date.now())
let timeTicker: ReturnType<typeof setInterval> | null = null

const estimatedTotalMinutes = computed(() =>
    editing.value?.lines.reduce((s, l) => s + (l.estimatedMinutes || 0), 0) ?? 0)
const liveActualMinutes = computed(() => {
    const o = editing.value
    if (!o) return 0
    let m = o.actualMinutes
    if (o.timerStartedAt) m += Math.max(0, Math.floor((nowMs.value - new Date(o.timerStartedAt).getTime()) / 60000))
    return m
})
function fmtMins(m: number): string {
    if (m < 60) return `${m}m`
    const h = Math.floor(m / 60), r = m % 60
    return r ? `${h}h ${r}m` : `${h}h`
}
// Only judge variance once there's an estimate and some actual time logged.
const timeVariance = computed(() => {
    const est = estimatedTotalMinutes.value, act = liveActualMinutes.value
    if (est <= 0 || act <= 0) return null
    if (act > est * 1.1) return { color: 'error', text: `over by ${fmtMins(act - est)}` }
    if (act < est * 0.9) return { color: 'success', text: `under by ${fmtMins(est - act)}` }
    return { color: 'success', text: 'on estimate' }
})

async function startTimer() {
    if (!editing.value) return
    timerBusy.value = true
    try { editing.value = (await service.startWorkOrderTimer(editing.value.id)).data.data }
    catch (e: any) { editorError.value = e.response?.data?.error || 'Could not start the timer.' }
    finally { timerBusy.value = false }
}
async function stopTimer() {
    if (!editing.value) return
    timerBusy.value = true
    try { editing.value = (await service.stopWorkOrderTimer(editing.value.id)).data.data }
    catch (e: any) { editorError.value = e.response?.data?.error || 'Could not stop the timer.' }
    finally { timerBusy.value = false }
}
function openAdjust() { adjustMins.value = liveActualMinutes.value; adjustingTime.value = true }
async function saveAdjust() {
    if (!editing.value) return
    timerBusy.value = true
    try {
        editing.value = (await service.setWorkOrderActualMinutes(editing.value.id,
            Math.max(0, Math.round(adjustMins.value || 0)))).data.data
        adjustingTime.value = false
    } catch (e: any) { editorError.value = e.response?.data?.error || 'Could not update the time.' }
    finally { timerBusy.value = false }
}

// ── Customer visit (multi-bike) ─────────────────────────────────────────────
const addingBike = ref(false)
// Ensure the current ticket has a visit group, then open a fresh ticket pre-filled with the same
// customer so a family's second/third bike doesn't mean retyping their details.
async function addAnotherBike() {
    if (!editing.value) return
    const cur = editing.value
    addingBike.value = true
    try {
        const groupId = cur.groupId ?? (await service.ensureWorkOrderGroup(cur.id)).data.data.groupId
        openNew()
        form.value.customerName = cur.customerName
        form.value.customerPhone = cur.customerPhone
        form.value.customerEmail = cur.customerEmail
        form.value.customerUserId = cur.customerUserId
        form.value.groupId = groupId
    } catch (e: any) {
        editorError.value = e.response?.data?.error || 'Could not start another bike.'
    } finally { addingBike.value = false }
}
async function openSibling(woId: string) {
    try {
        openEdit((await service.getWorkOrder(woId)).data.data)
    } catch (e: any) {
        editorError.value = e.response?.data?.error || 'Could not open that bike.'
    }
}

// ── QC sign-off ─────────────────────────────────────────────────────────────
const checkingQc = ref(false)
async function setQc(userId: string | null) {
    if (!editing.value) return
    checkingQc.value = true
    try {
        // The server stamps checked_at and returns the refreshed order.
        editing.value = (await service.setWorkOrderQc(editing.value.id, userId ?? null)).data.data
        await reload()  // keep the list's checked indicator in sync
    } catch (e: any) {
        editorError.value = e.response?.data?.error || 'Could not update the QC check.'
    } finally { checkingQc.value = false }
}

// ── Internal notes thread ───────────────────────────────────────────────────
const newNote = ref('')
const addingNote = ref(false)
function noteTime(iso: string): string { return formatTenantDateTime(iso, 'MMM D, h:mm A') }

async function addNote() {
    const body = newNote.value.trim()
    if (!body || !editing.value) return
    addingNote.value = true
    try {
        const created = (await service.addWorkOrderNote(editing.value.id, body)).data.data
        // Prepend the server's note (newest first) rather than refetching the whole order.
        if (!editing.value.notes) editing.value.notes = []
        editing.value.notes.unshift(created)
        newNote.value = ''
    } catch (e: any) {
        editorError.value = e.response?.data?.error || 'Could not add the note.'
    } finally { addingNote.value = false }
}
const addingLine = ref(false)

// Saved jobs, loaded once for the picker above the lines.
const templates = ref<ShopJobTemplate[]>([])
const applyingTemplate = ref(false)
const activeTemplates = computed(() => templates.value.filter(t => t.isActive))

async function loadTemplates() {
    try {
        templates.value = ((await service.listJobTemplates(true)).data as any).data
    } catch {
        // A missing picker is a small loss; the editor still works line by line.
        templates.value = []
    }
}

async function applyTemplate(t: ShopJobTemplate) {
    if (!editing.value) return
    applyingTemplate.value = true
    try {
        const r = await service.applyJobTemplate(editing.value.id, t.id)
        const { added, skipped } = r.data.data
        await reloadEditing(editing.value.id)
        // Naming what was skipped matters: a part whose product was deactivated silently
        // missing from a quote is how a job gets underbilled.
        flash(skipped.length
            ? `Added ${added} line(s). Skipped ${skipped.join(', ')} (no longer available).`
            : `Added ${added} line(s) from ${t.name}.`,
            skipped.length ? 'error' : 'success')
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not add that job.', 'error')
    } finally {
        applyingTemplate.value = false
    }
}

async function addLine() {
    if (!editing.value) return
    editorError.value = ''
    addingLine.value = true
    try {
        // Send hours only in rate-driven labor mode; the server prices it from hours * the shop
        // rate. A labor line priced by hours is a single line, so its quantity is 1.
        const inHoursMode = newLine.value.kind === 'labor' && laborRateDollars.value != null
            && newLine.value.hours != null && !isNaN(newLine.value.hours) && newLine.value.hours > 0
        await service.addWorkOrderLine(editing.value.id, {
            lineKind: newLine.value.kind,
            description: newLine.value.kind === 'labor' ? newLine.value.description.trim() : null,
            variantId: newLine.value.kind === 'part' ? newLine.value.variantId : null,
            quantity: inHoursMode ? 1 : Math.max(1, Math.round(newLine.value.qty)),
            unitPriceCents: newLine.value.priceDollars != null && !isNaN(newLine.value.priceDollars)
                ? Math.round(newLine.value.priceDollars * 100) : null,
            laborHours: inHoursMode ? newLine.value.hours : null,
            estimatedMinutes: newLine.value.kind === 'labor' && newLine.value.estMin != null && !isNaN(newLine.value.estMin)
                ? Math.max(0, Math.round(newLine.value.estMin)) : null,
        })
        newLine.value = { kind: newLine.value.kind, description: '', variantId: null, qty: 1, priceDollars: null, hours: null, estMin: null }
        await reloadEditing(editing.value.id)
    } catch (e: any) {
        editorError.value = e.response?.data?.error || 'Could not add the line.'
    } finally { addingLine.value = false }
}

async function removeLine(lineId: string) {
    if (!editing.value) return
    try {
        await service.removeWorkOrderLine(lineId)
        await reloadEditing(editing.value.id)
    } catch (e: any) {
        editorError.value = e.response?.data?.error || 'Could not remove the line.'
    }
}

async function reloadEditing(id: string) {
    try {
        editing.value = (await service.getWorkOrder(id)).data.data
        depositDollars.value = editing.value.depositCents > 0 ? editing.value.depositCents / 100 : null
        await reload()
    } catch { /* the list reload surfaces its own error */ }
}

// ── Deposit ────────────────────────────────────────────────────────────────
const depositDollars = ref<number | null>(null)
const depositBusy = ref<'save' | 'email' | 'cash' | 'refund' | null>(null)

async function saveDeposit() {
    if (!editing.value) return
    editorError.value = ''
    depositBusy.value = 'save'
    try {
        const cents = Math.max(0, Math.round((depositDollars.value ?? 0) * 100))
        await service.setWorkOrderDeposit(editing.value.id, cents)
        flash('Deposit set.')
        await reloadEditing(editing.value.id)
    } catch (e: any) {
        editorError.value = e.response?.data?.error || 'Could not set the deposit.'
    } finally { depositBusy.value = null }
}

async function emailDepositRequest() {
    if (!editing.value) return
    editorError.value = ''
    depositBusy.value = 'email'
    try {
        await service.sendWorkOrderDepositRequest(editing.value.id)
        flash(`Payment link emailed to ${editing.value.customerEmail}.`)
        await reloadEditing(editing.value.id)
    } catch (e: any) {
        editorError.value = e.response?.data?.error || 'Could not send the payment link.'
    } finally { depositBusy.value = null }
}

async function recordCashDeposit() {
    if (!editing.value) return
    editorError.value = ''
    depositBusy.value = 'cash'
    try {
        await service.recordWorkOrderCashDeposit(editing.value.id)
        flash('Cash deposit recorded.')
        await reloadEditing(editing.value.id)
    } catch (e: any) {
        editorError.value = e.response?.data?.error || 'Could not record the cash deposit.'
    } finally { depositBusy.value = null }
}

async function refundDeposit() {
    if (!editing.value) return
    const ok = await confirmDialog({
        title: 'Refund deposit?',
        message: `Refund the ${money(editing.value.depositCents)} deposit to ${editing.value.customerName}?`,
        confirmText: 'Refund',
        confirmColor: 'error',
    })
    if (!ok) return
    editorError.value = ''
    depositBusy.value = 'refund'
    try {
        await service.refundWorkOrderDeposit(editing.value.id)
        flash('Deposit refunded.')
        await reloadEditing(editing.value.id)
    } catch (e: any) {
        editorError.value = e.response?.data?.error || 'Could not refund the deposit.'
    } finally { depositBusy.value = null }
}

// ── Special order (order a part line from a supplier) ──────────────────────
const orderOpen = ref(false)
const ordering = ref(false)
const orderError = ref('')
const orderLine = ref<ShopWorkOrderLine | null>(null)
const orderPoId = ref<string | null>(null)
const orderSupplierId = ref<string | null>(null)
const orderCostDollars = ref<number | null>(null)
const orderPos = ref<{ id: string; reference: string | null; status: string; supplierId: string | null }[]>([])
const orderSuppliers = ref<{ id: string; name: string }[]>([])

const orderPoItems = computed(() => orderPos.value.map(p => ({
    id: p.id,
    title: `${p.reference || p.id.slice(0, 8).toUpperCase()} (${p.status}${supplierName(p.supplierId) ? ', ' + supplierName(p.supplierId) : ''})`,
})))
function supplierName(id: string | null): string {
    return orderSuppliers.value.find(s => s.id === id)?.name ?? ''
}

async function openOrderPart(l: ShopWorkOrderLine) {
    orderLine.value = l
    orderPoId.value = null
    orderSupplierId.value = null
    orderCostDollars.value = null
    orderError.value = ''
    orderOpen.value = true
    try {
        const r = await service.specialOrderOptions()
        orderPos.value = r.data.data.pos
        orderSuppliers.value = r.data.data.suppliers
    } catch (e: any) {
        orderError.value = e.response?.data?.error || 'Could not load purchase orders. You can still create a new one.'
    }
}

async function orderPart() {
    if (!editing.value || !orderLine.value) return
    orderError.value = ''
    ordering.value = true
    try {
        await service.orderWorkOrderLine(editing.value.id, orderLine.value.id, {
            poId: orderPoId.value,
            supplierId: orderPoId.value ? null : orderSupplierId.value,
            unitCostCents: orderCostDollars.value != null && !isNaN(orderCostDollars.value)
                ? Math.round(orderCostDollars.value * 100) : null,
        })
        orderOpen.value = false
        flash('Part placed on order.')
        await reloadEditing(editing.value.id)
    } catch (e: any) {
        orderError.value = e.response?.data?.error || 'Could not place the order.'
    } finally { ordering.value = false }
}

// ── Printing (claim tag + estimate) ────────────────────────────────────────
function printHtml(title: string, body: string) {
    const w = window.open('', '_blank', 'width=420,height=600')
    if (!w) { flash('Allow pop-ups to print.', 'error'); return }
    w.document.write(`<!doctype html><html><head><title>${title}</title>
        <style>body{font-family:Arial,Helvetica,sans-serif;margin:16px;font-size:13px}
        h2{margin:0 0 2px;font-size:17px} .muted{color:#555} table{width:100%;border-collapse:collapse;margin-top:8px}
        td{padding:2px 0;vertical-align:top} .r{text-align:right} .total{font-weight:bold;border-top:1px solid #000}
        .tag{font-size:22px;font-weight:bold;letter-spacing:2px;margin:6px 0}.sub-tabs {
    background: rgba(var(--v-theme-on-surface), 0.04);
    border-radius: 4px;
    padding: 4px;
    display: inline-flex;
    flex: 0 0 auto;
}
.sub-tabs :deep(.v-slide-group__content) { gap: 4px; align-items: center; }
.sub-tabs :deep(.v-tab) {
    border-radius: 4px; height: 32px; min-height: 32px; min-width: 0;
    padding: 0 18px; font-size: 13px; text-transform: none; opacity: 0.75;
}
.sub-tabs :deep(.v-tab.sub-tab-active),
.sub-tabs :deep(.v-tab--selected) {
    background: rgba(var(--v-theme-primary), 0.14);
    color: rgb(var(--v-theme-primary));
    opacity: 1;
    font-weight: 600;
}
.sortable-col {
    cursor: pointer;
    user-select: none;
    white-space: nowrap;
}
.sortable-col:hover { background: rgba(var(--v-theme-on-surface), 0.04); }
.insp-link { cursor: pointer; }
.insp-link:hover { background: rgba(var(--v-theme-on-surface), 0.04); }
</style></head><body>${body}</body></html>`)
    w.document.close()
    w.focus()
    w.print()
}
function esc(s: string | null | undefined): string {
    return (s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}

function printClaimTag() {
    const o = editing.value
    if (!o) return
    printHtml('Claim tag', `
        <h2>${esc(branding.displayName)}</h2>
        <div class="tag">#${o.id.slice(0, 8).toUpperCase()}</div>
        <div><strong>${esc(o.customerName)}</strong>${o.customerPhone ? ' · ' + esc(o.customerPhone) : ''}</div>
        <div>${esc(o.customerBikeDesc || '(shop unit)')}</div>
        <div class="muted">Taken in ${formatTenantDate(o.createdAt, 'MMM D, YYYY')}${o.promisedAt ? ' · promised ' + dayjs(o.promisedAt).format('MMM D') : ''}</div>
        ${o.intakeNotes ? `<div class="muted" style="margin-top:6px">${esc(o.intakeNotes)}</div>` : ''}
        ${o.customerNotes ? `<div style="margin-top:6px">${esc(o.customerNotes)}</div>` : ''}`)
}

function printEstimate() {
    const o = editing.value
    if (!o) return
    const rows = o.lines.map(l => {
        const tag = l.approvalStatus === 'approved' ? ' <strong>[approved]</strong>'
            : l.approvalStatus === 'declined' ? ' <span class="muted">[declined]</span>' : ''
        const style = l.approvalStatus === 'declined' ? ' style="text-decoration:line-through;color:#999"' : ''
        return `<tr${style}><td>${esc(lineName(l))}${l.quantity > 1 ? ` × ${l.quantity}` : ''}${tag}</td><td class="r">${money(l.unitPriceCents * l.quantity)}</td></tr>`
    }).join('')
    const deposit = o.depositPaidAt && !o.depositRefundedAt
        ? `<tr><td>Deposit paid</td><td class="r">-${money(o.depositCents)}</td></tr>` : ''
    printHtml('Estimate', `
        <h2>${esc(branding.displayName)}</h2>
        <div class="muted">${o.status === 'estimate' ? 'Estimate' : 'Service order'} #${o.id.slice(0, 8).toUpperCase()} · ${dayjs().format('MMM D, YYYY')}</div>
        <div style="margin-top:6px"><strong>${esc(o.customerName)}</strong>${o.customerPhone ? ' · ' + esc(o.customerPhone) : ''}</div>
        <div>${esc(o.customerBikeDesc || '(shop unit)')}</div>
        <table>${rows}
        <tr class="total"><td>Total (pre-tax)</td><td class="r">${money(orderTotal(o))}</td></tr>${deposit}</table>
        ${o.customerNotes ? `<div style="margin-top:8px">${esc(o.customerNotes)}</div>` : ''}
        <div class="muted" style="margin-top:8px">Parts are taxed at checkout. This is not a receipt.</div>`)
}

// ── Billing ────────────────────────────────────────────────────────────────
const billOpen = ref(false)
const billing = ref(false)
const billError = ref('')
const billMethod = ref<'cash' | 'card' | null>(null)
const tipDollars = ref<number | null>(null)
const excessAction = ref<'refund' | 'credit'>('refund')
// What's still on the deposit (partial refunds/credit conversions reduce it).
const billDepositCredit = computed(() =>
    editing.value?.depositPaidAt
        ? Math.max(0, editing.value.depositCents - editing.value.depositRefundedCents) : 0)
// Pre-tax approximation just to decide whether to show the overage choice; the server computes
// the real excess (with tax) and refuses without a choice if one is needed.
const billExcessLikely = computed(() =>
    editing.value != null && billDepositCredit.value > orderTotal(editing.value))
function openBill() { billError.value = ''; excessAction.value = 'refund'; billOpen.value = true }

async function bill(method: 'cash' | 'card') {
    if (!editing.value) return
    billError.value = ''
    billMethod.value = method
    billing.value = true
    try {
        const tipCents = tipDollars.value != null && !isNaN(tipDollars.value)
            ? Math.max(0, Math.round(tipDollars.value * 100)) : 0
        const r = await service.billWorkOrder(editing.value.id, {
            paymentMethod: method, tipCents,
            excessAction: billDepositCredit.value > 0 ? excessAction.value : null,
        })
        const data = r.data.data
        const due = data.dueCents ?? data.totalCents
        if (data.status === 'paid') {
            // Cash, or a deposit that covered the whole bill (the server settles those without a card).
            billOpen.value = false
            editorOpen.value = false
            let excessNote = ''
            if ((data.depositExcessCents ?? 0) > 0) {
                excessNote = data.excessAction === 'credit'
                    ? `; ${money(data.depositExcessCents!)} kept as store credit`
                    : data.depositWasCash
                        ? `; HAND BACK ${money(data.depositExcessCents!)} CASH`
                        : `; ${money(data.depositExcessCents!)} refunded to their card`
            }
            flash(`Picked up: ${money(due)} ${method === 'cash' || due === 0 ? 'cash' : 'card'}` +
                `${(data.depositAppliedCents ?? 0) > 0 ? ` (deposit ${money(data.depositAppliedCents!)} applied)` : ''}` +
                `${data.orderNumber != null ? ', sale #' + data.orderNumber : ''}${excessNote}.`)
            await reload()
        } else {
            billOpen.value = false
            pendingTotal.value = due
            clientSecret.value = data.clientSecret ?? null
            payOpen.value = true
            await nextTick()
            await mountPayment()
        }
    } catch (e: any) {
        billError.value = e.response?.data?.error || 'Could not bill this work order.'
    } finally { billing.value = false }
}

const payOpen = ref(false)
const paying = ref(false)
const payError = ref('')
const stripeReady = ref(false)
const clientSecret = ref<string | null>(null)
const pendingTotal = ref(0)
let stripe: any = null
let elements: any = null

async function mountPayment() {
    payError.value = ''
    stripeReady.value = false
    if (!clientSecret.value) { payError.value = 'Payment could not be started.'; return }
    const account = branding.stripeChargeMode === 'direct' ? branding.stripeConnectAccountId : null
    stripe = await getStripe(branding.stripePublishableKey, account)
    if (!stripe) { payError.value = 'Payments are unavailable right now.'; return }
    elements = stripe.elements({ clientSecret: clientSecret.value })
    elements.create('payment').mount('#wo-payment-element')
    stripeReady.value = true
}

async function payCard() {
    if (!stripe || !elements) return
    paying.value = true
    payError.value = ''
    try {
        const { error, paymentIntent } = await stripe.confirmPayment({ elements, redirect: 'if_required' })
        if (error) {
            payError.value = error.message || 'Payment failed. Check the card and try again.'
        } else if (paymentIntent?.status === 'succeeded') {
            try { await service.confirmIntent(paymentIntent.id) } catch { /* webhook finalizes */ }
            payOpen.value = false
            editorOpen.value = false
            flash('Paid — work order picked up.')
            await reload()
        } else {
            payError.value = 'The payment has not settled yet. It will complete shortly.'
        }
    } catch (e: any) {
        payError.value = e?.message || 'Payment failed.'
    } finally { paying.value = false }
}

async function reload() {
    loading.value = orders.value.length === 0 && products.value.length === 0
    loadError.value = ''
    try {
        const [o, p, t, st] = await Promise.all([
            service.listWorkOrders(includeClosed.value), service.listProducts(true), service.listTechnicians(),
            service.listWorkOrderStatuses()])
        orders.value = o.data.data
        products.value = p.data.data
        technicians.value = t.data.data
        statuses.value = st.data.data
    } catch (e: any) {
        loadError.value = e.response?.data?.error || 'Could not load work orders. Refresh to try again.'
    } finally { loading.value = false }
}

onMounted(() => {
    reload()
    loadTemplates()
    // Advance the running-timer display every 30s (cheap; only matters while a timer runs).
    timeTicker = setInterval(() => { nowMs.value = Date.now() }, 30000)
})
onUnmounted(() => { if (timeTicker) clearInterval(timeTicker) })
</script>

<style scoped>
.line-declined {
    opacity: 0.55;
    text-decoration: line-through;
}
.wo-group-label {
    font-size: 13px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: rgba(var(--v-theme-on-surface), 0.6);
}
</style>
