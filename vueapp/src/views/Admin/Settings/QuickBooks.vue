<template>
    <v-container>
        <h1 class="text-h4 mb-2">QuickBooks</h1>
        <p class="text-body-2 text-medium-emphasis mb-6">
            Send your RidePass sales into QuickBooks Online or QuickBooks Desktop as one summarised
            journal entry per day: revenue by category, sales tax and tips as liabilities, and
            processing fees as expenses. Nothing is sent twice, and only days that have fully ended in
            your track's timezone are included.
        </p>

        <v-alert v-if="!loading && !status.isConfigured && status.edition !== 'desktop'" type="warning" variant="tonal"
            density="comfortable" class="mb-6">
            QuickBooks Online isn't set up on this RidePass deployment yet. Contact support to enable it.
            QuickBooks Desktop works without it.
        </v-alert>

        <v-tabs v-model="tab" color="primary" class="mb-4">
            <v-tab value="connection">Connection</v-tab>
            <v-tab value="accounts" :disabled="!status.isConnected">
                Chart of accounts
                <v-chip v-if="status.isConnected && !status.mappingComplete" size="x-small" color="warning"
                    variant="flat" class="ml-2">{{ status.unmappedKeys.length }}</v-chip>
            </v-tab>
            <v-tab value="classes" :disabled="!status.isConnected">Profit centers</v-tab>
            <v-tab value="history" :disabled="!status.isConnected">Sync history</v-tab>
        </v-tabs>

        <v-window v-model="tab">
            <!-- ── Connection ─────────────────────────────────────────────────────── -->
            <v-window-item value="connection">
                <v-card class="pa-4">
                    <v-card-text class="px-0 pt-0">
                        <div v-if="loading" class="d-flex align-center ga-3 py-4">
                            <v-progress-circular indeterminate size="20" />
                            <span class="text-body-2">Loading QuickBooks status…</span>
                        </div>

                        <div v-else-if="!status.isConnected" class="d-flex flex-column ga-4">
                            <div class="d-flex align-center ga-2">
                                <v-icon color="grey">mdi-link-off</v-icon>
                                <span>Not set up yet. Which QuickBooks does your bookkeeper use?</span>
                            </div>
                            <v-row>
                                <v-col cols="12" md="6">
                                    <v-card variant="outlined" class="pa-4 h-100 d-flex flex-column">
                                        <div class="text-subtitle-1 font-weight-medium">QuickBooks Online</div>
                                        <p class="text-body-2 text-medium-emphasis mt-1 mb-4 flex-grow-1">
                                            You sign in at qbo.intuit.com. We connect to your company and post each
                                            closed day automatically every night.
                                        </p>
                                        <div>
                                            <v-btn color="primary" :disabled="!status.isConfigured" :loading="connectLoading"
                                                @click="connect">
                                                Connect QuickBooks Online
                                            </v-btn>
                                        </div>
                                    </v-card>
                                </v-col>
                                <v-col cols="12" md="6">
                                    <v-card variant="outlined" class="pa-4 h-100 d-flex flex-column">
                                        <div class="text-subtitle-1 font-weight-medium">QuickBooks Desktop</div>
                                        <p class="text-body-2 text-medium-emphasis mt-1 mb-4 flex-grow-1">
                                            QuickBooks Pro, Premier or Enterprise installed on a computer. You download
                                            a file of closed days whenever you like and import it into QuickBooks.
                                        </p>
                                        <v-text-field v-model="desktopStartDate" type="date" density="compact"
                                            variant="outlined" label="First day to send"
                                            hint="Sales before this day are never sent. Usually your go-live day."
                                            persistent-hint class="mb-4" />
                                        <div>
                                            <v-btn color="primary" :loading="desktopSetupLoading" @click="setupDesktop">
                                                Use QuickBooks Desktop
                                            </v-btn>
                                        </div>
                                    </v-card>
                                </v-col>
                            </v-row>
                        </div>

                        <!-- ── QuickBooks Desktop ───────────────────────────────── -->
                        <div v-else-if="isDesktop" class="d-flex flex-column ga-5">
                            <div class="d-flex align-center ga-2 flex-wrap">
                                <v-icon :color="desktopReady ? 'success' : 'warning'">
                                    {{ desktopReady ? 'mdi-check-circle' : 'mdi-alert-circle-outline' }}
                                </v-icon>
                                <strong>QuickBooks Desktop</strong>
                                <span v-if="status.companyProduct" class="text-caption text-medium-emphasis">
                                    ({{ status.companyProduct }})
                                </span>
                                <v-chip :color="desktopReady ? 'success' : 'warning'" size="small">
                                    {{ desktopReady ? 'Ready to export' : 'Setup incomplete' }}
                                </v-chip>
                            </div>

                            <div>
                                <div class="text-subtitle-2 mb-1">1. Upload your Chart of Accounts and Class List</div>
                                <p class="text-body-2 text-medium-emphasis mb-3">
                                    In QuickBooks Desktop choose <strong>File &rarr; Utilities &rarr; Export &rarr; Lists to
                                    IIF Files</strong>, tick <strong>Chart of Accounts</strong> and <strong>Class List</strong>,
                                    save, and upload that file here. We only ever post to accounts and classes from this
                                    list, so the import can't create stray ones. Upload again whenever you add accounts.
                                </p>
                                <div class="d-flex align-center ga-3 flex-wrap">
                                    <v-file-input v-model="listFile" accept=".iif,.IIF" density="compact" variant="outlined"
                                        label="Lists .IIF file" prepend-icon="" prepend-inner-icon="mdi-paperclip"
                                        hide-details style="max-width: 360px" />
                                    <v-btn color="primary" variant="tonal" :disabled="!selectedListFile"
                                        :loading="uploadLoading" @click="uploadLists">
                                        Upload
                                    </v-btn>
                                </div>
                                <div class="text-caption text-medium-emphasis mt-2">
                                    <template v-if="status.listsUploadedAtUtc">
                                        {{ status.accountCount }} {{ status.accountCount === 1 ? 'account' : 'accounts' }},
                                        {{ status.classCount }} {{ status.classCount === 1 ? 'class' : 'classes' }}.
                                        Last uploaded {{ formatDateTime(status.listsUploadedAtUtc) }}.
                                    </template>
                                    <template v-else>Nothing uploaded yet.</template>
                                </div>
                            </div>

                            <div>
                                <div class="text-subtitle-2 mb-1">2. Map your accounts</div>
                                <p class="text-body-2 text-medium-emphasis mb-0">
                                    <template v-if="status.mappingComplete">
                                        Every account is mapped. Optional: tag revenue with classes in the Profit centers tab.
                                    </template>
                                    <template v-else>
                                        {{ status.unmappedKeys.length }}
                                        {{ status.unmappedKeys.length === 1 ? 'account still needs' : 'accounts still need' }}
                                        mapping in the Chart of accounts tab before you can export.
                                    </template>
                                </p>
                            </div>

                            <div>
                                <div class="text-subtitle-2 mb-1">3. Export and import</div>
                                <p class="text-body-2 text-medium-emphasis mb-3">
                                    Each file holds every closed day since your last export, one journal entry per day
                                    (numbered RP-yyyymmdd). In QuickBooks Desktop choose <strong>File &rarr; Utilities
                                    &rarr; Import &rarr; IIF Files</strong>. Import each file once: QuickBooks doesn't
                                    check for duplicates, so importing the same file twice doubles those days.
                                </p>
                                <div class="d-flex align-center ga-3 flex-wrap">
                                    <v-btn color="primary" :loading="exportLoading" :disabled="!desktopReady" @click="exportDesktop">
                                        Create export file
                                    </v-btn>
                                    <span class="text-caption text-medium-emphasis">
                                        Sending sales from {{ formatDate(status.syncStartDate) }}.
                                        <template v-if="status.lastSyncedDate">
                                            Exported through {{ formatDate(status.lastSyncedDate) }}.
                                        </template>
                                    </span>
                                </div>
                                <div v-if="!status.hasExported" class="d-flex align-center ga-3 flex-wrap mt-3">
                                    <v-text-field v-model="desktopStartDate" type="date" density="compact" variant="outlined"
                                        label="First day to send" hide-details style="max-width: 220px" />
                                    <v-btn variant="text" :loading="startDateLoading"
                                        :disabled="!desktopStartDate || desktopStartDate === status.syncStartDate"
                                        @click="saveStartDate">
                                        Change start date
                                    </v-btn>
                                </div>
                            </div>

                            <div>
                                <div class="text-subtitle-2 mb-2">Export files</div>
                                <v-alert v-if="batchesError" type="error" variant="tonal" density="compact" class="mb-3">
                                    {{ batchesError }}
                                    <template #append>
                                        <v-btn size="small" variant="text" @click="loadBatches">Retry</v-btn>
                                    </template>
                                </v-alert>
                                <div class="table-scroll">
                                    <v-table density="compact">
                                        <thead>
                                            <tr>
                                                <th class="text-left">Days</th>
                                                <th class="text-right">Day count</th>
                                                <th class="text-right">Total</th>
                                                <th class="text-left">Created</th>
                                                <th class="text-right">Downloads</th>
                                                <th></th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            <tr v-for="b in batches" :key="b.id" :class="b.voidedAtUtc ? 'text-medium-emphasis' : ''">
                                                <td>
                                                    {{ formatDate(b.fromDate) }}
                                                    <template v-if="b.toDate !== b.fromDate"> to {{ formatDate(b.toDate) }}</template>
                                                    <v-chip v-if="b.voidedAtUtc" size="x-small" class="ml-2">Voided</v-chip>
                                                </td>
                                                <td class="text-right">{{ b.dayCount }}</td>
                                                <td class="text-right">{{ formatMoney(b.totalDebitsCents) }}</td>
                                                <td>{{ formatDateTime(b.createdAt) }}</td>
                                                <td class="text-right">{{ b.downloadCount }}</td>
                                                <td class="text-right text-no-wrap">
                                                    <template v-if="!b.voidedAtUtc">
                                                        <v-btn size="x-small" variant="text" :loading="downloading === b.id"
                                                            @click="downloadBatch(b)">
                                                            Download
                                                        </v-btn>
                                                        <v-btn size="x-small" variant="text" color="error" :loading="voiding === b.id"
                                                            @click="voidBatch(b)">
                                                            Void
                                                        </v-btn>
                                                    </template>
                                                </td>
                                            </tr>
                                            <tr v-if="!batches.length && !batchesError">
                                                <td colspan="6" class="text-center text-medium-emphasis py-4">
                                                    No files exported yet.
                                                </td>
                                            </tr>
                                        </tbody>
                                    </v-table>
                                </div>
                            </div>

                            <div>
                                <v-btn variant="text" color="error" :loading="disconnectLoading" @click="removeDesktop">
                                    Stop using QuickBooks Desktop
                                </v-btn>
                            </div>
                        </div>

                        <!-- ── QuickBooks Online ────────────────────────────────── -->
                        <div v-else class="d-flex flex-column ga-3">
                            <div class="d-flex align-center ga-2 flex-wrap">
                                <v-icon :color="statusColor">{{ statusIcon }}</v-icon>
                                <span>
                                    <strong>{{ status.companyName || 'Connected' }}</strong>
                                    <code v-if="status.realmId" class="ml-2 text-caption">Realm {{ status.realmId }}</code>
                                </span>
                                <v-chip :color="statusColor" size="small">{{ statusLabel }}</v-chip>
                            </div>

                            <v-alert v-if="status.lastSyncError" type="error" variant="tonal" density="compact">
                                {{ status.lastSyncError }}
                            </v-alert>

                            <v-alert v-if="!status.mappingComplete" type="info" variant="tonal" density="compact">
                                Account mapping isn't finished, so nothing can post yet. Complete it in the
                                Chart of accounts tab.
                            </v-alert>

                            <div class="text-caption text-medium-emphasis">
                                Syncing sales from {{ formatDate(status.syncStartDate) }}.
                                <template v-if="status.lastSyncedDate">
                                    Posted through {{ formatDate(status.lastSyncedDate) }}.
                                </template>
                                <template v-else>Nothing posted yet.</template>
                            </div>

                            <v-switch v-model="syncEnabled" color="primary" density="compact" hide-details
                                :label="syncEnabled ? 'Nightly sync is on' : 'Nightly sync is paused'"
                                :loading="toggleLoading" @update:model-value="onToggleSync" />

                            <div class="d-flex ga-2 flex-wrap">
                                <v-btn color="primary" variant="tonal" :loading="syncLoading"
                                    :disabled="!status.mappingComplete" @click="syncNow">
                                    Sync now
                                </v-btn>
                                <v-btn variant="text" :loading="connectLoading" @click="connect">Reconnect</v-btn>
                                <v-btn variant="text" color="error" :loading="disconnectLoading" @click="disconnect">
                                    Disconnect
                                </v-btn>
                            </div>
                        </div>
                    </v-card-text>
                </v-card>
            </v-window-item>

            <!-- ── Account mapping ────────────────────────────────────────────────── -->
            <v-window-item value="accounts">
                <v-card class="pa-4">
                    <v-card-text class="px-0 pt-0">
                        <p class="text-body-2 text-medium-emphasis mb-2">
                            Tell us which of your accounts each kind of money belongs in. Every row needs an
                            account before we can post, a day whose accounts aren't all mapped is held back
                            rather than booked to the wrong place.
                        </p>

                        <v-alert v-if="isDesktop && !status.accountCount" type="warning" variant="tonal" density="compact" class="mb-4">
                            Upload your Chart of Accounts from QuickBooks Desktop on the Connection tab first; these
                            dropdowns list only accounts from that file.
                        </v-alert>

                        <v-alert v-if="!status.mappingComplete" type="info" variant="tonal" density="compact" class="mb-4">
                            {{ status.unmappedKeys.length }}
                            {{ status.unmappedKeys.length === 1 ? 'account still needs' : 'accounts still need' }}
                            to be mapped before syncing can start.
                        </v-alert>

                        <div v-if="accountsError" class="mb-4">
                            <v-alert type="error" variant="tonal" density="compact">
                                {{ accountsError }}
                                <template #append>
                                    <v-btn size="small" variant="text" @click="loadAccounts">Retry</v-btn>
                                </template>
                            </v-alert>
                        </div>

                        <template v-for="(group, gi) in mappingGroups" :key="group.title">
                            <div class="text-subtitle-2 mb-3" :class="gi === 0 ? '' : 'mt-6'">
                                {{ group.title }}
                                <span class="text-caption text-medium-emphasis ml-1">{{ group.caption }}</span>
                            </div>

                            <!-- Revenue grouped by the tenant's profit centers: one account per
                                 center, expanded to the member slots on save. -->
                            <template v-if="group.classification === 'Revenue' && centerGroups.length">
                                <div v-for="(g, i) in centerGroups" :key="g.center.id"
                                    class="d-flex align-center ga-3" :class="i === 0 ? '' : 'mt-4'">
                                    <!-- The center's color, so this screen names the same buckets
                                         the reports and charts show, in the same colors. -->
                                    <span class="swatch" :style="{ background: seriesColor(g.center.color, isDark) }"></span>
                                    <v-select v-model="centerAccounts[g.center.id]" :items="accountsFor('Revenue')"
                                        item-title="name" item-value="id" density="compact" variant="outlined"
                                        :label="g.center.name" :loading="accountsLoading" clearable
                                        :hint="centerHint(g)" persistent-hint />
                                </div>
                                <div v-for="m in ungroupedRevenueRows" :key="m.mappingKey">
                                    <v-select v-model="m.qboAccountId" :items="accountsFor('Revenue')"
                                        item-title="name" item-value="id" density="compact" variant="outlined"
                                        :label="m.label" :loading="accountsLoading" clearable
                                        class="mt-4" hide-details />
                                </div>
                            </template>

                            <template v-else>
                                <div v-for="(m, i) in group.rows" :key="m.mappingKey">
                                    <v-select v-model="m.qboAccountId" :items="accountsFor(m.expectedClassification)"
                                        item-title="name" item-value="id" density="compact" variant="outlined"
                                        :label="m.label" :loading="accountsLoading" clearable
                                        :class="i === 0 ? '' : 'mt-4'" hide-details />
                                </div>
                            </template>
                        </template>

                        <div class="d-flex ga-2 mt-6">
                            <v-btn color="primary" :loading="saveLoading" @click="saveMappings">Save mapping</v-btn>
                        </div>
                    </v-card-text>
                </v-card>
            </v-window-item>

            <!-- ── Profit centers (QBO classes) ───────────────────────────────────── -->
            <v-window-item value="classes">
                <v-card class="pa-4">
                    <v-card-text class="px-0 pt-0">
                        <p class="text-body-2 text-medium-emphasis mb-2">
                            Give each profit center a QuickBooks class and every sale we post gets tagged with
                            it, so a Profit &amp; Loss by Class in QuickBooks splits out your bike shop, food and
                            gate revenue the same way the Revenue by Department report does here. This is
                            optional: leave it blank and revenue still posts, just untagged.
                        </p>
                        <p class="text-body-2 text-medium-emphasis mb-4">
                            Only income lines are tagged. Sales tax, tips, gift card balances, processing fees
                            and cash movements belong to the whole business rather than one center, so they post
                            without a class and show under "Not Specified".
                        </p>

                        <div v-if="classesLoading" class="d-flex align-center ga-3 py-4">
                            <v-progress-circular indeterminate size="20" />
                            <span class="text-body-2">Loading your QuickBooks classes…</span>
                        </div>

                        <template v-else>
                            <!-- Desktop keeps its class preference inside the company file, which we can't
                                 read, so say where the switch is up front. -->
                            <template v-if="isDesktop && !classesError">
                                <v-alert type="info" variant="tonal" density="compact" class="mb-4">
                                    Class tracking has to be on in QuickBooks Desktop: <strong>Edit &rarr; Preferences
                                    &rarr; Accounting &rarr; Company Preferences &rarr; Use class tracking for
                                    transactions</strong>.
                                </v-alert>
                                <v-alert v-if="!classSettings.classes.length" type="info" variant="tonal"
                                    density="compact" class="mb-4">
                                    No classes uploaded yet. Include the Class List when you export your lists from
                                    QuickBooks Desktop, then upload the file on the Connection tab.
                                </v-alert>
                            </template>

                            <v-alert v-if="classesError" type="error" variant="tonal" density="compact" class="mb-4">
                                {{ classesError }}
                                <template #append>
                                    <v-btn size="small" variant="text" @click="loadClasses">Retry</v-btn>
                                </template>
                            </v-alert>

                            <!-- Nothing below is usable until QuickBooks itself is tracking classes, so say
                                 exactly where the switch is rather than showing empty dropdowns. -->
                            <v-alert v-else-if="!classSettings.trackingEnabled" type="warning" variant="tonal"
                                density="comfortable" class="mb-4">
                                Class tracking is switched off in QuickBooks, so we can't tag anything yet. In
                                QuickBooks go to Settings &rarr; Account and settings &rarr; Advanced &rarr;
                                Categories, turn on <strong>Track classes</strong>, choose
                                <strong>Assign one to each row in transaction</strong>, then come back and
                                reload this page.
                            </v-alert>

                            <template v-else>
                                <!-- Per-transaction mode puts ONE class on the whole journal entry, which
                                     would flatten every center back into a single bucket. -->
                                <v-alert v-if="!classSettings.trackingPerLine" type="warning" variant="tonal"
                                    density="comfortable" class="mb-4">
                                    QuickBooks is set to one class per transaction. Because we post one summary
                                    journal entry per day, that can't separate your centers. Switch Categories to
                                    <strong>Assign one to each row in transaction</strong> in QuickBooks to get a
                                    real split.
                                </v-alert>

                                <v-alert v-if="!classSettings.classes.length && !isDesktop" type="info" variant="tonal"
                                    density="compact" class="mb-4">
                                    Your QuickBooks company has class tracking on but no classes created yet. Add
                                    one per profit center in QuickBooks, then reload this page.
                                </v-alert>

                                <div v-for="(row, i) in classMappings" :key="row.bucketKey"
                                    class="d-flex align-center ga-3" :class="i === 0 ? '' : 'mt-4'">
                                    <!-- Same color the reports and charts draw this bucket in. -->
                                    <span class="swatch" :style="{ background: seriesColor(row.color, isDark) }"></span>
                                    <v-select v-model="row.qboClassId" :items="classSettings.classes"
                                        item-title="fullyQualifiedName" item-value="id" density="compact"
                                        variant="outlined" :label="row.label" clearable
                                        :disabled="!classSettings.classes.length"
                                        :hint="classHint(row)" persistent-hint />
                                </div>

                                <p v-if="usingBuiltInDepartments" class="text-caption text-medium-emphasis mt-4">
                                    These are the built-in departments. To use your own names and grouping,
                                    set them up under
                                    <router-link to="/Admin/Settings/ProfitCenters">Settings &rarr; Profit centers</router-link>.
                                </p>

                                <p class="text-caption text-medium-emphasis mt-4">
                                    <template v-if="isDesktop">
                                        Files already exported keep the classes they were built with. To re-tag them,
                                        delete those journal entries in QuickBooks Desktop, void the file, and export again.
                                    </template>
                                    <template v-else>
                                        Days already posted keep the classes they were posted with. To re-tag one,
                                        delete its journal entry in QuickBooks and retry the day from Sync history.
                                    </template>
                                </p>

                                <div class="d-flex ga-2 mt-6">
                                    <v-btn color="primary" :loading="saveClassesLoading"
                                        :disabled="!classSettings.classes.length" @click="saveClassMappings">
                                        Save profit centers
                                    </v-btn>
                                </div>
                            </template>
                        </template>
                    </v-card-text>
                </v-card>
            </v-window-item>

            <!-- ── History ────────────────────────────────────────────────────────── -->
            <v-window-item value="history">
                <v-card class="pa-4">
                    <v-card-text class="px-0 pt-0">
                        <div class="table-scroll">
                            <v-table density="compact">
                                <thead>
                                    <tr>
                                        <th class="text-left">Business date</th>
                                        <th class="text-left">Status</th>
                                        <th class="text-right">Transactions</th>
                                        <th class="text-right">Total</th>
                                        <th class="text-left">Journal entry</th>
                                        <th></th>
                                    </tr>
                                </thead>
                                <tbody>
                                    <tr v-for="row in syncLog" :key="row.businessDate">
                                        <td>{{ formatDate(row.businessDate) }}</td>
                                        <td>
                                            <v-chip :color="rowColor(row.status)" size="x-small">{{ rowLabel(row) }}</v-chip>
                                            <div v-if="row.lastError" class="text-caption text-error mt-1">{{ row.lastError }}</div>
                                        </td>
                                        <td class="text-right">{{ row.entryCount }}</td>
                                        <td class="text-right">{{ formatMoney(row.totalDebitsCents) }}</td>
                                        <td>
                                            <code v-if="row.qboDocNumber" class="text-caption">{{ row.qboDocNumber }}</code>
                                            <span v-else class="text-medium-emphasis text-caption">, </span>
                                        </td>
                                        <td class="text-right">
                                            <v-btn v-if="row.status === 'failed' && !isDesktop" size="x-small" variant="text"
                                                :loading="resyncing === row.businessDate" @click="resync(row.businessDate)">
                                                Retry
                                            </v-btn>
                                        </td>
                                    </tr>
                                    <tr v-if="!syncLog.length && !loading">
                                        <td colspan="6" class="text-center text-medium-emphasis py-4">
                                            <template v-if="isDesktop">Nothing exported yet. Create an export file on the Connection tab.</template>
                                            <template v-else>Nothing posted yet. The first sync runs tonight, or use "Sync now".</template>
                                        </td>
                                    </tr>
                                </tbody>
                            </v-table>
                        </div>
                    </v-card-text>
                </v-card>
            </v-window-item>
        </v-window>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="5000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { QuickBooksService, type QuickBooksStatus, type QboAccount, type QboMapping, type QboSyncLogRow, type QboClassSettings, type QboClassMapping, type QbdExportBatch } from '../../../services/QuickBooksService'
