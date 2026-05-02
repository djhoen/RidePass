<template>
    <v-container>
        <h1 class="text-h4 mb-6">Super Admin</h1>

        <v-tabs v-model="tab" density="compact" class="mb-4">
            <v-tab value="analytics">Analytics</v-tab>
            <v-tab value="tenants">Tenants</v-tab>
            <v-tab value="users">Users</v-tab>
            <v-tab value="refunds">
                Refunds
                <v-badge v-if="refunds.length > 0" :content="refunds.length" color="error" inline class="ml-2"></v-badge>
            </v-tab>
            <v-tab value="disputes">
                Disputes
                <v-badge v-if="openDisputeCount > 0" :content="openDisputeCount" color="error" inline class="ml-2"></v-badge>
            </v-tab>
            <v-tab value="payouts">Payouts</v-tab>
            <v-tab value="audit">Audit log</v-tab>
            <v-tab value="reconcile">Reconcile</v-tab>
        </v-tabs>

        <v-window v-model="tab">
            <!-- ANALYTICS -->
            <v-window-item value="analytics">
                <div class="d-flex align-center mb-4 flex-wrap ga-3">
                    <v-select v-model="analyticsPreset" :items="analyticsPresetOptions" label="Range" density="compact"
                        hide-details style="max-width: 200px" @update:model-value="applyAnalyticsPreset"></v-select>
                    <v-text-field v-model="analyticsFrom" type="date" label="From" density="compact" hide-details
                        style="max-width: 160px" @change="analyticsPreset = 'custom'"></v-text-field>
                    <v-text-field v-model="analyticsTo" type="date" label="To" density="compact" hide-details
                        style="max-width: 160px" @change="analyticsPreset = 'custom'"></v-text-field>
                    <v-spacer></v-spacer>
                    <v-btn color="primary" :loading="loadingAnalytics" @click="loadAnalytics">Refresh</v-btn>
                </div>

                <v-row v-if="analytics" class="mb-4">
                    <v-col cols="12" sm="6" md="3">
                        <v-card><v-card-text>
                            <div class="text-caption text-medium-emphasis">Platform Revenue</div>
                            <div class="text-h4">${{ (analytics.totalRevenueCents / 100).toFixed(2) }}</div>
                        </v-card-text></v-card>
                    </v-col>
                    <v-col cols="12" sm="6" md="3">
                        <v-card><v-card-text>
                            <div class="text-caption text-medium-emphasis">Tenants</div>
                            <div class="text-h4">{{ analytics.activeTenants }} / {{ analytics.totalTenants }}</div>
                            <div class="text-caption text-medium-emphasis">active / total</div>
                        </v-card-text></v-card>
                    </v-col>
                    <v-col cols="12" sm="6" md="3">
                        <v-card><v-card-text>
                            <div class="text-caption text-medium-emphasis">Passes / Tickets</div>
                            <div class="text-h4">{{ analytics.passesSold }} / {{ analytics.ticketsSold }}</div>
                        </v-card-text></v-card>
                    </v-col>
                    <v-col cols="12" sm="6" md="3">
                        <v-card><v-card-text>
                            <div class="text-caption text-medium-emphasis">Refunds / Disputes</div>
                            <div class="text-h4">{{ analytics.refundedCount }} / {{ analytics.disputedCount }}</div>
                        </v-card-text></v-card>
                    </v-col>
                </v-row>

                <v-card class="mb-4" v-if="analytics">
                    <v-card-title>Daily Revenue (UTC)</v-card-title>
                    <v-card-text>
                        <div style="position: relative; height: 320px;">
                            <Line v-if="platformRevenueChart" :data="platformRevenueChart" :options="revenueChartOptions" />
                        </div>
                    </v-card-text>
                </v-card>

                <v-card class="mb-4" v-if="analytics">
                    <v-card-title>Revenue by Tenant</v-card-title>
                    <v-card-text>
                        <div v-if="analytics.tenantBreakdown.length === 0" class="text-medium-emphasis">No tenant sales.</div>
                        <div v-else style="position: relative; height: 320px;">
                            <Bar :data="tenantChart" :options="horizontalBarOptions" />
                        </div>
                    </v-card-text>
                </v-card>

                <v-card v-if="analytics">
                    <v-card-title>Tenant Breakdown</v-card-title>
                    <v-table density="compact">
                        <thead>
                            <tr>
                                <th @click="sortBreakdown('displayName')" style="cursor: pointer">Tenant</th>
                                <th @click="sortBreakdown('subdomain')" style="cursor: pointer">Subdomain</th>
                                <th @click="sortBreakdown('revenueCents')" style="cursor: pointer; width: 140px">Revenue</th>
                                <th @click="sortBreakdown('passesSold')" style="cursor: pointer; width: 100px">Passes</th>
                                <th @click="sortBreakdown('ticketsSold')" style="cursor: pointer; width: 100px">Tickets</th>
                                <th @click="sortBreakdown('refundedCount')" style="cursor: pointer; width: 100px">Refunds</th>
                                <th @click="sortBreakdown('disputedCount')" style="cursor: pointer; width: 100px">Disputes</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="r in sortedBreakdown" :key="r.tenantId">
                                <td>{{ r.displayName }}</td>
                                <td><code>{{ r.subdomain }}</code></td>
                                <td>${{ (r.revenueCents / 100).toFixed(2) }}</td>
                                <td>{{ r.passesSold }}</td>
                                <td>{{ r.ticketsSold }}</td>
                                <td>{{ r.refundedCount }}</td>
                                <td>{{ r.disputedCount }}</td>
                            </tr>
                            <tr v-if="analytics.tenantBreakdown.length === 0">
                                <td colspan="7" class="text-center text-medium-emphasis py-8">No tenants.</td>
                            </tr>
                        </tbody>
                    </v-table>
                </v-card>
            </v-window-item>

            <!-- TENANTS -->
            <v-window-item value="tenants">
                <div class="d-flex align-center mb-3">
                    <v-spacer></v-spacer>
                    <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreateTenant">New Tenant</v-btn>
                </div>
                <v-card>
                    <v-table>
                        <thead>
                            <tr>
                                <th>Subdomain</th>
                                <th>Display Name</th>
                                <th style="width: 120px">Status</th>
                                <th style="width: 160px">Timezone</th>
                                <th style="width: 180px">Created</th>
                                <th style="width: 140px" class="text-right"></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="t in tenants" :key="t.id">
                                <td><code>{{ t.subdomain }}</code></td>
                                <td>{{ t.displayName }}</td>
                                <td>{{ t.status }}</td>
                                <td>{{ t.timezone }}</td>
                                <td>{{ formatDate(t.createdAtUtc) }}</td>
                                <td class="text-right">
                                    <v-btn variant="text" size="small" :href="tenantUrl(t.subdomain)" target="_blank">Visit</v-btn>
                                </td>
                            </tr>
                            <tr v-if="!loadingTenants && tenants.length === 0">
                                <td colspan="6" class="text-center text-medium-emphasis py-8">No tenants yet.</td>
                            </tr>
                        </tbody>
                    </v-table>
                </v-card>
            </v-window-item>

            <!-- USERS -->
            <v-window-item value="users">
                <div class="d-flex align-center mb-3 ga-2">
                    <v-text-field v-model="userQuery" label="Search users" density="compact" hide-details clearable
                        style="max-width: 360px" @keyup.enter="loadUsers"></v-text-field>
                    <v-btn @click="loadUsers">Search</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn color="primary" prepend-icon="mdi-shield-plus" @click="openCreateSuperAdmin">Add Super Admin</v-btn>
                </div>
                <v-card>
                    <v-table>
                        <thead>
                            <tr>
                                <th>Name</th>
                                <th>Email</th>
                                <th style="width: 130px">Role</th>
                                <th style="width: 160px">Tenant</th>
                                <th style="width: 120px">Status</th>
                                <th style="width: 140px" class="text-right"></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="u in users" :key="u.id">
                                <td>{{ u.firstName }} {{ u.lastName }}</td>
                                <td>{{ u.email }}</td>
                                <td>{{ u.role }}</td>
                                <td>
                                    <code v-if="u.tenantSubdomain">{{ u.tenantSubdomain }}</code>
                                    <span v-else class="text-medium-emphasis">— global —</span>
                                </td>
                                <td>{{ u.status }}</td>
                                <td class="text-right">
                                    <v-btn v-if="u.role !== 'super_admin'" variant="text" size="small"
                                        :loading="impersonatingId === u.id" @click="startImpersonation(u)">
                                        Impersonate
                                    </v-btn>
                                </td>
                            </tr>
                            <tr v-if="!loadingUsers && users.length === 0">
                                <td colspan="6" class="text-center text-medium-emphasis py-8">No users match.</td>
                            </tr>
                        </tbody>
                    </v-table>
                </v-card>
            </v-window-item>

            <!-- REFUNDS -->
            <v-window-item value="refunds">
                <div class="d-flex align-center mb-3">
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="loadRefunds">Refresh</v-btn>
                </div>
                <v-card>
                    <v-table>
                        <thead>
                            <tr>
                                <th style="width: 100px">Kind</th>
                                <th style="width: 160px">Tenant</th>
                                <th>Item</th>
                                <th>Purchaser</th>
                                <th style="width: 110px">Amount</th>
                                <th style="width: 180px">Cancelled</th>
                                <th>Reason</th>
                                <th style="width: 160px" class="text-right"></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="r in refunds" :key="r.kind + ':' + r.id">
                                <td>
                                    <v-chip size="x-small" :color="r.kind === 'day_pass' ? 'primary' : 'secondary'">
                                        {{ r.kind === 'day_pass' ? 'Day Pass' : 'Ticket' }}
                                    </v-chip>
                                </td>
                                <td><code>{{ r.tenantSubdomain }}</code></td>
                                <td>{{ r.itemName }}</td>
                                <td>
                                    <div>{{ r.purchaserName }}</div>
                                    <div class="text-caption text-medium-emphasis">{{ r.purchaserEmail }}</div>
                                </td>
                                <td>${{ (r.amountCents / 100).toFixed(2) }}</td>
                                <td>{{ r.cancelledAtUtc ? formatDate(r.cancelledAtUtc) : '—' }}</td>
                                <td>
                                    <span v-if="r.cancellationReason">{{ r.cancellationReason }}</span>
                                    <span v-else class="text-medium-emphasis">—</span>
                                </td>
                                <td class="text-right">
                                    <v-btn size="small" color="primary" variant="tonal"
                                        :disabled="!r.stripePaymentIntentId" :loading="processingId === r.id"
                                        @click="processRefund(r)">
                                        Process Refund
                                    </v-btn>
                                </td>
                            </tr>
                            <tr v-if="!loadingRefunds && refunds.length === 0">
                                <td colspan="8" class="text-center text-medium-emphasis py-8">No refund requests pending.</td>
                            </tr>
                        </tbody>
                    </v-table>
                </v-card>
            </v-window-item>

            <!-- DISPUTES -->
            <v-window-item value="disputes">
                <div class="d-flex align-center mb-3">
                    <p class="text-caption text-medium-emphasis mb-0 mr-4">
                        Chargebacks and inquiries captured from Stripe webhooks. Submit evidence in the Stripe Dashboard.
                    </p>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="loadDisputes">Refresh</v-btn>
                </div>
                <v-card>
                    <v-table>
                        <thead>
                            <tr>
                                <th style="width: 100px">Kind</th>
                                <th style="width: 160px">Tenant</th>
                                <th>Item</th>
                                <th>Purchaser</th>
                                <th style="width: 110px">Amount</th>
                                <th style="width: 140px">Reason</th>
                                <th style="width: 180px">Status</th>
                                <th style="width: 180px">Evidence Due</th>
                                <th style="width: 120px" class="text-right"></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="d in disputes" :key="d.id">
                                <td>
                                    <v-chip v-if="d.kind !== 'unlinked'" size="x-small"
                                        :color="d.kind === 'day_pass' ? 'primary' : 'secondary'">
                                        {{ d.kind === 'day_pass' ? 'Day Pass' : 'Ticket' }}
                                    </v-chip>
                                    <v-chip v-else size="x-small" color="grey">Unlinked</v-chip>
                                </td>
                                <td><code>{{ d.tenantSubdomain }}</code></td>
                                <td>{{ d.itemName || '—' }}</td>
                                <td>
                                    <div>{{ d.purchaserName || '—' }}</div>
                                    <div class="text-caption text-medium-emphasis">{{ d.purchaserEmail }}</div>
                                </td>
                                <td>${{ (d.amountCents / 100).toFixed(2) }} {{ d.currency.toUpperCase() }}</td>
                                <td>{{ d.reason || '—' }}</td>
                                <td>
                                    <v-chip size="small" :color="disputeStatusColor(d.status)">{{ d.status }}</v-chip>
                                </td>
                                <td>
                                    <span v-if="d.evidenceDueByUtc" :class="evidenceDueClass(d.evidenceDueByUtc)">
                                        {{ formatDate(d.evidenceDueByUtc) }}
                                    </span>
                                    <span v-else class="text-medium-emphasis">—</span>
                                </td>
                                <td class="text-right">
                                    <v-btn size="small" variant="tonal" :href="stripeDisputeUrl(d.stripeDisputeId)"
                                        target="_blank" rel="noopener">
                                        Open in Stripe
                                    </v-btn>
                                </td>
                            </tr>
                            <tr v-if="!loadingDisputes && disputes.length === 0">
                                <td colspan="9" class="text-center text-medium-emphasis py-8">No disputes on record.</td>
                            </tr>
                        </tbody>
                    </v-table>
                </v-card>
            </v-window-item>

            <!-- PAYOUTS -->
            <v-window-item value="payouts">
                <div class="d-flex align-center mb-3">
                    <p class="text-caption text-medium-emphasis mb-0 mr-4">
                        Per-tenant available balance and lifetime totals. Click a row for ledger detail and fee schedule.
                    </p>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="loadBalances">Refresh</v-btn>
                </div>
                <v-card>
                    <v-table>
                        <thead>
                            <tr>
                                <th>Tenant</th>
                                <th class="text-right" style="width: 130px">Available</th>
                                <th class="text-right" style="width: 130px">This month</th>
                                <th class="text-right" style="width: 130px">Lifetime gross</th>
                                <th class="text-right" style="width: 110px">Stripe fees</th>
                                <th class="text-right" style="width: 130px">RidePass cut</th>
                                <th class="text-right" style="width: 130px">Paid out</th>
                                <th style="width: 200px" class="text-right"></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="b in balances" :key="b.tenantId">
                                <td>
                                    <strong>{{ b.tenantDisplayName }}</strong>
                                    <div class="text-caption text-medium-emphasis"><code>{{ b.tenantSubdomain }}</code></div>
                                </td>
                                <td class="text-right">${{ (b.availableBalanceCents / 100).toFixed(2) }}</td>
                                <td class="text-right">${{ (b.currentMonthGrossCents / 100).toFixed(2) }}</td>
                                <td class="text-right">${{ (b.lifetimeGrossCents / 100).toFixed(2) }}</td>
                                <td class="text-right">${{ (b.lifetimeStripeFeeCents / 100).toFixed(2) }}</td>
                                <td class="text-right">${{ (b.lifetimeRidepassCutCents / 100).toFixed(2) }}</td>
                                <td class="text-right">${{ (b.lifetimePaidOutCents / 100).toFixed(2) }}</td>
                                <td class="text-right">
                                    <v-btn variant="text" size="small" @click="openTenantDetail(b)">Detail</v-btn>
                                    <v-btn variant="text" size="small" @click="openFeeEditor(b)">Fees</v-btn>
                                </td>
                            </tr>
                            <tr v-if="!loadingBalances && balances.length === 0">
                                <td colspan="8" class="text-center text-medium-emphasis py-8">No tenants yet.</td>
                            </tr>
                        </tbody>
                    </v-table>
                </v-card>
            </v-window-item>

            <!-- AUDIT LOG -->
            <v-window-item value="audit">
                <div class="d-flex align-center mb-3 ga-2">
                    <v-text-field v-model="auditFilterAction" label="Action filter" placeholder="e.g. payout.create"
                        density="compact" hide-details clearable style="max-width: 280px"></v-text-field>
                    <v-btn @click="loadAuditLog">Apply</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="loadAuditLog">Refresh</v-btn>
                </div>
                <v-card>
                    <v-table density="compact">
                        <thead>
                            <tr>
                                <th style="width: 180px">When (UTC)</th>
                                <th style="width: 180px">Actor</th>
                                <th style="width: 200px">Action</th>
                                <th style="width: 140px">Target</th>
                                <th>Summary</th>
                                <th style="width: 130px">IP</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="a in auditLog" :key="a.id">
                                <td>{{ formatDate(a.createdAt) }}</td>
                                <td>
                                    <div>{{ a.actorEmail || '—' }}</div>
                                    <div class="text-caption text-medium-emphasis">{{ a.actorRole }}</div>
                                </td>
                                <td><code>{{ a.action }}</code></td>
                                <td>
                                    <span v-if="a.targetKind">{{ a.targetKind }}</span>
                                    <span v-else class="text-medium-emphasis">—</span>
                                </td>
                                <td>{{ a.summary }}</td>
                                <td>{{ a.ipAddress || '—' }}</td>
                            </tr>
                            <tr v-if="!loadingAudit && auditLog.length === 0">
                                <td colspan="6" class="text-center text-medium-emphasis py-8">No audit entries yet.</td>
                            </tr>
                        </tbody>
                    </v-table>
                </v-card>
            </v-window-item>

            <!-- RECONCILE -->
            <v-window-item value="reconcile">
                <div class="d-flex align-center mb-3 flex-wrap ga-3">
                    <v-text-field v-model="reconcileFrom" type="date" label="From (UTC)" density="compact" hide-details
                        style="max-width: 180px"></v-text-field>
                    <v-text-field v-model="reconcileTo" type="date" label="To (UTC, exclusive)" density="compact" hide-details
                        style="max-width: 180px"></v-text-field>
                    <v-btn color="primary" :loading="loadingReconcile" @click="loadReconciliation">Run</v-btn>
                    <v-spacer></v-spacer>
                    <p class="text-caption text-medium-emphasis mb-0">
                        Compares Stripe balance_transactions against our ledger for the chosen period.
                    </p>
                </div>

                <v-alert v-if="reconciliation && !reconciliation.stripeConfigured" type="warning" variant="tonal" class="mb-4">
                    Stripe credentials are not configured on the server, so the Stripe column is unavailable.
                    Ledger totals are still shown.
                </v-alert>

                <v-row v-if="reconciliation" class="mb-4">
                    <v-col cols="12" md="6">
                        <v-card>
                            <v-card-title>Stripe (balance_transactions)</v-card-title>
                            <v-card-text>
                                <div v-if="!reconciliation.stripe" class="text-medium-emphasis">Not available.</div>
                                <v-table v-else density="compact">
                                    <tbody>
                                        <tr><td>Transactions</td><td class="text-right">{{ reconciliation.stripe.count }}</td></tr>
                                        <tr><td>Gross</td><td class="text-right">${{ (reconciliation.stripe.grossCents / 100).toFixed(2) }}</td></tr>
                                        <tr><td>Stripe fee</td><td class="text-right">${{ (reconciliation.stripe.feeCents / 100).toFixed(2) }}</td></tr>
                                        <tr><td>Net to platform</td><td class="text-right"><strong>${{ (reconciliation.stripe.netCents / 100).toFixed(2) }}</strong></td></tr>
                                    </tbody>
                                </v-table>
                            </v-card-text>
                        </v-card>
                    </v-col>
                    <v-col cols="12" md="6">
                        <v-card>
                            <v-card-title>RidePass ledger</v-card-title>
                            <v-card-text>
                                <v-table density="compact">
                                    <tbody>
                                        <tr><td>Entries</td><td class="text-right">{{ reconciliation.ledger.count }}</td></tr>
                                        <tr><td>Gross</td><td class="text-right">${{ (reconciliation.ledger.grossCents / 100).toFixed(2) }}</td></tr>
                                        <tr><td>Stripe fee (recorded)</td><td class="text-right">${{ (reconciliation.ledger.stripeFeeCents / 100).toFixed(2) }}</td></tr>
                                        <tr><td>RidePass cut</td><td class="text-right">${{ (reconciliation.ledger.ridepassCutCents / 100).toFixed(2) }}</td></tr>
                                        <tr><td>Net to tenants</td><td class="text-right"><strong>${{ (reconciliation.ledger.netToTenantCents / 100).toFixed(2) }}</strong></td></tr>
                                    </tbody>
                                </v-table>
                            </v-card-text>
                        </v-card>
                    </v-col>
                </v-row>

                <v-card v-if="reconciliation && reconciliation.stripe">
                    <v-card-title>Gap analysis</v-card-title>
                    <v-card-text>
                        <p class="text-caption text-medium-emphasis mb-3">
                            Non-zero gaps mean Stripe and our ledger disagree for the period. A small fee gap is normal
                            (we estimate Stripe's fee at sale time; the actual fee can drift by a few cents).
                            Large or persistent gaps deserve investigation.
                        </p>
                        <v-table density="compact">
                            <thead>
                                <tr>
                                    <th>Metric</th>
                                    <th class="text-right">Stripe</th>
                                    <th class="text-right">Ledger</th>
                                    <th class="text-right">Gap (Stripe − Ledger)</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr>
                                    <td>Gross</td>
                                    <td class="text-right">${{ (reconciliation.stripe.grossCents / 100).toFixed(2) }}</td>
                                    <td class="text-right">${{ (reconciliation.ledger.grossCents / 100).toFixed(2) }}</td>
                                    <td class="text-right" :class="gapClass(reconciliation.gaps.grossGap)">
                                        ${{ (reconciliation.gaps.grossGap / 100).toFixed(2) }}
                                    </td>
                                </tr>
                                <tr>
                                    <td>Stripe fee</td>
                                    <td class="text-right">${{ (reconciliation.stripe.feeCents / 100).toFixed(2) }}</td>
                                    <td class="text-right">${{ (reconciliation.ledger.stripeFeeCents / 100).toFixed(2) }}</td>
                                    <td class="text-right" :class="gapClass(reconciliation.gaps.feeGap)">
                                        ${{ (reconciliation.gaps.feeGap / 100).toFixed(2) }}
                                    </td>
                                </tr>
                                <tr>
                                    <td>Net (Stripe net vs. our gross − recorded fee)</td>
                                    <td class="text-right">${{ (reconciliation.stripe.netCents / 100).toFixed(2) }}</td>
                                    <td class="text-right">${{ (reconciliation.gaps.expectedStripeNet / 100).toFixed(2) }}</td>
                                    <td class="text-right" :class="gapClass(reconciliation.gaps.netGap)">
                                        ${{ (reconciliation.gaps.netGap / 100).toFixed(2) }}
                                    </td>
                                </tr>
                            </tbody>
                        </v-table>
                    </v-card-text>
                </v-card>
            </v-window-item>
        </v-window>

        <!-- Tenant detail dialog: payouts + ledger -->
        <v-dialog v-model="detailDialog" max-width="980" scrollable>
            <v-card v-if="detailTenant">
                <v-card-title>
                    {{ detailTenant.tenantDisplayName }}
                    <span class="text-medium-emphasis ml-2 text-body-2"><code>{{ detailTenant.tenantSubdomain }}</code></span>
                </v-card-title>
                <v-card-text style="max-height: 70vh">
                    <div class="d-flex align-center mb-2">
                        <div class="text-subtitle-2">Past payouts</div>
                        <v-spacer></v-spacer>
                        <v-btn size="small" color="primary" prepend-icon="mdi-plus" @click="openCreatePayout">
                            New payout
                        </v-btn>
                    </div>
                    <v-table density="compact" class="mb-4">
                        <thead>
                            <tr>
                                <th>Period</th>
                                <th>Status</th>
                                <th class="text-right">Net paid</th>
                                <th>Reference</th>
                                <th>Date paid</th>
                                <th style="width: 180px" class="text-right"></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="p in detailPayouts" :key="p.id">
                                <td>{{ formatDateOnly(p.periodStartUtc) }} – {{ formatDateOnly(p.periodEndUtc) }}</td>
                                <td>
                                    <v-chip size="x-small" :color="payoutStatusColor(p.status)">{{ p.status }}</v-chip>
                                </td>
                                <td class="text-right">${{ (p.netPaidCents / 100).toFixed(2) }}</td>
                                <td><code v-if="p.externalReference">{{ p.externalReference }}</code><span v-else class="text-medium-emphasis">—</span></td>
                                <td>{{ p.payoutDateUtc ? formatDate(p.payoutDateUtc) : '—' }}</td>
                                <td class="text-right">
                                    <v-btn v-if="p.status === 'pending' || p.status === 'processing'" size="x-small" variant="text"
                                        @click="openMarkPaid(p)">Mark paid</v-btn>
                                    <v-btn v-if="p.status === 'pending'" size="x-small" variant="text" color="error"
                                        @click="voidPayout(p)">Void</v-btn>
                                    <v-btn size="x-small" variant="text" icon="mdi-download" @click="downloadPayoutCsv(p)"
                                        :title="'Download CSV'"></v-btn>
                                </td>
                            </tr>
                            <tr v-if="detailPayouts.length === 0">
                                <td colspan="6" class="text-center text-medium-emphasis py-4">No payouts yet.</td>
                            </tr>
                        </tbody>
                    </v-table>

                    <div class="text-subtitle-2 mb-2">Recent transactions (last 200)</div>
                    <v-table density="compact">
                        <thead>
                            <tr>
                                <th>Date</th>
                                <th>Kind</th>
                                <th class="text-right">Gross</th>
                                <th class="text-right">Stripe</th>
                                <th class="text-right">RidePass</th>
                                <th class="text-right">Net</th>
                                <th>Payout</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="e in detailLedger" :key="e.id">
                                <td>{{ formatDate(e.occurredAtUtc) }}</td>
                                <td>
                                    <v-chip size="x-small" :color="e.entryKind === 'sale' ? 'primary' : 'warning'">
                                        {{ e.entryKind }}<span v-if="e.sourceKind"> · {{ e.sourceKind }}</span>
                                    </v-chip>
                                </td>
                                <td class="text-right">${{ (e.grossCents / 100).toFixed(2) }}</td>
                                <td class="text-right">${{ (e.stripeFeeCents / 100).toFixed(2) }}</td>
                                <td class="text-right">${{ (e.ridepassCutCents / 100).toFixed(2) }}</td>
                                <td class="text-right"><strong>${{ (e.netToTenantCents / 100).toFixed(2) }}</strong></td>
                                <td>
                                    <span v-if="e.payoutId" class="text-success text-caption">paid out</span>
                                    <span v-else class="text-warning text-caption">pending</span>
                                </td>
                            </tr>
                            <tr v-if="detailLedger.length === 0">
                                <td colspan="7" class="text-center text-medium-emphasis py-4">No transactions yet.</td>
                            </tr>
                        </tbody>
                    </v-table>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="detailDialog = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Create payout dialog -->
        <v-dialog v-model="createPayoutDialog" max-width="560" persistent>
            <v-card v-if="detailTenant">
                <v-card-title>Create payout — {{ detailTenant.tenantDisplayName }}</v-card-title>
                <v-card-text>
                    <p class="text-caption text-medium-emphasis mb-3">
                        All unpaid ledger entries with <code>occurred_at_utc</code> in the period will be batched into this payout.
                    </p>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="payoutPeriodStart" type="date" label="Period start (UTC)" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="payoutPeriodEnd" type="date" label="Period end (UTC, exclusive)" density="compact"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-text-field v-model="payoutMemo" label="Memo (optional)" density="compact"></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="createPayoutDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="creatingPayout" @click="submitCreatePayout">Create</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Mark paid dialog -->
        <v-dialog v-model="markPaidDialog" max-width="520" persistent>
            <v-card v-if="markPaidTarget">
                <v-card-title>Mark payout as paid</v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-3">
                        Net to pay: <strong>${{ (markPaidTarget.netPaidCents / 100).toFixed(2) }}</strong>
                    </p>
                    <v-text-field v-model="markPaidDate" type="date" label="Payout date (UTC)" density="compact"></v-text-field>
                    <v-text-field v-model="markPaidReference" label="External reference (transfer id / ACH trace / check #)" density="compact"></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="markPaidDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="markingPaid" @click="submitMarkPaid">Mark paid</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Fee schedule editor -->
        <v-dialog v-model="feeDialog" max-width="720" persistent>
            <v-card v-if="feeTenant">
                <v-card-title>Fee schedule — {{ feeTenant.tenantDisplayName }}</v-card-title>
                <v-card-text>
                    <p class="text-caption text-medium-emphasis mb-3">
                        Tiered RidePass cut by monthly cumulative gross volume (UTC). New schedule takes effect immediately;
                        past transactions keep the rate they were charged.
                    </p>
                    <v-text-field v-model.number="feeMonthlyCapDollars" type="number" min="0"
                        label="Monthly cap (USD, blank = no cap)" density="compact" class="mb-3"
                        hint="RidePass take is limited to this much per UTC month per tenant" persistent-hint></v-text-field>

                    <v-table density="compact">
                        <thead>
                            <tr>
                                <th>From (USD/mo)</th>
                                <th>To (USD/mo, blank = ∞)</th>
                                <th>Rate (%)</th>
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="(t, i) in feeTiers" :key="i">
                                <td><v-text-field v-model.number="t.minDollars" type="number" density="compact" hide-details></v-text-field></td>
                                <td><v-text-field v-model.number="t.maxDollars" type="number" density="compact" hide-details placeholder="∞"></v-text-field></td>
                                <td><v-text-field v-model.number="t.ratePct" type="number" step="0.01" density="compact" hide-details></v-text-field></td>
                                <td>
                                    <v-btn icon="mdi-delete" size="x-small" variant="text" :disabled="feeTiers.length === 1"
                                        @click="feeTiers.splice(i, 1)"></v-btn>
                                </td>
                            </tr>
                        </tbody>
                    </v-table>
                    <v-btn variant="text" prepend-icon="mdi-plus" class="mt-2" @click="feeTiers.push({ minDollars: 0, maxDollars: null, ratePct: 5 })">
                        Add tier
                    </v-btn>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="feeDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="savingFee" @click="saveFeeSchedule">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Create tenant dialog -->
        <v-dialog v-model="createDialog" max-width="640" persistent>
            <v-card>
                <v-card-title>New Tenant</v-card-title>
                <v-card-text>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="createForm.subdomain" label="Subdomain" density="compact"
                                hint="lowercase, digits, hyphens" persistent-hint></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-autocomplete v-model="createForm.timezone" :items="timezoneOptions"
                                item-title="title" item-value="value" label="Timezone" density="compact"></v-autocomplete>
                        </v-col>
                    </v-row>
                    <v-text-field v-model="createForm.displayName" label="Display Name" density="compact"></v-text-field>
                    <v-divider class="my-3"></v-divider>
                    <div class="text-subtitle-2 mb-1">Optional: first tenant admin</div>
                    <p class="text-caption text-medium-emphasis mb-3">
                        Leave blank to skip. A temporary password is generated and shown once.
                    </p>
                    <v-row>
                        <v-col cols="12" md="4">
                            <v-text-field v-model="createForm.adminFirstName" label="First name" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="4">
                            <v-text-field v-model="createForm.adminLastName" label="Last name" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="4">
                            <v-text-field v-model="createForm.adminEmail" type="email" label="Email" density="compact"></v-text-field>
                        </v-col>
                    </v-row>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="createDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="creating" @click="submitCreateTenant">Create</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Add super admin dialog -->
        <v-dialog v-model="superAdminDialog" max-width="560" persistent>
            <v-card>
                <v-card-title>Add Super Admin</v-card-title>
                <v-card-text>
                    <p class="text-caption text-medium-emphasis mb-3">
                        The new super admin will have full platform-level access. They sign in at
                        <code>https://ridepass.io/Login</code> with the password you set here.
                    </p>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="superAdminForm.firstName" label="First name" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="superAdminForm.lastName" label="Last name" density="compact"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-text-field v-model="superAdminForm.email" type="email" label="Email" density="compact" class="mb-2"></v-text-field>
                    <v-text-field v-model="superAdminForm.password" type="password" label="Password (min 8 chars)" density="compact"></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="superAdminDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="creatingSuperAdmin" @click="submitCreateSuperAdmin">Create</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- One-time credential reveal -->
        <v-dialog v-model="credsDialog" max-width="560" persistent>
            <v-card>
                <v-card-title>Tenant created</v-card-title>
                <v-card-text>
                    <p class="mb-3">
                        <strong>{{ createdResult?.displayName }}</strong>
                        (<code>{{ createdResult?.subdomain }}</code>)
                        is live.
                    </p>
                    <template v-if="createdResult?.adminTemporaryPassword">
                        <v-alert type="warning" variant="tonal" class="mb-3">
                            This is the only time the admin password is shown. Copy it now.
                        </v-alert>
                        <div class="text-body-2 mb-1"><strong>Email:</strong> {{ createdResult.adminEmail }}</div>
                        <div class="text-body-2 mb-1">
                            <strong>Temporary Password:</strong>
                            <code>{{ createdResult.adminTemporaryPassword }}</code>
                        </div>
                    </template>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn color="primary" @click="credsDialog = false">Done</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { SuperAdminService, type TenantSummary, type SuperAdminUser, type CreateTenantResult, type RefundListItem, type DisputeListItem,
    type TenantBalanceSummary, type LedgerEntry, type PayoutSummary, type AuditLogEntry, type ReconciliationResult } from '@/services/SuperAdminService'
import { ReportsService, type PlatformAnalyticsSummary, type TenantBreakdownRow } from '@/services/ReportsService'
import { computed } from 'vue'
import { Line, Bar } from 'vue-chartjs'
import { registerChartJs } from '@/helpers/ChartSetup'

registerChartJs()
import authHelper from '@/helpers/AuthHelper'

const router = useRouter()
const service = new SuperAdminService()
const reportsService = new ReportsService()

const tab = ref('analytics')

const tenants = ref<TenantSummary[]>([])
const loadingTenants = ref(false)

const users = ref<SuperAdminUser[]>([])
const loadingUsers = ref(false)
const userQuery = ref('')
const impersonatingId = ref<string | null>(null)

const createDialog = ref(false)
const creating = ref(false)
const createForm = ref({
    subdomain: '',
    displayName: '',
    timezone: 'America/New_York',
    adminFirstName: '',
    adminLastName: '',
    adminEmail: '',
})

const credsDialog = ref(false)
const createdResult = ref<CreateTenantResult | null>(null)

const superAdminDialog = ref(false)
const creatingSuperAdmin = ref(false)
const superAdminForm = ref({ firstName: '', lastName: '', email: '', password: '' })

// Payouts tab state
const balances = ref<TenantBalanceSummary[]>([])
const loadingBalances = ref(false)
const detailDialog = ref(false)
const detailTenant = ref<TenantBalanceSummary | null>(null)
const detailPayouts = ref<PayoutSummary[]>([])
const detailLedger = ref<LedgerEntry[]>([])
const feeDialog = ref(false)
const feeTenant = ref<TenantBalanceSummary | null>(null)
const feeMonthlyCapDollars = ref<number | null>(null)
const feeTiers = ref<{ minDollars: number; maxDollars: number | null; ratePct: number }[]>([])
const savingFee = ref(false)

const createPayoutDialog = ref(false)
const creatingPayout = ref(false)
const payoutPeriodStart = ref(dayjs.utc().startOf('month').subtract(1, 'month').format('YYYY-MM-DD'))
const payoutPeriodEnd = ref(dayjs.utc().startOf('month').format('YYYY-MM-DD'))
const payoutMemo = ref('')

const markPaidDialog = ref(false)
const markingPaid = ref(false)
const markPaidTarget = ref<PayoutSummary | null>(null)
const markPaidDate = ref(dayjs.utc().format('YYYY-MM-DD'))
const markPaidReference = ref('')

const auditLog = ref<AuditLogEntry[]>([])
const loadingAudit = ref(false)
const auditFilterAction = ref('')

const reconciliation = ref<ReconciliationResult | null>(null)
const loadingReconcile = ref(false)
const reconcileFrom = ref(dayjs.utc().startOf('month').subtract(1, 'month').format('YYYY-MM-DD'))
const reconcileTo = ref(dayjs.utc().startOf('month').format('YYYY-MM-DD'))

function gapClass(cents: number): string {
    if (cents === 0) return 'text-success'
    if (Math.abs(cents) < 100) return 'text-warning'
    return 'text-error'
}

async function loadReconciliation() {
    loadingReconcile.value = true
    try {
        const fromUtc = dayjs.utc(reconcileFrom.value).toISOString()
        const toUtc = dayjs.utc(reconcileTo.value).toISOString()
        const r = await service.getReconciliation(fromUtc, toUtc)
        reconciliation.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load reconciliation.', 'error')
    } finally {
        loadingReconcile.value = false
    }
}

function formatDateOnly(utc: string): string {
    return dayjs.utc(utc).format('YYYY-MM-DD')
}

function payoutStatusColor(s: string): string {
    switch (s) {
        case 'paid': return 'success'
        case 'pending': return 'warning'
        case 'processing': return 'info'
        case 'failed': return 'error'
        case 'on_hold': return 'grey'
        default: return 'default'
    }
}

const refunds = ref<RefundListItem[]>([])
const loadingRefunds = ref(false)
const processingId = ref<string | null>(null)

const disputes = ref<DisputeListItem[]>([])
const loadingDisputes = ref(false)
const openDisputeCount = computed(() => disputes.value.filter(d => isOpenDispute(d.status)).length)

const analytics = ref<PlatformAnalyticsSummary | null>(null)
const loadingAnalytics = ref(false)
const analyticsPresetOptions = [
    { title: 'Last 7 days', value: '7d' },
    { title: 'Last 30 days', value: '30d' },
    { title: 'This month', value: 'thismonth' },
    { title: 'Year to date', value: 'ytd' },
    { title: 'Custom', value: 'custom' },
]
const analyticsPreset = ref('30d')
const _today = dayjs()
const analyticsFrom = ref(_today.subtract(29, 'day').format('YYYY-MM-DD'))
const analyticsTo = ref(_today.add(1, 'day').format('YYYY-MM-DD'))

const breakdownSortKey = ref<keyof TenantBreakdownRow>('revenueCents')
const breakdownSortDir = ref<'asc' | 'desc'>('desc')

// US IANA timezones — covers all 50 states plus Arizona (no DST).
const timezoneOptions = [
    { title: 'Eastern (New York)', value: 'America/New_York' },
    { title: 'Central (Chicago)', value: 'America/Chicago' },
    { title: 'Mountain (Denver)', value: 'America/Denver' },
    { title: 'Mountain — no DST (Phoenix)', value: 'America/Phoenix' },
    { title: 'Pacific (Los Angeles)', value: 'America/Los_Angeles' },
    { title: 'Alaska (Anchorage)', value: 'America/Anchorage' },
    { title: 'Hawaii–Aleutian (Honolulu)', value: 'Pacific/Honolulu' },
]

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(async () => {
    await loadAnalytics()
    await loadTenants()
    await loadUsers()
    await loadRefunds()
    await loadDisputes()
    await loadBalances()
    await loadAuditLog()
})

async function loadAuditLog() {
    loadingAudit.value = true
    try {
        const r = await service.listAuditLog({
            action: auditFilterAction.value || undefined,
            take: 200,
        })
        auditLog.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load audit log.', 'error')
    } finally {
        loadingAudit.value = false
    }
}

async function loadBalances() {
    loadingBalances.value = true
    try {
        const r = await service.listBalances()
        balances.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load balances.', 'error')
    } finally {
        loadingBalances.value = false
    }
}

async function openTenantDetail(b: TenantBalanceSummary) {
    detailTenant.value = b
    detailPayouts.value = []
    detailLedger.value = []
    detailDialog.value = true
    try {
        const [p, l] = await Promise.all([
            service.listPayouts(b.tenantId),
            service.listLedger(b.tenantId, undefined, undefined, 200),
        ])
        detailPayouts.value = (p.data as any).data
        detailLedger.value = (l.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load tenant detail.', 'error')
    }
}

async function openFeeEditor(b: TenantBalanceSummary) {
    feeTenant.value = b
    feeDialog.value = true
    feeTiers.value = [{ minDollars: 0, maxDollars: null, ratePct: 5 }]
    feeMonthlyCapDollars.value = null
    try {
        const r = await service.getFeeSchedule(b.tenantId)
        const data = (r.data as any).data
        feeMonthlyCapDollars.value = data.schedule.monthlyCapCents !== null ? data.schedule.monthlyCapCents / 100 : null
        feeTiers.value = data.tiers.map((t: any) => ({
            minDollars: t.minVolumeCents / 100,
            maxDollars: t.maxVolumeCents !== null ? t.maxVolumeCents / 100 : null,
            ratePct: t.rateBps / 100,
        }))
    } catch {
        // 404 or other — leave defaults
    }
}

function openCreatePayout() {
    payoutMemo.value = ''
    createPayoutDialog.value = true
}

async function submitCreatePayout() {
    if (!detailTenant.value) return
    creatingPayout.value = true
    try {
        const r = await service.createPayout(detailTenant.value.tenantId, {
            periodStartUtc: dayjs.utc(payoutPeriodStart.value).toISOString(),
            periodEndUtc: dayjs.utc(payoutPeriodEnd.value).toISOString(),
            memo: payoutMemo.value || null,
        })
        const data = (r.data as any).data
        flash(`Payout created with ${data.attachedCount} ledger entr${data.attachedCount === 1 ? 'y' : 'ies'} attached.`, 'success')
        createPayoutDialog.value = false
        await refreshDetailDialog()
        await loadBalances()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to create payout.', 'error')
    } finally {
        creatingPayout.value = false
    }
}

function openMarkPaid(p: PayoutSummary) {
    markPaidTarget.value = p
    markPaidDate.value = dayjs.utc().format('YYYY-MM-DD')
    markPaidReference.value = p.externalReference ?? ''
    markPaidDialog.value = true
}

async function submitMarkPaid() {
    if (!markPaidTarget.value || !detailTenant.value) return
    markingPaid.value = true
    try {
        await service.updatePayoutStatus(detailTenant.value.tenantId, markPaidTarget.value.id, {
            status: 'paid',
            payoutDateUtc: dayjs.utc(markPaidDate.value).toISOString(),
            externalReference: markPaidReference.value || null,
        })
        flash('Marked as paid.', 'success')
        markPaidDialog.value = false
        await refreshDetailDialog()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to update payout.', 'error')
    } finally {
        markingPaid.value = false
    }
}

async function voidPayout(p: PayoutSummary) {
    if (!detailTenant.value) return
    if (!confirm(`Void this pending payout? Attached ledger entries will become unpaid again.`)) return
    try {
        await service.voidPayout(detailTenant.value.tenantId, p.id)
        flash('Payout voided.', 'success')
        await refreshDetailDialog()
        await loadBalances()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to void payout.', 'error')
    }
}

async function downloadPayoutCsv(p: PayoutSummary) {
    if (!detailTenant.value) return
    try {
        await service.downloadPayoutCsv(detailTenant.value.tenantId, p.id)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to download CSV.', 'error')
    }
}

async function refreshDetailDialog() {
    if (!detailTenant.value) return
    const [p, l] = await Promise.all([
        service.listPayouts(detailTenant.value.tenantId),
        service.listLedger(detailTenant.value.tenantId, undefined, undefined, 200),
    ])
    detailPayouts.value = (p.data as any).data
    detailLedger.value = (l.data as any).data
}

async function saveFeeSchedule() {
    if (!feeTenant.value) return
    savingFee.value = true
    try {
        await service.updateFeeSchedule(feeTenant.value.tenantId, {
            monthlyCapCents: feeMonthlyCapDollars.value !== null && !Number.isNaN(feeMonthlyCapDollars.value)
                ? Math.round(feeMonthlyCapDollars.value * 100) : null,
            tiers: feeTiers.value.map(t => ({
                minVolumeCents: Math.round((t.minDollars || 0) * 100),
                maxVolumeCents: t.maxDollars !== null && !Number.isNaN(t.maxDollars) ? Math.round(t.maxDollars * 100) : null,
                rateBps: Math.round((t.ratePct || 0) * 100),
            })),
        })
        flash('Fee schedule saved.', 'success')
        feeDialog.value = false
        await loadBalances()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to save fee schedule.', 'error')
    } finally {
        savingFee.value = false
    }
}

async function loadTenants() {
    loadingTenants.value = true
    try {
        const r = await service.listTenants()
        tenants.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load tenants.', 'error')
    } finally {
        loadingTenants.value = false
    }
}

async function loadUsers() {
    loadingUsers.value = true
    try {
        const r = await service.listUsers(userQuery.value || undefined)
        users.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load users.', 'error')
    } finally {
        loadingUsers.value = false
    }
}

async function loadRefunds() {
    loadingRefunds.value = true
    try {
        const r = await service.listRefunds()
        refunds.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load refunds.', 'error')
    } finally {
        loadingRefunds.value = false
    }
}

function applyAnalyticsPreset(v: string) {
    const t = dayjs()
    switch (v) {
        case '7d':
            analyticsFrom.value = t.subtract(6, 'day').format('YYYY-MM-DD')
            analyticsTo.value = t.add(1, 'day').format('YYYY-MM-DD')
            break
        case '30d':
            analyticsFrom.value = t.subtract(29, 'day').format('YYYY-MM-DD')
            analyticsTo.value = t.add(1, 'day').format('YYYY-MM-DD')
            break
        case 'thismonth':
            analyticsFrom.value = t.startOf('month').format('YYYY-MM-DD')
            analyticsTo.value = t.endOf('month').add(1, 'day').format('YYYY-MM-DD')
            break
        case 'ytd':
            analyticsFrom.value = t.startOf('year').format('YYYY-MM-DD')
            analyticsTo.value = t.add(1, 'day').format('YYYY-MM-DD')
            break
    }
    loadAnalytics()
}

async function loadAnalytics() {
    loadingAnalytics.value = true
    try {
        const fromUtc = dayjs.utc(analyticsFrom.value).toISOString()
        const toUtc = dayjs.utc(analyticsTo.value).toISOString()
        const r = await reportsService.getPlatformAnalytics(fromUtc, toUtc)
        analytics.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load analytics.', 'error')
    } finally {
        loadingAnalytics.value = false
    }
}

function sortBreakdown(key: keyof TenantBreakdownRow) {
    if (breakdownSortKey.value === key) {
        breakdownSortDir.value = breakdownSortDir.value === 'asc' ? 'desc' : 'asc'
    } else {
        breakdownSortKey.value = key
        breakdownSortDir.value = 'desc'
    }
}

const sortedBreakdown = computed<TenantBreakdownRow[]>(() => {
    if (!analytics.value) return []
    const rows = [...analytics.value.tenantBreakdown]
    const key = breakdownSortKey.value
    const dir = breakdownSortDir.value === 'asc' ? 1 : -1
    rows.sort((a: any, b: any) => {
        const av = a[key]; const bv = b[key]
        if (typeof av === 'number' && typeof bv === 'number') return (av - bv) * dir
        return String(av).localeCompare(String(bv)) * dir
    })
    return rows
})

const platformRevenueChart = computed(() => {
    if (!analytics.value) return null
    const points = analytics.value.dailyRevenue
    return {
        labels: points.map(p => p.date),
        datasets: [
            {
                label: 'Revenue ($)',
                data: points.map(p => p.revenueCents / 100),
                borderColor: '#1976D2',
                backgroundColor: 'rgba(25, 118, 210, 0.15)',
                fill: true,
                tension: 0.3,
                yAxisID: 'y',
            },
            {
                label: 'Passes',
                data: points.map(p => p.passesSold),
                borderColor: '#43A047',
                backgroundColor: 'transparent',
                tension: 0.3,
                yAxisID: 'y1',
            },
            {
                label: 'Tickets',
                data: points.map(p => p.ticketsSold),
                borderColor: '#FB8C00',
                backgroundColor: 'transparent',
                tension: 0.3,
                yAxisID: 'y1',
            },
        ],
    }
})

const revenueChartOptions = {
    responsive: true,
    maintainAspectRatio: false,
    interaction: { mode: 'index' as const, intersect: false },
    scales: {
        y: { beginAtZero: true, position: 'left' as const, title: { display: true, text: 'Revenue ($)' } },
        y1: { beginAtZero: true, position: 'right' as const, title: { display: true, text: 'Count' }, grid: { drawOnChartArea: false } },
    },
}

const tenantChart = computed(() => {
    if (!analytics.value) return { labels: [], datasets: [] }
    const rows = [...analytics.value.tenantBreakdown]
        .sort((a, b) => b.revenueCents - a.revenueCents)
        .slice(0, 15)
    return {
        labels: rows.map(r => r.displayName),
        datasets: [{
            label: 'Revenue ($)',
            data: rows.map(r => r.revenueCents / 100),
            backgroundColor: '#1976D2',
        }],
    }
})

const horizontalBarOptions = {
    indexAxis: 'y' as const,
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { display: false } },
    scales: { x: { beginAtZero: true } },
}

async function loadDisputes() {
    loadingDisputes.value = true
    try {
        const r = await service.listDisputes()
        disputes.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load disputes.', 'error')
    } finally {
        loadingDisputes.value = false
    }
}

function isOpenDispute(status: string): boolean {
    return status === 'needs_response' || status === 'warning_needs_response'
}

function disputeStatusColor(status: string): string {
    switch (status) {
        case 'needs_response':
        case 'warning_needs_response':
            return 'error'
        case 'under_review':
        case 'warning_under_review':
            return 'warning'
        case 'won':
            return 'success'
        case 'lost':
            return 'grey'
        case 'charge_refunded':
        case 'warning_closed':
            return 'default'
        default:
            return 'default'
    }
}

function evidenceDueClass(dueUtc: string): string {
    const hoursRemaining = dayjs.utc(dueUtc).diff(dayjs.utc(), 'hour')
    if (hoursRemaining <= 0) return 'text-error'
    if (hoursRemaining <= 48) return 'text-warning'
    return ''
}

function stripeDisputeUrl(stripeDisputeId: string): string {
    return `https://dashboard.stripe.com/disputes/${stripeDisputeId}`
}

async function processRefund(r: RefundListItem) {
    if (!r.stripePaymentIntentId) {
        flash('No Stripe payment intent on record — cannot refund automatically.', 'error')
        return
    }
    if (!confirm(`Refund $${(r.amountCents / 100).toFixed(2)} to ${r.purchaserEmail} via Stripe?`)) return
    try {
        processingId.value = r.id
        if (r.kind === 'day_pass') {
            await service.processDayPassRefund(r.id)
        } else {
            await service.processTicketRefund(r.id)
        }
        flash('Refund processed.', 'success')
        await loadRefunds()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Refund failed.', 'error')
    } finally {
        processingId.value = null
    }
}

function openCreateTenant() {
    createForm.value = {
        subdomain: '',
        displayName: '',
        timezone: 'America/New_York',
        adminFirstName: '',
        adminLastName: '',
        adminEmail: '',
    }
    createDialog.value = true
}

async function submitCreateTenant() {
    try {
        creating.value = true
        const body = {
            subdomain: createForm.value.subdomain.trim().toLowerCase(),
            displayName: createForm.value.displayName.trim(),
            timezone: createForm.value.timezone,
            adminEmail: createForm.value.adminEmail.trim() || null,
            adminFirstName: createForm.value.adminFirstName.trim() || null,
            adminLastName: createForm.value.adminLastName.trim() || null,
        }
        const r = await service.createTenant(body)
        createdResult.value = (r.data as any).data
        createDialog.value = false
        credsDialog.value = true
        await loadTenants()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to create tenant.', 'error')
    } finally {
        creating.value = false
    }
}

function openCreateSuperAdmin() {
    superAdminForm.value = { firstName: '', lastName: '', email: '', password: '' }
    superAdminDialog.value = true
}

async function submitCreateSuperAdmin() {
    try {
        creatingSuperAdmin.value = true
        await service.createSuperAdmin({
            firstName: superAdminForm.value.firstName.trim(),
            lastName: superAdminForm.value.lastName.trim(),
            email: superAdminForm.value.email.trim(),
            password: superAdminForm.value.password,
        })
        flash('Super admin created.', 'success')
        superAdminDialog.value = false
        await loadUsers()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to create super admin.', 'error')
    } finally {
        creatingSuperAdmin.value = false
    }
}

async function startImpersonation(u: SuperAdminUser) {
    if (!confirm(`Impersonate ${u.firstName} ${u.lastName} (${u.email})?`)) return
    try {
        impersonatingId.value = u.id
        const r = await service.impersonate(u.id)
        const data = (r.data as any).data
        authHelper.startImpersonation({
            token: data.token,
            userId: data.userId,
            role: data.role,
            label: `${data.firstName} ${data.lastName} <${data.email}>`,
        })
        // Route the super admin to the impersonated user's tenant (if they have one).
        if (data.tenantSubdomain) {
            const rootDomain = import.meta.env.VITE_ROOT_DOMAIN ?? 'ridepass.local'
            const port = window.location.port ? `:${window.location.port}` : ''
            window.location.href = `${window.location.protocol}//${data.tenantSubdomain}.${rootDomain}${port}/`
        } else {
            // Global users (riders) — stay on apex and the banner + nav will reflect the switch.
            router.push('/')
        }
    } catch (err: any) {
        flash(err.response?.data?.error || 'Impersonation failed.', 'error')
    } finally {
        impersonatingId.value = null
    }
}

function tenantUrl(subdomain: string): string {
    const rootDomain = import.meta.env.VITE_ROOT_DOMAIN ?? 'ridepass.local'
    const port = window.location.port ? `:${window.location.port}` : ''
    return `${window.location.protocol}//${subdomain}.${rootDomain}${port}/`
}

function formatDate(utc: string): string {
    return dayjs.utc(utc).format('YYYY-MM-DD HH:mm')
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