import { useTheme } from 'vuetify'
import { ProfitCenterService, type ProfitCenter } from '../../../services/ProfitCenterService'
import { seriesColor } from '../../../helpers/profitCenterColor'
import { useConfirm } from '../../../composables/useConfirm'

const service = new QuickBooksService()
const profitCenterService = new ProfitCenterService()
const theme = useTheme()
const isDark = computed(() => theme.current.value.dark)
const confirm = useConfirm()
const route = useRoute()
const router = useRouter()

const tab = ref('connection')
const loading = ref(true)
const connectLoading = ref(false)
const disconnectLoading = ref(false)
const toggleLoading = ref(false)
const syncLoading = ref(false)
const saveLoading = ref(false)
const accountsLoading = ref(false)
const resyncing = ref<string | null>(null)

const accountsError = ref('')
const syncEnabled = ref(false)

const status = ref<QuickBooksStatus>({
    isConfigured: false, edition: null, isConnected: false, status: null, realmId: null, companyName: null,
    syncEnabled: false, syncStartDate: null, lastSyncedDate: null, lastSyncAtUtc: null,
    lastSyncError: null, connectedAtUtc: null, mappingComplete: false, unmappedKeys: [],
    listsUploadedAtUtc: null, companyProduct: null, accountCount: 0, classCount: 0, hasExported: false,
})

// ── QuickBooks Desktop ──────────────────────────────────────────────────────────
const isDesktop = computed(() => status.value.edition === 'desktop')
// Exporting needs both halves: the track's own account list (so every line names a real account)
// and a complete mapping. The server enforces both; this only stops a doomed click.
const desktopReady = computed(() => isDesktop.value && status.value.accountCount > 0 && status.value.mappingComplete)
// yyyy-MM-dd from the date input. Blank on first setup means "today at the track", resolved server-side.
const desktopStartDate = ref('')
const desktopSetupLoading = ref(false)
const startDateLoading = ref(false)
const uploadLoading = ref(false)
const exportLoading = ref(false)
const listFile = ref<File | File[] | null>(null)
const selectedListFile = computed<File | null>(() =>
    Array.isArray(listFile.value) ? (listFile.value[0] ?? null) : listFile.value)
const batches = ref<QbdExportBatch[]>([])
const batchesError = ref('')
const downloading = ref<string | null>(null)
const voiding = ref<string | null>(null)
const accounts = ref<QboAccount[]>([])
const mappings = ref<QboMapping[]>([])
const syncLog = ref<QboSyncLogRow[]>([])

// The tenant's profit centers (Settings > Profit Centers). When configured, the revenue section
// asks for ONE account per center and expands it to the member slots on save. Empty = flat list.
const profitCenters = ref<ProfitCenter[]>([])
// Per-center account choice, keyed by center id.
const centerAccounts = ref<Record<string, string | null>>({})
// True when a center's member slots currently point at different accounts (pre-centers drift).
const centerMixed = ref<Record<string, boolean>>({})

// Profit centers -> QuickBooks classes. Optional, and the only part of this screen that needs a
// QBO preference read, so it loads lazily the first time the tab is opened rather than on mount.
const classSettings = ref<QboClassSettings>({ trackingEnabled: true, trackingPerLine: true, classes: [] })
const classMappings = ref<QboClassMapping[]>([])
const classesLoading = ref(false)
const classesLoaded = ref(false)
const classesError = ref('')
const saveClassesLoading = ref(false)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

// The accounts/history tabs only exist for a connected company; if the user disconnects while
// sitting on one, land them back on Connection instead of a disabled tab's stale panel.
watch(() => status.value.isConnected, connected => {
    if (!connected && tab.value !== 'connection') tab.value = 'connection'
})

// First visit to the Profit centers tab pulls the class list and the company's class-tracking
// preference. Both are QBO round-trips, so a track that never uses classes never pays for them.
watch(tab, async value => {
    if (value === 'classes' && !classesLoaded.value) await loadClasses()
})

function toast(text: string, color: 'success' | 'error' = 'success') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

/** Prefer the server's own message, it names the QuickBooks account or day that's wrong. */
function errText(err: any, fallback: string): string {
    return err?.response?.data?.error || fallback
}

// 'error' and 'expired' both mean "we can't post" but they need different words: expired is fixed
// by reconnecting, error might just be a bad account mapping.
const statusColor = computed(() => {
    switch (status.value.status) {
        case 'active': return status.value.mappingComplete ? 'success' : 'warning'
        case 'expired': return 'warning'
        case 'error': return 'error'
        case 'revoked': return 'error'
        default: return 'grey'
    }
})

const statusIcon = computed(() => {
    switch (status.value.status) {
        case 'active': return status.value.mappingComplete ? 'mdi-check-circle' : 'mdi-alert-circle-outline'
        case 'expired': return 'mdi-clock-alert-outline'
        default: return 'mdi-alert-circle'
    }
})

const statusLabel = computed(() => {
    switch (status.value.status) {
        case 'active': return status.value.mappingComplete ? 'Syncing' : 'Setup incomplete'
        case 'expired': return 'Reconnect needed'
        case 'revoked': return 'Access revoked'
        case 'error': return 'Attention needed'
        default: return 'Unknown'
    }
})

function rowColor(s: string) {
    return s === 'success' ? 'success' : s === 'failed' ? 'error' : 'grey'
}
function rowLabel(row: QboSyncLogRow) {
    if (row.status === 'success') return row.exportBatchId ? 'Exported' : 'Posted'
    return row.status === 'failed' ? 'Failed' : 'No activity'
}

function formatDateTime(utc: string | null) {
    if (!utc) return ''
    return new Date(utc).toLocaleString(undefined, { month: 'numeric', day: 'numeric', year: 'numeric', hour: 'numeric', minute: '2-digit' })
}

/**
 * A blob download that fails carries its JSON error as a Blob, so the usual
 * err.response.data.error is undefined; read it out before falling back.
 */
async function blobErrText(err: any, fallback: string): Promise<string> {
    const data = err?.response?.data
    if (data instanceof Blob) {
        try { return JSON.parse(await data.text())?.error || fallback } catch { return fallback }
    }
    return errText(err, fallback)
}

function formatDate(d: string | null) {
    if (!d) return ', '
    // The API sends a plain calendar date (the track's local business date). Parsing it with the
    // Date constructor would treat it as UTC midnight and shift it a day back in western
    // timezones, so split it rather than round-trip through Date.
    const [y, m, day] = d.slice(0, 10).split('-')
    return `${m}/${day}/${y}`
}

function formatMoney(cents: number) {
    return (cents / 100).toLocaleString(undefined, { style: 'currency', currency: 'USD' })
}

// The rows grouped by classification, in a fixed money-flow order, so the section heading says
// "these are income accounts" ONCE instead of a per-row hint under every dropdown repeating it.
const GROUP_ORDER: { classification: string; title: string; caption: string }[] = [
    { classification: 'Revenue',   title: 'Revenue',           caption: 'where each kind of sale is booked as income' },
    { classification: 'Liability', title: 'Liabilities',       caption: 'money you hold that isn\'t yours yet: tax, tips, gift card balances' },
    { classification: 'Asset',     title: 'Assets',            caption: 'where money sits before it reaches your bank' },
    { classification: 'Expense',   title: 'Fees & expenses',   caption: 'processing and platform fees' },
]

const mappingGroups = computed(() =>
    GROUP_ORDER
        .map(g => ({ ...g, rows: mappings.value.filter(m => m.expectedClassification === g.classification) }))
        // A classification QBO never asked us for (or a future one this build doesn't know) still
        // renders: anything unmatched lands in a trailing group rather than silently disappearing.
        .concat([{
            classification: '', title: 'Other', caption: '',
            rows: mappings.value.filter(m => !GROUP_ORDER.some(g => g.classification === m.expectedClassification)),
        }])
        .filter(g => g.rows.length > 0)
)

const revenueRows = computed(() => mappings.value.filter(m => m.expectedClassification === 'Revenue'))

// Only centers that actually own one of THIS tenant's required revenue slots render; a center
// whose streams the tenant can't use (feature off) would just be an empty picker.
const centerGroups = computed(() =>
    profitCenters.value
        .map(c => ({ center: c, rows: revenueRows.value.filter(m => c.revenueKeys.includes(m.mappingKey)) }))
        .filter(g => g.rows.length > 0))

const ungroupedRevenueRows = computed(() => {
    const grouped = new Set(centerGroups.value.flatMap(g => g.rows.map(r => r.mappingKey)))
    return revenueRows.value.filter(m => !grouped.has(m.mappingKey))
})

function centerHint(g: { center: ProfitCenter; rows: QboMapping[] }): string {
    const streams = g.rows.map(r => r.label).join(', ')
    return centerMixed.value[g.center.id]
        ? `Currently split across multiple accounts; pick one to unify. Includes: ${streams}`
        : `Includes: ${streams}`
}

// Seed each center's picker from the saved per-slot mappings: unanimous non-empty slots show
// their shared account; anything else starts blank (saving then keeps the per-slot values).
function initCenterAccounts() {
    const acc: Record<string, string | null> = {}
    const mixed: Record<string, boolean> = {}
    for (const g of centerGroups.value) {
        const ids = [...new Set(g.rows.map(r => r.qboAccountId).filter((id): id is string => !!id))]
        const anyBlank = g.rows.some(r => !r.qboAccountId)
        acc[g.center.id] = ids.length === 1 && !anyBlank ? ids[0] : null
        mixed[g.center.id] = ids.length > 1
    }
    centerAccounts.value = acc
    centerMixed.value = mixed
}

function classHint(row: QboClassMapping): string {
    const streams = row.revenueStreams.join(', ')
    return streams ? `Tags: ${streams}` : 'No revenue streams report under this center yet.'
}

// True when the tenant has never configured centers and is seeing the built-in departments, which
// is worth saying out loud: the names aren't theirs and they can change that.
const usingBuiltInDepartments = computed(() =>
    classMappings.value.length > 0 && classMappings.value.every(r => !r.isCustom))

function accountsFor(classification: string) {
    const matching = accounts.value.filter(a => a.classification === classification)
    // If QBO didn't classify anything (unusual, but possible on odd charts of accounts), showing
    // an empty dropdown would look broken and leave the tenant stuck. Fall back to everything.
    return matching.length ? matching : accounts.value
}

async function load() {
    loading.value = true
    try {
        const resp = await service.status()
        status.value = resp.data.data
        syncEnabled.value = status.value.syncEnabled

        if (status.value.isConnected) {
            const loads = [loadMappings(), loadSyncLog(), loadAccounts(), loadProfitCenters()]
            if (isDesktop.value) {
                desktopStartDate.value = status.value.syncStartDate ?? ''
                loads.push(loadBatches())
            }
            await Promise.all(loads)
            initCenterAccounts()
        }
    } catch (err: any) {
        toast(errText(err, 'Could not load your QuickBooks settings. Refresh the page to try again.'), 'error')
    } finally {
        loading.value = false
    }
}

async function loadAccounts() {
    accountsLoading.value = true
    accountsError.value = ''
    try {
        const resp = await service.accounts()
        accounts.value = resp.data.data
    } catch (err: any) {
        // Don't leave an empty dropdown looking like "you have no accounts", say what happened.
        accountsError.value = errText(err, 'Could not load your QuickBooks chart of accounts.')
    } finally {
        accountsLoading.value = false
    }
}

async function loadClasses() {
    classesLoading.value = true
    classesError.value = ''
    try {
        const [settings, mappings] = await Promise.all([service.classes(), service.classMappings()])
        classSettings.value = settings.data.data
        classMappings.value = mappings.data.data
        classesLoaded.value = true
    } catch (err: any) {
        // Never render an empty picker as "you have no classes"; say what actually failed.
        classesError.value = errText(err, 'Could not load your QuickBooks classes. Check the connection and try again.')
    } finally {
        classesLoading.value = false
    }
}

async function saveClassMappings() {
    saveClassesLoading.value = true
    try {
        await service.saveClassMappings(classMappings.value.map(row => ({
            bucketKey: row.bucketKey,
            qboClassId: row.qboClassId,
            qboClassName: classSettings.value.classes.find(c => c.id === row.qboClassId)?.fullyQualifiedName ?? null,
        })))
        toast('Profit centers saved. They apply to the next day we post.')
        await loadClasses()
    } catch (err: any) {
        toast(errText(err, 'Could not save your profit center classes.'), 'error')
    } finally {
        saveClassesLoading.value = false
    }
}

async function loadMappings() {
    try {
        const resp = await service.mappings()
        mappings.value = resp.data.data
    } catch (err: any) {
        toast(errText(err, 'Could not load your account mapping.'), 'error')
    }
}

async function loadProfitCenters() {
    try {
        const resp = await profitCenterService.get()
        profitCenters.value = resp.data.data.usingDefaults ? [] : resp.data.data.centers
    } catch (err: any) {
        // The flat per-slot list still works without them; say why the grouping is missing.
        profitCenters.value = []
        toast(errText(err, 'Could not load your profit centers; showing the ungrouped account list.'), 'error')
    }
}

async function loadSyncLog() {
    try {
        const resp = await service.syncLog()
        syncLog.value = resp.data.data
    } catch (err: any) {
        toast(errText(err, 'Could not load the QuickBooks sync history.'), 'error')
    }
}

async function connect() {
    connectLoading.value = true
    try {
        const resp = await service.connect()
        // Full navigation, not a popup: Intuit blocks its consent screen in an iframe, and the
        // callback comes back to the apex host before redirecting here.
        window.location.href = resp.data.data.authorizationUrl
    } catch (err: any) {
        toast(errText(err, 'Could not start the QuickBooks connection. Please try again.'), 'error')
        connectLoading.value = false
    }
}

async function disconnect() {
    const ok = await confirm({
        title: 'Disconnect QuickBooks?',
        message: 'New sales will stop syncing. Journal entries already posted stay in QuickBooks, and '
            + 'your account mapping is kept in case you reconnect.',
        confirmText: 'Disconnect',
        confirmColor: 'error',
    })
    if (!ok) return

    disconnectLoading.value = true
    try {
        await service.disconnect()
        // Drop the cached class list with the company it came from: reconnecting to a DIFFERENT
        // QuickBooks company must not show the old one's classes.
        classesLoaded.value = false
        classSettings.value = { trackingEnabled: true, trackingPerLine: true, classes: [] }
        classMappings.value = []
        toast('QuickBooks disconnected.')
        await load()
    } catch (err: any) {
        toast(errText(err, 'Could not disconnect QuickBooks. Please try again.'), 'error')
    } finally {
        disconnectLoading.value = false
    }
}

async function onToggleSync(value: boolean | null) {
    const enabled = !!value
    toggleLoading.value = true
    try {
        await service.setSyncEnabled(enabled)
        status.value.syncEnabled = enabled
        toast(enabled ? 'Nightly sync resumed.' : 'Nightly sync paused.')
    } catch (err: any) {
        syncEnabled.value = !enabled   // roll the switch back so it reflects the server
        toast(errText(err, 'Could not change the sync setting. Please try again.'), 'error')
    } finally {
        toggleLoading.value = false
    }
}

async function syncNow() {
    syncLoading.value = true
    try {
        const resp = await service.syncNow()
        const { posted, skipped } = resp.data.data
        toast(posted
            ? `Posted ${posted} ${posted === 1 ? 'day' : 'days'} to QuickBooks.`
            : `Already up to date${skipped ? ` (${skipped} ${skipped === 1 ? 'day' : 'days'} had no activity)` : ''}.`)
        await load()
    } catch (err: any) {
        toast(errText(err, 'The QuickBooks sync could not be completed.'), 'error')
        await load()   // surface the per-day error the server just recorded
    } finally {
        syncLoading.value = false
    }
}

async function resync(businessDate: string) {
    resyncing.value = businessDate
    try {
        await service.resync(businessDate)
        toast(`${formatDate(businessDate)} posted to QuickBooks.`)
        await load()
    } catch (err: any) {
        toast(errText(err, `Could not post ${formatDate(businessDate)} to QuickBooks.`), 'error')
        await loadSyncLog()
    } finally {
        resyncing.value = null
    }
}

async function loadBatches() {
    batchesError.value = ''
    try {
        const resp = await service.desktopBatches()
        batches.value = resp.data.data
    } catch (err: any) {
        // Never show "No files exported yet" when the list simply failed to load.
        batchesError.value = errText(err, 'Could not load your QuickBooks Desktop export files.')
    }
}

async function setupDesktop() {
    desktopSetupLoading.value = true
    try {
        await service.desktopSetup(desktopStartDate.value || null)
        toast('Switched to QuickBooks Desktop. Next, upload your Chart of Accounts and Class List.')
        await load()
    } catch (err: any) {
        toast(errText(err, 'Could not switch this track to QuickBooks Desktop. Please try again.'), 'error')
    } finally {
        desktopSetupLoading.value = false
    }
}

async function saveStartDate() {
    if (!desktopStartDate.value) return
    startDateLoading.value = true
    try {
        await service.desktopSetStartDate(desktopStartDate.value)
        toast(`Exports now start ${formatDate(desktopStartDate.value)}.`)
        await load()
    } catch (err: any) {
        toast(errText(err, 'Could not change the start date.'), 'error')
    } finally {
        startDateLoading.value = false
    }
}

async function uploadLists() {
    const file = selectedListFile.value
    if (!file) return
    uploadLoading.value = true
    try {
        const resp = await service.desktopUploadLists(file)
        const r = resp.data.data
        const parts: string[] = []
        if (r.accountCount !== null) parts.push(`${r.accountCount} ${r.accountCount === 1 ? 'account' : 'accounts'}`)
        if (r.classCount !== null) parts.push(`${r.classCount} ${r.classCount === 1 ? 'class' : 'classes'}`)
        listFile.value = null
        if (r.missingMappedNames.length) {
            toast(`Uploaded ${parts.join(' and ')}. These mapped names are no longer in your lists and need re-mapping: `
                + r.missingMappedNames.join(', '), 'error')
        } else {
            toast(`Uploaded ${parts.join(' and ')}.`)
        }
        await load()
    } catch (err: any) {
        toast(errText(err, 'Could not read that file. Export your lists from QuickBooks Desktop as an .IIF file and try again.'), 'error')
    } finally {
        uploadLoading.value = false
    }
}

async function exportDesktop() {
    exportLoading.value = true
    try {
        const resp = await service.desktopExport()
        const r = resp.data.data
        if (!r.batchId) {
            toast(r.daysWithNoActivity
                ? `Nothing to import: the new days had no net sales. Everything through ${formatDate(r.lastCompleteDay)} is covered.`
                : `Nothing new to export. Everything through ${formatDate(r.lastCompleteDay)} is already in a file.`)
        } else {
            const range = r.fromDate === r.toDate
                ? formatDate(r.fromDate)
                : `${formatDate(r.fromDate)} to ${formatDate(r.toDate)}`
            try {
                await downloadBatchById(r.batchId)
                toast(`Exported ${r.daysInFile} ${r.daysInFile === 1 ? 'day' : 'days'} (${range}).`
                    + (r.moreRemaining ? ' More days are waiting: export again for the rest.' : ''))
            } catch (dlErr: any) {
                // The batch exists and its days are claimed; only the download failed, so point at
                // the Download button rather than suggesting a fresh export.
                toast(await blobErrText(dlErr, `The file for ${range} was created but didn't download. Use Download in the Export files list.`), 'error')
            }
        }
        await load()
    } catch (err: any) {
        toast(errText(err, 'Could not build the QuickBooks Desktop export.'), 'error')
    } finally {
        exportLoading.value = false
    }
}

async function downloadBatchById(id: string) {
    const { blob, filename } = await service.desktopDownload(id)
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = filename
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
}

async function downloadBatch(b: QbdExportBatch) {
    // A second download is the same file. Remind them rather than block: re-downloading to a new
    // computer is legitimate, importing it twice is the mistake.
    if (b.downloadCount > 0) {
        const ok = await confirm({
            title: 'Download this file again?',
            message: `This file has been downloaded ${b.downloadCount} ${b.downloadCount === 1 ? 'time' : 'times'}. `
                + 'If it was already imported into QuickBooks Desktop, importing it again doubles those days.',
            confirmText: 'Download',
        })
        if (!ok) return
    }
    downloading.value = b.id
    try {
        await downloadBatchById(b.id)
        await loadBatches()
    } catch (err: any) {
        toast(await blobErrText(err, 'Could not download that export file. Please try again.'), 'error')
    } finally {
        downloading.value = null
    }
}

async function voidBatch(b: QbdExportBatch) {
    const range = b.fromDate === b.toDate ? formatDate(b.fromDate) : `${formatDate(b.fromDate)} to ${formatDate(b.toDate)}`
    const ok = await confirm({
        title: 'Void this export file?',
        message: 'Only void a file you did NOT import (or whose entries you deleted in QuickBooks Desktop). '
            + `Its days (${range}) go back into the next export, so if it was imported they would be sent twice.`,
        confirmText: 'Void file',
        confirmColor: 'error',
    })
    if (!ok) return
    voiding.value = b.id
    try {
        await service.desktopVoid(b.id)
        toast('File voided. Its days will be in your next export.')
        await load()
    } catch (err: any) {
        toast(errText(err, 'Could not void that export file.'), 'error')
    } finally {
        voiding.value = null
    }
}

async function removeDesktop() {
    const ok = await confirm({
        title: 'Stop using QuickBooks Desktop?',
        message: 'You can then connect QuickBooks Online instead. Files already exported stay in your books, '
            + 'and days already exported are never sent again by either edition. Your uploaded lists and '
            + 'mapping are kept in case you come back.',
        confirmText: 'Stop using Desktop',
        confirmColor: 'error',
    })
    if (!ok) return
    disconnectLoading.value = true
    try {
        await service.desktopRemove()
        classesLoaded.value = false
        classSettings.value = { trackingEnabled: true, trackingPerLine: true, classes: [] }
        classMappings.value = []
        batches.value = []
        toast('QuickBooks Desktop switched off.')
        await load()
    } catch (err: any) {
        toast(errText(err, 'Could not switch off QuickBooks Desktop. Please try again.'), 'error')
    } finally {
        disconnectLoading.value = false
    }
}

async function saveMappings() {
    saveLoading.value = true
    try {
        // A slot inside a profit center takes the CENTER's chosen account; a center left blank
        // keeps each slot's existing value (so an unresolved "mixed" center is never clobbered).
        const centerByKey = new Map<string, string>()
        for (const g of centerGroups.value) {
            for (const r of g.rows) centerByKey.set(r.mappingKey, g.center.id)
        }
        await service.saveMappings(mappings.value.map(m => {
            const centerId = centerByKey.get(m.mappingKey)
            const accountId = (centerId && centerAccounts.value[centerId]) || m.qboAccountId
            return {
                mappingKey: m.mappingKey,
                qboAccountId: accountId,
                qboAccountName: accounts.value.find(a => a.id === accountId)?.name ?? null,
            }
        }))
        toast('Account mapping saved.')
        await load()
    } catch (err: any) {
        toast(errText(err, 'Could not save the account mapping.'), 'error')
    } finally {
        saveLoading.value = false
    }
}

onMounted(async () => {
    // The OAuth callback lands on the apex and bounces back here with a result flag. Consume it and
    // strip it from the URL so a refresh doesn't re-announce a stale outcome.
    const connected = route.query.qboConnected
    const oauthError = route.query.qboError
    if (connected || oauthError) {
        await router.replace({ query: {} })
        if (connected) toast('QuickBooks connected. Map your accounts in the Chart of accounts tab to start syncing.')
        if (oauthError) toast(String(oauthError), 'error')
    }
    await load()
    // Fresh from the OAuth callback, the next step is mapping accounts; put the user on that tab.
    if (connected && status.value.isConnected && !status.value.mappingComplete) tab.value = 'accounts'
})
</script>

<script lang="ts">
export default { name: 'AdminSettingsQuickBooks' }
</script>

<style scoped>
/* Wide table must scroll inside its own box rather than the page scrolling sideways. */
.table-scroll {
    overflow-x: auto;
}

/* Profit-center color beside its account picker. Sized to sit level with a compact v-select
   that carries a persistent hint, so the row reads as one line. */
.swatch {
    display: inline-block;
    flex: 0 0 auto;
    width: 14px;
    height: 14px;
    border-radius: 4px;
    margin-bottom: 22px;
    box-shadow: inset 0 0 0 1px rgba(var(--v-theme-on-surface), 0.2);
}
</style>
