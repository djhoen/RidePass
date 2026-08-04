<template>
    <v-container>
        <div class="d-flex align-center mb-4 ga-3 flex-wrap">
            <h1 class="text-h4">Rentals</h1>
            <v-spacer></v-spacer>
            <ShopDisplayButton />
            <v-btn color="primary" prepend-icon="mdi-plus" :loading="openingBook" @click="openBook">New rental</v-btn>
        </div>

        <v-tabs v-model="tab" class="mb-4">
            <v-tab value="board">Board</v-tab>
            <v-tab value="bookings">All Bookings</v-tab>
            <v-tab value="fleet">Rental products</v-tab>
            <v-tab value="settings">Settings</v-tab>
        </v-tabs>

        <!-- ── Board ────────────────────────────────────────────────────── -->
        <!-- Kept alive so flipping to another tab and back doesn't re-fetch the day and lose the
             date, hour range, and filters the user had set up. -->
        <keep-alive>
            <RentalBoardPanel v-if="tab === 'board'" ref="boardPanel" @changed="onBookingsChanged" />
        </keep-alive>

        <!-- ── Settings ─────────────────────────────────────────────────── -->
        <div v-if="tab === 'settings'">
            <v-card class="pa-4" max-width="720">
                <div class="text-subtitle-1 mb-1">Service fee</div>
                <p class="text-caption text-medium-emphasis mb-4">
                    Rentals carry the same {{ (branding.serviceChargeBps / 100).toFixed(2) }}% service fee as
                    events and everything else you sell. That rate is set once for the whole track in
                    <router-link to="/Admin/Settings/General">Settings</router-link>; there's no per-product rate.
                    All you choose here is who pays it. The refundable deposit is never included in the fee.
                </p>

                <v-slider v-model="feeSplitPct" :min="0" :max="100" :step="5" thumb-label
                    color="primary" label="Renter pays" class="mt-6">
                    <template #append>
                        <span style="min-width: 52px" class="text-right">{{ feeSplitPct }}%</span>
                    </template>
                </v-slider>

                <v-alert type="info" variant="tonal" density="compact" class="mb-4">
                    <template v-if="feeSplitPct === 100">
                        The renter pays the whole fee. It shows as a "Service fee" line at checkout.
                    </template>
                    <template v-else-if="feeSplitPct === 0">
                        You absorb the whole fee. The renter sees no fee line, and it comes out of your
                        rental revenue instead.
                    </template>
                    <template v-else>
                        The renter pays {{ feeSplitPct }}% of the fee and you absorb the rest.
                    </template>
                </v-alert>

                <!-- Worked example, because a bps split is hard to reason about in the abstract. -->
                <v-table density="compact" class="mb-4">
                    <thead>
                        <tr><th>On a {{ money(exampleRentalCents) }} rental</th><th class="text-right">Amount</th></tr>
                    </thead>
                    <tbody>
                        <tr><td>Renter is charged</td><td class="text-right">{{ money(exampleRentalCents + exampleRenterFee) }}</td></tr>
                        <tr><td>Service fee total</td><td class="text-right">{{ money(exampleServiceCharge) }}</td></tr>
                        <tr>
                            <td class="text-medium-emphasis">— paid by renter</td>
                            <td class="text-right text-medium-emphasis">{{ money(exampleRenterFee) }}</td>
                        </tr>
                        <tr>
                            <td class="text-medium-emphasis">— absorbed by you</td>
                            <td class="text-right text-medium-emphasis">{{ money(exampleServiceCharge - exampleRenterFee) }}</td>
                        </tr>
                    </tbody>
                </v-table>

                <v-divider class="my-5"></v-divider>

                <div class="text-subtitle-1 mb-1">Sales tax</div>
                <p class="text-caption text-medium-emphasis mb-3">
                    Renting equipment is taxable in most US states. Set the rate that applies to
                    rentals at this track. The refundable deposit is never taxed.
                </p>

                <!-- The warning is the point of making the rate nullable: an unset rate is silent
                     under-collection, and that is the tenant's liability, not ours. -->
                <v-alert v-if="taxRateUnset" type="warning" variant="tonal" density="compact" class="mb-4">
                    <strong>No rental tax rate set.</strong>
                    Rentals are being booked with no sales tax. If rentals are taxable where you
                    operate, you're under-collecting. Enter your rate below, or enter 0 to confirm
                    rentals aren't taxed here.
                </v-alert>

                <div class="d-flex ga-3 align-start flex-wrap">
                    <v-text-field v-model="taxRatePct" type="number" min="0" max="100" step="0.01"
                        suffix="%" label="Rental tax rate" density="compact"
                        style="max-width: 200px" clearable
                        hint="Leave blank if you haven't determined it yet. Enter 0 for no tax."
                        persistent-hint></v-text-field>
                    <v-switch v-model="taxFeeTaxable" color="primary" density="compact"
                        label="Tax the service fee too" hide-details class="mt-1"></v-switch>
                </div>
                <p class="text-caption text-medium-emphasis mt-1 mb-4">
                    A mandatory fee on a taxable sale is generally taxable, which is why this is on
                    by default. Turn it off if your jurisdiction excludes it.
                </p>

                <v-alert v-if="!taxRateUnset" type="info" variant="tonal" density="compact" class="mb-4">
                    On a {{ money(exampleRentalCents) }} rental, tax is
                    {{ money(exampleTaxCents) }} and the renter pays
                    {{ money(exampleRentalCents + exampleRenterFee + exampleTaxCents) }} plus any deposit.
                </v-alert>

                <div class="d-flex">
                    <v-spacer></v-spacer>
                    <v-btn color="primary" :loading="savingSettings" :disabled="!settingsDirty"
                        @click="saveRentalSettings">Save</v-btn>
                </div>

                <v-divider class="my-5"></v-divider>

                <!-- Retail tax is configured elsewhere (per-product tax categories on the Bike Shop
                     page) and is easy to forget when you've just set the rental rate here. This is
                     status only: it never edits retail tax, it just stops one half being silently
                     unconfigured. -->
                <div class="text-subtitle-1 mb-1">Retail sales tax</div>
                <p class="text-caption text-medium-emphasis mb-3">
                    Selling in the shop is taxed separately from renting, using per-product tax
                    categories. Shown here so you can see both are set up.
                </p>

                <div v-if="loadingRetailTax" class="text-center py-3">
                    <v-progress-circular indeterminate size="20"></v-progress-circular>
                </div>
                <template v-else>
                    <!-- Don't assert "no sales tax" off a transient load failure: that reads as a
                         definitive misconfiguration when it's really that we couldn't check. -->
                    <v-alert v-if="retailTaxError" type="info" variant="tonal" density="compact" class="mb-2">
                        Couldn't check the retail tax setup. Reopen Settings to try again.
                    </v-alert>
                    <v-alert v-else-if="retailTaxCategories.length === 0" type="warning" variant="tonal"
                        density="compact" class="mb-2">
                        <strong>No retail tax categories.</strong>
                        Anything sold in the shop is being rung up with no sales tax.
                    </v-alert>
                    <v-alert v-else-if="!retailTaxDefault" type="warning" variant="tonal"
                        density="compact" class="mb-2">
                        <strong>No default retail tax category.</strong>
                        You have {{ retailTaxCategories.length }} categor{{ retailTaxCategories.length === 1 ? 'y' : 'ies' }},
                        but products without one assigned fall back to the default, and there isn't one,
                        so those sell untaxed.
                    </v-alert>
                    <v-alert v-else type="success" variant="tonal" density="compact" class="mb-2">
                        Retail tax is set up. Default is
                        <strong>{{ retailTaxDefault.name }} ({{ (retailTaxDefault.rateBps / 100).toFixed(2) }}%)</strong>,
                        across {{ retailTaxCategories.length }} categor{{ retailTaxCategories.length === 1 ? 'y' : 'ies' }}.
                    </v-alert>
                    <v-btn size="small" variant="text" prepend-icon="mdi-open-in-new"
                        to="/Admin/BikeShop/Settings?tab=tax">Manage retail tax categories</v-btn>
                </template>
            </v-card>
        </div>

        <!-- ── Rental products (the fleet) ──────────────────────────────── -->
        <!-- Every product flagged rentable, with what's free for the chosen window and what's
             already booked against it. A product that is also sellable still lives in the retail
             catalog on the Bike Shop page; here it appears as fleet. -->
        <div v-if="tab === 'fleet'">
            <div class="d-flex ga-2 mb-3 flex-wrap align-center">
                <v-text-field v-model="fleetFrom" type="datetime-local" label="From" density="compact"
                    hide-details style="max-width: 220px" @update:model-value="refreshFleetAvailability"></v-text-field>
                <v-text-field v-model="fleetTo" type="datetime-local" label="Until" density="compact"
                    hide-details style="max-width: 220px" @update:model-value="refreshFleetAvailability"></v-text-field>
                <v-text-field v-model="fleetSearch" density="compact" hide-details clearable
                    prepend-inner-icon="mdi-magnify" label="Search name or SKU" style="max-width: 280px"></v-text-field>
                <v-chip v-if="!fleetWindowValid" size="small" color="error" variant="tonal">
                    "Until" must be after "From"
                </v-chip>
                <v-spacer></v-spacer>
                <v-btn size="small" variant="tonal" prepend-icon="mdi-chart-timeline-variant"
                    to="/Admin/BikeShop/RentalBoard">See it on a timeline</v-btn>
            </div>

            <v-card>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th style="width: 40px"></th>
                            <th>Rental product</th>
                            <th style="width: 110px">Variants</th>
                            <th style="width: 150px" class="text-right">Available in window</th>
                            <th style="width: 150px"></th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-if="fleetProducts.length === 0">
                            <td colspan="5" class="text-center text-medium-emphasis py-6">
                                <!-- A failed catalog load must not read as "you own no bikes". -->
                                {{ !fleetLoaded
                                    ? 'Could not load the rental fleet. Reopen this tab to retry.'
                                    : fleetSearch
                                        ? 'No rental products match that search.'
                                        : 'No rental products yet. Flag a product as rentable on the Bike Shop page.' }}
                            </td>
                        </tr>
                        <template v-for="p in fleetProducts" :key="p.id">
                            <tr class="fleet-row" @click="toggleFleet(p.id)">
                                <td>
                                    <v-icon size="small">
                                        {{ fleetExpanded.has(p.id) ? 'mdi-chevron-down' : 'mdi-chevron-right' }}
                                    </v-icon>
                                </td>
                                <td>
                                    <div class="d-flex align-center ga-2">
                                        <strong>{{ p.name }}</strong>
                                        <v-chip v-if="p.isSellable" size="x-small" variant="tonal">Also sold</v-chip>
                                    </div>
                                    <div v-if="p.brand" class="text-caption text-medium-emphasis">{{ p.brand }}</div>
                                </td>
                                <td class="text-caption text-medium-emphasis">{{ rentableVariantsOf(p).length }}</td>
                                <td class="text-right">
                                    <template v-if="loadingFleetAvailability">
                                        <v-progress-circular indeterminate size="16" width="2"></v-progress-circular>
                                    </template>
                                    <template v-else>
                                        {{ productAvailable(p) ?? '?' }}
                                        <v-chip v-if="productAvailable(p) === 0" size="x-small" color="error" class="ml-1">Booked</v-chip>
                                    </template>
                                </td>
                                <td class="text-right">
                                    <v-btn size="x-small" color="primary" variant="tonal"
                                        :disabled="!fleetWindowValid || productAvailable(p) === 0"
                                        @click.stop="startRentalForProduct(p)">Start rental</v-btn>
                                </td>
                            </tr>
                            <tr v-if="fleetExpanded.has(p.id)" class="fleet-expanded">
                                <td colspan="5" class="pa-0">
                                    <div class="pa-4">
                                        <v-table density="compact">
                                            <thead>
                                                <tr>
                                                    <th>Variant</th><th>SKU</th>
                                                    <th class="text-right">Rate / day</th>
                                                    <th class="text-right">Deposit</th>
                                                    <th class="text-right">Available</th>
                                                    <th style="width: 130px"></th>
                                                </tr>
                                            </thead>
                                            <tbody>
                                                <tr v-for="v in rentableVariantsOf(p)" :key="v.id">
                                                    <td>{{ variantLabelOf(v) || '(default)' }}</td>
                                                    <td class="text-caption">{{ v.sku || '—' }}</td>
                                                    <td class="text-right">{{ v.dailyRateCents != null ? money(v.dailyRateCents) : '—' }}</td>
                                                    <td class="text-right">{{ v.depositCents ? money(v.depositCents) : '—' }}</td>
                                                    <td class="text-right">{{ availabilityOf(v.id) ?? '?' }}</td>
                                                    <td class="text-right">
                                                        <v-btn size="x-small" color="primary" variant="text"
                                                            :disabled="!fleetWindowValid || availabilityOf(v.id) === 0"
                                                            @click="startRentalForVariant(v.id)">Start rental</v-btn>
                                                    </td>
                                                </tr>
                                            </tbody>
                                        </v-table>

                                        <!-- Schedule: what's already on the books for this product. -->
                                        <div class="text-subtitle-2 mt-4 mb-1">Schedule</div>
                                        <div v-if="scheduleFor(p).length === 0" class="text-caption text-medium-emphasis">
                                            Nothing booked.
                                        </div>
                                        <v-table v-else density="compact">
                                            <thead>
                                                <tr>
                                                    <th>Window</th><th>Renter</th><th>Unit</th><th>Status</th>
                                                </tr>
                                            </thead>
                                            <tbody>
                                                <tr v-for="s in scheduleFor(p)" :key="s.key">
                                                    <td>{{ s.window }}</td>
                                                    <td>{{ s.renter }}</td>
                                                    <td class="text-caption">{{ s.unit }}</td>
                                                    <td>
                                                        <v-chip size="x-small" variant="tonal">{{ s.status }}</v-chip>
                                                    </td>
                                                </tr>
                                            </tbody>
                                        </v-table>
                                    </div>
                                </td>
                            </tr>
                        </template>
                    </tbody>
                </v-table>
            </v-card>
        </div>

        <!-- ── All Bookings ─────────────────────────────────────────────── -->
        <div v-if="tab === 'bookings'">
            <v-card class="pa-3 mb-4">
                <div class="d-flex ga-3 align-center flex-wrap">
                    <!-- Upcoming first, because "what's coming" is the standing question and
                         history is what you go looking for. -->
                    <v-btn-toggle v-model="scope" mandatory density="compact" variant="outlined"
                        color="primary" @update:model-value="applyFilters">
                        <v-btn value="upcoming" size="small">Upcoming</v-btn>
                        <v-btn value="past" size="small">Past</v-btn>
                        <v-btn value="all" size="small">All</v-btn>
                    </v-btn-toggle>

                    <!-- Applies as you type (debounced in the watcher); Enter just skips the wait. -->
                    <v-text-field v-model="search" density="compact" hide-details clearable
                        prepend-inner-icon="mdi-magnify" label="Renter name, email or order #"
                        style="max-width: 280px" @keyup.enter="applyFilters"></v-text-field>

                    <!-- Reload is driven by a debounced watcher, not @update:menu: chips are
                         closable, and removing one never opens or closes the menu. -->
                    <v-select v-model="statusFilter" :items="statusOptions" multiple chips closable-chips
                        density="compact" hide-details label="Status" style="max-width: 280px"></v-select>

                    <v-text-field v-model="fromDate" type="date" label="From" density="compact"
                        hide-details style="max-width: 165px" @update:model-value="applyFilters"></v-text-field>
                    <v-text-field v-model="toDate" type="date" label="Until" density="compact"
                        hide-details style="max-width: 165px" @update:model-value="applyFilters"></v-text-field>

                    <v-btn v-if="filtersActive" size="small" variant="text" prepend-icon="mdi-filter-off"
                        @click="clearFilters">Clear</v-btn>

                    <v-spacer></v-spacer>
                    <v-tooltip text="Reload" location="top">
                        <template #activator="{ props }">
                            <v-btn v-bind="props" icon="mdi-refresh" variant="text" size="small"
                                :loading="loading" @click="loadBookings"></v-btn>
                        </template>
                    </v-tooltip>
                </div>

                <div v-if="!loadError" class="d-flex ga-2 mt-3 flex-wrap align-center">
                    <v-chip size="small" variant="tonal">
                        {{ total }} booking{{ total === 1 ? '' : 's' }}{{ scopeLabel }}
                    </v-chip>
                    <v-chip v-if="outNowCount > 0" size="small" color="indigo" variant="tonal">
                        {{ outNowCount }} out on this page
                    </v-chip>
                    <v-chip v-if="depositHeldCents > 0" size="small" variant="tonal">
                        {{ money(depositHeldCents) }} deposits held on this page
                    </v-chip>
                </div>
            </v-card>

            <v-card v-if="loading && rentals.length === 0" class="pa-6 text-center">
                <v-progress-circular indeterminate color="primary" />
            </v-card>
            <v-alert v-else-if="loadError" type="error" variant="tonal">
                {{ loadError }}
                <template #append>
                    <v-btn size="small" variant="text" @click="loadBookings">Retry</v-btn>
                </template>
            </v-alert>
            <v-card v-else-if="rentals.length === 0" class="pa-6 text-center text-medium-emphasis">
                <template v-if="filtersActive">
                    No bookings match those filters.
                    <v-btn size="small" variant="text" class="ml-2" @click="clearFilters">Clear filters</v-btn>
                </template>
                <template v-else-if="scope === 'upcoming'">
                    Nothing booked from here on. Switch to Past to see history, or book one.
                </template>
                <template v-else>No rentals yet. Book the first one.</template>
            </v-card>

            <template v-else>
                <v-card>
                    <v-table density="compact">
                        <thead>
                            <tr>
                                <th>#</th><th>Renter</th><th>Window</th><th>Items</th>
                                <th class="text-right">Total</th><th class="text-right">Deposit</th>
                                <th>Status</th><th></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="r in rentals" :key="r.id" :class="{ 'row-past': isPast(r) }">
                                <td>{{ r.orderNumber ?? '—' }}</td>
                                <td>
                                    <div>{{ r.renterName || 'Walk-in' }}</div>
                                    <div v-if="r.renterEmail" class="text-caption text-medium-emphasis">
                                        {{ r.renterEmail }}
                                    </div>
                                </td>
                                <td class="text-caption">
                                    <div>{{ windowLabel(r) }}</div>
                                    <!-- Relative time is what makes a long list scannable: "in 2 hours"
                                         reads faster than a date you have to compare against today. -->
                                    <div class="text-medium-emphasis">{{ relativeLabel(r) }}</div>
                                </td>
                                <td class="text-caption">{{ itemsLabel(r) }}</td>
                                <td class="text-right">{{ money(r.totalCents) }}</td>
                                <td class="text-right">
                                    {{ money(r.depositCents) }}
                                    <span v-if="r.depositCapturedCents > 0" class="text-error text-caption">
                                        (-{{ money(r.depositCapturedCents) }})
                                    </span>
                                </td>
                                <td><v-chip size="x-small" :color="statusColor(r.status)">{{ r.status }}</v-chip></td>
                                <td class="text-right" style="white-space: nowrap">
                                    <v-btn v-if="r.status === 'paid'" size="x-small" color="primary" variant="tonal"
                                        :loading="busyId === r.id" @click="checkOut(r)">Check out</v-btn>
                                    <v-tooltip v-if="r.status === 'paid' || r.status === 'pending'" text="Agreement + waiver" location="top">
                                        <template #activator="{ props }">
                                            <v-btn v-bind="props" size="x-small" variant="text"
                                                icon="mdi-draw-pen" @click="openSign(r)"></v-btn>
                                        </template>
                                    </v-tooltip>
                                    <v-btn v-if="r.status === 'out'" size="x-small" color="secondary" variant="tonal"
                                        @click="openReturn(r)">Return</v-btn>
                                    <v-tooltip text="Condition photos" location="top">
                                        <template #activator="{ props }">
                                            <v-btn v-bind="props" size="x-small" variant="text" icon="mdi-camera"
                                                @click="openPhotos(r)"></v-btn>
                                        </template>
                                    </v-tooltip>
                                    <v-tooltip v-if="r.status === 'pending' || r.status === 'paid'" text="Cancel" location="top">
                                        <template #activator="{ props }">
                                            <v-btn v-bind="props" size="x-small" variant="text"
                                                icon="mdi-close" @click="cancel(r)"></v-btn>
                                        </template>
                                    </v-tooltip>
                                </td>
                            </tr>
                        </tbody>
                    </v-table>
                </v-card>

                <div v-if="pageCount > 1" class="d-flex justify-center mt-4">
                    <v-pagination v-model="page" :length="pageCount" :total-visible="7"
                        density="compact" @update:model-value="loadBookings"></v-pagination>
                </div>
            </template>
        </div>

        <!-- ── Book + return, shared with the Rental Board ──────────────── -->
        <BookRentalDialog v-model="bookOpen" :rentable-variants="rentableVariants" :preset="bookPreset"
            @booked="onBookingsChanged" @notify="flash" />

        <ReturnRentalDialog v-model="returnOpen" :rental="returning" @returned="onReturned" />

        <!-- ── Rental agreement ────────────────────────────────────────── -->
        <v-dialog v-model="signOpen" max-width="560">
            <v-card v-if="signFor">
                <v-card-title class="d-flex align-center">
                    <span class="text-body-1">Before check out</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="signOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-caption text-medium-emphasis mb-3">
                        Signed on this device at pickup. Gear can't be checked out until both the
                        agreement and the waiver are signed.
                    </p>
                    <RentalReadinessPanel :rental-id="signFor.id"
                        :renter-name="signFor.renterName" :renter-email="signFor.renterEmail" />
                </v-card-text>
            </v-card>
        </v-dialog>

        <!-- ── Condition photos ────────────────────────────────────────── -->
        <v-dialog v-model="photosOpen" max-width="720">
            <v-card v-if="photosFor">
                <v-card-title class="d-flex align-center">
                    <span>Condition photos</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="photosOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <ConditionPhotos :rental-id="photosFor.id" stage="intake"
                        title="Going out"
                        hint="Photograph the gear before it leaves, especially any existing damage." />
                    <v-divider class="my-4"></v-divider>
                    <ConditionPhotos :rental-id="photosFor.id" stage="return"
                        title="Coming back"
                        hint="Photograph anything damaged on return." />
                    <v-divider class="my-4"></v-divider>
                    <PhotoQrPanel kind="rental" :id="photosFor.id" />
                </v-card-text>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackColor" :timeout="3500">{{ snackText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import { formatTenantDateTime, tenantWallClockNow, tenantWallClockToMs } from '@/helpers/TenantTime'
import { BikeShopService, type ShopProduct, type ShopRental, type ShopTaxCategory } from '@/services/BikeShopService'
import ConditionPhotos from '@/components/bikeshop/ConditionPhotos.vue'
import PhotoQrPanel from '@/components/bikeshop/PhotoQrPanel.vue'
import RentalReadinessPanel from '@/components/bikeshop/RentalReadinessPanel.vue'
import ShopDisplayButton from '@/components/bikeshop/ShopDisplayButton.vue'
import BookRentalDialog, { type BookRentalPreset } from '@/components/bikeshop/BookRentalDialog.vue'
import ReturnRentalDialog from '@/components/bikeshop/ReturnRentalDialog.vue'
import RentalBoardPanel from '@/components/bikeshop/RentalBoardPanel.vue'
import { branding } from '@/stores/branding'
import { TenantService } from '@/services/TenantService'
import { useConfirm } from '@/composables/useConfirm'

const service = new BikeShopService()
const confirm = useConfirm()

// Rental agreement signing, on the shop's tablet with the renter present.
const signOpen = ref(false)
const signFor = ref<ShopRental | null>(null)
function openSign(r: ShopRental) {
    signFor.value = r
    signOpen.value = true
}
// Condition photos: reachable any time from the row, and surfaced again inside the return
// dialog where a damage capture is actually decided.
const photosOpen = ref(false)
const photosFor = ref<ShopRental | null>(null)
function openPhotos(r: ShopRental) {
    photosFor.value = r
    photosOpen.value = true
}

const rentals = ref<ShopRental[]>([])
const products = ref<ShopProduct[]>([])
const loading = ref(false)
const loadError = ref('')
const busyId = ref<string | null>(null)

// ── All Bookings: filters ───────────────────────────────────────────────────
// Scope defaults to upcoming. History is one click away rather than the thing you scroll past to
// find tomorrow's pickup, which is what an undated list of everything turns into.
type RentalScope = 'upcoming' | 'past' | 'all'
const scope = ref<RentalScope>('upcoming')
const search = ref('')
const statusFilter = ref<string[]>([])
const fromDate = ref('')
const toDate = ref('')
const page = ref(1)
const total = ref(0)
const pageSize = 25

const statusOptions = [
    { title: 'Pending payment', value: 'pending' },
    { title: 'Paid', value: 'paid' },
    { title: 'Out', value: 'out' },
    { title: 'Returned', value: 'returned' },
    { title: 'Damaged', value: 'damaged' },
    { title: 'Cancelled', value: 'cancelled' },
    { title: 'Failed', value: 'failed' },
]

const pageCount = computed(() => Math.max(1, Math.ceil(total.value / pageSize)))
const filtersActive = computed(() =>
    !!search.value?.trim() || statusFilter.value.length > 0 || !!fromDate.value || !!toDate.value)
const scopeLabel = computed(() =>
    scope.value === 'upcoming' ? ' from here on' : scope.value === 'past' ? ' in the past' : '')

// Page-level tallies, labelled as such. Summing the whole filtered set would need another query,
// and quietly presenting a page total as a grand total is worse than not showing one.
const outNowCount = computed(() => rentals.value.filter(r => r.status === 'out').length)
const depositHeldCents = computed(() => rentals.value
    .filter(r => r.status === 'out' || r.status === 'paid')
    .reduce((sum, r) => sum + Math.max(0, r.depositCents - r.depositCapturedCents), 0))

function isPast(r: ShopRental): boolean {
    return dayjs(r.endsAt).isBefore(dayjs())
}
/**
 * "in 2 hours" / "3 days ago". Hand-rolled rather than pulling in dayjs' relativeTime plugin: it
 * is a handful of lines, it keeps the plugin registration in main.ts untouched, and the wording
 * here wants to be specific to a rental ("due back", not a bare "in 2 hours").
 */
function fromNow(iso: string): string {
    const mins = Math.round((new Date(iso).getTime() - Date.now()) / 60000)
    const ago = mins < 0
    const m = Math.abs(mins)
    const said = m < 1 ? 'now'
        : m < 60 ? `${m} min`
        : m < 60 * 36 ? `${Math.round(m / 60)} hr`
        : `${Math.round(m / 1440)} days`
    if (said === 'now') return 'now'
    return ago ? `${said} ago` : `in ${said}`
}

/** Relative time against whichever end of the window is the live one. */
function relativeLabel(r: ShopRental): string {
    const now = Date.now()
    if (new Date(r.startsAt).getTime() > now) return `starts ${fromNow(r.startsAt)}`
    if (new Date(r.endsAt).getTime() > now) return `due back ${fromNow(r.endsAt)}`
    return `ended ${fromNow(r.endsAt)}`
}

// Any filter change resets to page 1: staying on page 4 of a result set that just shrank to one
// page shows an empty table and reads as "no matches".
let filterTimer: number | undefined
function applyFilters() {
    // Cancel any debounced run this one supersedes, so an explicit apply (Enter, Clear, a scope
    // change) doesn't get followed by a redundant fetch 350ms later.
    if (filterTimer) { window.clearTimeout(filterTimer); filterTimer = undefined }
    page.value = 1
    loadBookings()
}
function clearFilters() {
    search.value = ''
    statusFilter.value = []
    fromDate.value = ''
    toDate.value = ''
    applyFilters()
}

// Search and status apply as you type / click, debounced. Enter still works for anyone who
// expects it; this just means someone who types a name and then looks up at the table sees
// results rather than a list that never moved. Debouncing status as well coalesces the burst of
// changes from ticking several boxes in one visit to the menu, and unlike an on-menu-close hook
// it also catches a chip being removed. The stale-response guard in loadBookings makes the
// overlapping calls safe.
function debouncedApply() {
    if (filterTimer) window.clearTimeout(filterTimer)
    filterTimer = window.setTimeout(applyFilters, 350)
}
watch(search, debouncedApply)
watch(statusFilter, debouncedApply, { deep: true })
onBeforeUnmount(() => { if (filterTimer) window.clearTimeout(filterTimer) })

const snackbar = ref(false); const snackText = ref(''); const snackColor = ref<'success' | 'error'>('success')
function flash(t: string, c: 'success' | 'error' = 'success') { snackText.value = t; snackColor.value = c; snackbar.value = true }
function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }
function windowLabel(r: ShopRental): string {
    return `${formatTenantDateTime(r.startsAt, 'MMM D h:mm A')} – ${formatTenantDateTime(r.endsAt, 'MMM D h:mm A')}`
}
function itemsLabel(r: ShopRental): string {
    return r.lines.map(l => `${l.nameSnapshot}${l.quantity > 1 ? ' ×' + l.quantity : ''}`).join(', ')
}
function statusColor(s: string) {
    return s === 'paid' ? 'primary' : s === 'out' ? 'indigo' : s === 'returned' ? 'success'
        : s === 'damaged' ? 'warning' : s === 'pending' ? 'grey' : 'error'
}

// ── Booking ────────────────────────────────────────────────────────────────
// The dialog itself (and the Stripe fee-then-deposit flow) lives in BookRentalDialog so the
// Rental Board can open the same thing against a resource the user clicked.
const bookOpen = ref(false)
const bookPreset = ref<BookRentalPreset | null>(null)

// ── Settings tab: who funds the rental service fee ──────────────────────────
const tenantService = new TenantService()
const savingSettings = ref(false)
// Percent (not bps) for the slider; converted back on save.
const feeSplitPct = ref(Math.round((branding.rentalRiderPaidServiceChargeBps ?? 10000) / 100))
const feeSplitDirty = computed(() =>
    feeSplitPct.value * 100 !== (branding.rentalRiderPaidServiceChargeBps ?? 10000))

// Tax rate as a percent string so the field can be genuinely EMPTY (not configured) rather than
// forced to 0. Empty and 0 mean different things: 0 is "confirmed untaxed", empty is "unknown".
const taxRatePct = ref<string | number | null>(
    branding.rentalTaxBps == null ? null : branding.rentalTaxBps / 100)
const taxFeeTaxable = ref(branding.rentalTaxServiceChargeTaxable ?? true)

function pctToBps(v: string | number | null): number | null {
    if (v === null || v === undefined || String(v).trim() === '') return null
    const n = typeof v === 'number' ? v : parseFloat(String(v))
    if (!Number.isFinite(n) || n < 0) return null
    return Math.round(n * 100)
}
const taxBpsValue = computed(() => pctToBps(taxRatePct.value))
// Warn on the SAVED value, not the in-progress edit, so the banner reflects reality.
const taxRateUnset = computed(() => branding.rentalTaxBps == null)

const exampleTaxCents = computed(() => {
    const base = exampleRentalCents + (taxFeeTaxable.value ? exampleRenterFee.value : 0)
    return Math.round((base * (taxBpsValue.value ?? 0)) / 10000)
})

// Retail tax status (read-only mirror of the Bike Shop page's Tax tab).
const retailTaxCategories = ref<ShopTaxCategory[]>([])
const retailTaxError = ref(false)
const loadingRetailTax = ref(false)
// A category only counts if it is active; an inactive default taxes nothing.
const retailTaxDefault = computed(() =>
    retailTaxCategories.value.find(c => c.isDefault && c.isActive) ?? null)

async function loadRetailTax() {
    loadingRetailTax.value = true
    retailTaxError.value = false
    try {
        const r = await service.listTaxCategories()
        retailTaxCategories.value = (r.data as any).data.filter((c: ShopTaxCategory) => c.isActive)
    } catch (e: any) {
        // Leave the list as-is and flag the error so the UI shows "couldn't check" rather than
        // falsely asserting the shop has no sales tax configured.
        retailTaxError.value = true
        flash(e.response?.data?.error || 'Could not check the retail tax setup. Reopen Settings to retry.', 'error')
    } finally {
        loadingRetailTax.value = false
    }
}

const settingsDirty = computed(() =>
    feeSplitDirty.value
    || taxBpsValue.value !== (branding.rentalTaxBps ?? null)
    || taxFeeTaxable.value !== (branding.rentalTaxServiceChargeTaxable ?? true))

// Worked example on a round number, using the same floor-then-floor math as the server.
const exampleRentalCents = 10000
const exampleServiceCharge = computed(() =>
    Math.floor((exampleRentalCents * (branding.serviceChargeBps ?? 0)) / 10000))
const exampleRenterFee = computed(() =>
    Math.floor((exampleServiceCharge.value * feeSplitPct.value * 100) / 10000))

async function saveRentalSettings() {
    savingSettings.value = true
    try {
        const taxBps = taxBpsValue.value
        await tenantService.updateRentalSettings({
            riderPaidBps: feeSplitPct.value * 100,
            taxBps,
            serviceChargeTaxable: taxFeeTaxable.value,
        })
        branding.rentalRiderPaidServiceChargeBps = feeSplitPct.value * 100
        branding.rentalTaxBps = taxBps
        branding.rentalTaxServiceChargeTaxable = taxFeeTaxable.value
        flash('Rental settings saved.', 'success')
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not save the rental settings. Please try again.', 'error')
    } finally {
        savingSettings.value = false
    }
}

// ── Fleet tab: rental products with availability + schedule ─────────────────
// ?tab= opens a specific tab, so links from elsewhere (Shop Settings, the booking dialog's
// "set the tax rate" prompt) land on the right one instead of dumping you on Bookings.
// Board first and by default: "what is on the rack right now" is the question the counter opens
// this screen to answer. The old default (an undated list of every booking) made them read to
// find it. ?tab= still overrides, which is what the retired /RentalBoard URL redirects into.
const validTabs = ['board', 'bookings', 'fleet', 'settings'] as const
type RentalTab = typeof validTabs[number]
const route = useRoute()
const tab = ref<RentalTab>(
    validTabs.includes(route.query.tab as RentalTab) ? route.query.tab as RentalTab : 'board')
watch(() => route.query.tab, t => {
    if (validTabs.includes(t as RentalTab)) tab.value = t as RentalTab
})
// Wall clock AT THE TRACK, not in the browser, matching the Rental Board and the booking dialog.
// These values are handed straight to the dialog as a preset, so if this seeded browser-local
// while the dialog read tenant-local, an operator in a different timezone from the track would
// probe availability for the wrong hours and be told available gear was taken.
const fleetFrom = ref(tenantWallClockNow())
const fleetTo = ref(dayjs(tenantWallClockNow()).add(1, 'day').format('YYYY-MM-DDTHH:mm'))
const fleetSearch = ref('')
const fleetExpanded = ref<Set<string>>(new Set())
// variantId -> units free across the chosen window. A missing key means "not known" (the probe
// failed), which renders as "?" rather than 0 — claiming a bike is booked when we simply could not
// check would send staff to turn away a paying customer.
const fleetAvailability = ref<Record<string, number>>({})
const loadingFleetAvailability = ref(false)

const fleetWindowValid = computed(() =>
    !!fleetFrom.value && !!fleetTo.value
    && tenantWallClockToMs(fleetTo.value) > tenantWallClockToMs(fleetFrom.value))

// The fleet is small (tens of units), and the page already holds the full product list for the
// booking dialog, so this filters in memory rather than paging the server. Switch to
// BikeShop/Products/Page?rentable=true if a shop ever runs a fleet big enough to need it.
const fleetProducts = computed(() => {
    const term = fleetSearch.value?.trim().toLowerCase()
    return products.value
        .filter(p => p.isActive && p.isRentable && rentableVariantsOf(p).length > 0)
        .filter(p => !term
            || p.name.toLowerCase().includes(term)
            || (p.brand ?? '').toLowerCase().includes(term)
            || p.variants.some(v => (v.sku ?? '').toLowerCase().includes(term)
                || (v.barcode ?? '').toLowerCase().includes(term)))
        .sort((a, b) => a.name.localeCompare(b.name))
})

function rentableVariantsOf(p: ShopProduct) {
    return p.variants.filter(v => v.isActive && v.dailyRateCents != null)
}
function variantLabelOf(v: { size: string | null; color: string | null; gender: string | null }) {
    return [v.size, v.color, v.gender].filter(Boolean).join(' / ')
}
function availabilityOf(variantId: string): number | null {
    const n = fleetAvailability.value[variantId]
    return n === undefined ? null : n
}
// Null if any variant's availability is unknown, so the row shows "?" instead of a total that
// silently omits the part we failed to check.
function productAvailable(p: ShopProduct): number | null {
    const vals = rentableVariantsOf(p).map(v => availabilityOf(v.id))
    if (vals.length === 0 || vals.some(v => v === null)) return null
    return vals.reduce((sum: number, v) => sum + (v as number), 0)
}
function toggleFleet(id: string) {
    const next = new Set(fleetExpanded.value)
    if (next.has(id)) next.delete(id); else next.add(id)
    fleetExpanded.value = next
}

// What's already on the books for this product. Reads fleetRentals, NOT the bookings page: that
// list is filtered and paged now, so using it would make this schedule reflect whatever the user
// last searched for rather than what is actually booked.
function scheduleFor(p: ShopProduct) {
    const variantIds = new Set(rentableVariantsOf(p).map(v => v.id))
    const out: { key: string; window: string; renter: string; unit: string; status: string }[] = []
    for (const r of fleetRentals.value) {
        if (r.status === 'cancelled' || r.status === 'failed' || r.status === 'returned') continue
        for (const l of r.lines) {
            if (!variantIds.has(l.variantId)) continue
            out.push({
                key: `${r.id}:${l.id}`,
                window: `${formatTenantDateTime(r.startsAt, 'MMM D h:mm A')} – ${formatTenantDateTime(r.endsAt, 'MMM D h:mm A')}`,
                renter: r.renterName || r.renterEmail || 'Walk-up',
                unit: l.variantLabel || (l.quantity > 1 ? `×${l.quantity}` : ''),
                status: r.status,
            })
        }
    }
    return out.sort((a, b) => a.window.localeCompare(b.window))
}

// One availability probe per rentable variant for the chosen window. Runs in parallel; a fleet is
// small enough that this stays a handful of requests.
async function refreshFleetAvailability() {
    if (!fleetWindowValid.value) { fleetAvailability.value = {}; return }
    const variants = fleetProducts.value.flatMap(rentableVariantsOf)
    if (variants.length === 0) { fleetAvailability.value = {}; return }
    loadingFleetAvailability.value = true
    const from = new Date(tenantWallClockToMs(fleetFrom.value)).toISOString()
    const to = new Date(tenantWallClockToMs(fleetTo.value)).toISOString()
    try {
        let failed = 0
        const results = await Promise.all(variants.map(async v => {
            try {
                const r = await service.rentalAvailability(v.id, from, to)
                return [v.id, r.data.data.available] as const
            } catch {
                // One probe failing must not blank the grid, but it must not read as "0 free"
                // either: omit the key so the row shows "?".
                failed++
                return null
            }
        }))
        fleetAvailability.value = Object.fromEntries(results.filter(Boolean) as (readonly [string, number])[])
        if (failed > 0) {
            flash(`Couldn't check availability for ${failed} item${failed === 1 ? '' : 's'} — those show "?". Retry the window to refresh.`, 'error')
        }
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not check fleet availability for that window.', 'error')
    } finally {
        loadingFleetAvailability.value = false
    }
}

// Start a rental straight from the fleet list: opens the shared booking dialog with this
// window and the chosen bike already on it.
function startRentalForVariant(variantId: string) {
    bookPreset.value = { startsAt: fleetFrom.value, endsAt: fleetTo.value, variantId }
    bookOpen.value = true
}
function startRentalForProduct(p: ShopProduct) {
    const vs = rentableVariantsOf(p)
    // Prefer a variant known to be free; fall back to the first (the booking flow re-checks
    // availability server-side anyway, so an unknown is worth attempting).
    const first = vs.find(v => (availabilityOf(v.id) ?? 0) > 0) ?? vs[0]
    if (first) startRentalForVariant(first.id)
}

const rentableVariants = computed(() =>
    products.value.filter(p => p.isActive && p.isRentable).flatMap(p =>
        p.variants.filter(v => v.isActive && v.dailyRateCents != null).map(v => ({
            id: v.id,
            title: `${p.name}${[v.size, v.color].filter(Boolean).length ? ' (' + [v.size, v.color].filter(Boolean).join('/') + ')' : ''} — ${money(v.dailyRateCents!)}/day`,
            trackingKind: v.trackingKind, name: p.name, dailyRateCents: v.dailyRateCents!, depositCents: v.depositCents,
        }))))

const boardPanel = ref<InstanceType<typeof RentalBoardPanel> | null>(null)
const openingBook = ref(false)

async function openBook() {
    // On the Board tab, drive the panel's own dialog. It already holds the fleet it loaded, so
    // its gear picker is populated even for a shop-counter user who can't read the catalog
    // endpoints this page uses. Two dialogs mounted at once would also fight over Stripe's
    // payment element, which can only be mounted in one place.
    if (tab.value === 'board' && boardPanel.value) {
        boardPanel.value.openBlankBooking()
        return
    }
    // The catalog now loads with the Rental products tab rather than on mount, so from any other
    // tab it may not be here yet. Without this the dialog opens with an empty gear picker and no
    // hint as to why.
    if (!fleetLoaded.value) {
        openingBook.value = true
        try { await loadFleet() } finally { openingBook.value = false }
        if (!fleetLoaded.value) return   // loadFleet already surfaced the failure
    }
    bookPreset.value = null
    bookOpen.value = true
}

// ── Lifecycle actions ──────────────────────────────────────────────────────
async function checkOut(r: ShopRental) {
    busyId.value = r.id
    try {
        await service.checkOutRental(r.id)
        flash('Checked out — gear is on its way.')
        await onBookingsChanged()
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not check out this rental.', 'error')
    } finally { busyId.value = null }
}

const returnOpen = ref(false)
const returning = ref<ShopRental | null>(null)
function openReturn(r: ShopRental) {
    returning.value = r
    returnOpen.value = true
}
async function onReturned(capturedCents: number) {
    flash(capturedCents > 0
        ? `Returned — ${money(capturedCents)} kept from the deposit.`
        : 'Returned — deposit released in full.')
    await onBookingsChanged()
}

async function cancel(r: ShopRental) {
    const ok = await confirm({
        title: 'Cancel this rental?',
        message: r.status === 'paid'
            ? 'The deposit hold is released. The paid fee is NOT auto-refunded — refund it separately if owed.'
            : 'The pending booking is discarded.',
        confirmText: 'Cancel rental',
    })
    if (!ok) return
    try {
        await service.cancelRental(r.id)
        flash('Rental cancelled.')
        await onBookingsChanged()
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not cancel this rental.', 'error')
    }
}

// Bookings are paged and filtered server-side, so this is the only thing that fills `rentals`.
// Filter changes reset the page first; see applyFilters.
let bookingsSeq = 0
async function loadBookings() {
    const seq = ++bookingsSeq
    loading.value = true
    loadError.value = ''
    try {
        const r = await service.searchRentals({
            scope: scope.value,
            search: search.value?.trim() || null,
            statuses: statusFilter.value,
            // Date inputs are plain calendar days. Send the tenant-local day boundaries so a
            // "From 25th" means the 25th at the track, not 00:00 UTC (which is the 24th in Denver).
            from: fromDate.value ? dayjs.tz(`${fromDate.value}T00:00:00`, tenantZone()).toISOString() : null,
            to: toDate.value ? dayjs.tz(`${toDate.value}T00:00:00`, tenantZone()).add(1, 'day').toISOString() : null,
            page: page.value,
            pageSize,
        })
        // Stale-response guard: typing in the search box fires several of these, and an older
        // one landing last would show results for a query the user has already moved past.
        if (seq !== bookingsSeq) return
        rentals.value = r.data.data.rows
        total.value = r.data.data.total
    } catch (e: any) {
        if (seq !== bookingsSeq) return
        rentals.value = []
        total.value = 0
        loadError.value = e.response?.data?.error
            || 'Could not load bookings. Check the connection and retry.'
    } finally {
        if (seq === bookingsSeq) loading.value = false
    }
}

function tenantZone(): string { return branding.timezone || 'UTC' }

// The fleet tab needs the catalog plus every currently-live rental for its per-product schedule.
// Deliberately its own read: the bookings list is a filtered page now, so reusing it would make
// the schedule show only whatever the user happened to be filtering for.
const fleetLoaded = ref(false)
const fleetRentals = ref<ShopRental[]>([])
async function loadFleet() {
    try {
        const [r, p] = await Promise.all([service.listRentals(true, 500), service.listProducts(true)])
        fleetRentals.value = r.data.data
        products.value = p.data.data
        fleetLoaded.value = true
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not load the rental fleet. Reopen the tab to retry.', 'error')
    }
}

/** A booking changed somewhere (the board, a row action), so anything already loaded is stale. */
async function onBookingsChanged() {
    await loadBookings()
    if (fleetLoaded.value) await loadFleet()
}

// Each tab loads what it needs the first time it's opened, so landing on the Board doesn't pull
// the catalog and a full booking page nobody asked for.
watch(tab, t => {
    if (t === 'bookings' && rentals.value.length === 0 && !loadError.value) loadBookings()
    if (t === 'fleet') {
        if (!fleetLoaded.value) loadFleet(); else refreshFleetAvailability()
    }
    if (t === 'settings' && retailTaxCategories.value.length === 0) loadRetailTax()
})
watch(products, () => { if (tab.value === 'fleet') refreshFleetAvailability() })

onMounted(() => {
    // The board fetches its own data; only load for the tab actually showing.
    if (tab.value === 'bookings') loadBookings()
    if (tab.value === 'fleet') loadFleet()
    if (tab.value === 'settings') loadRetailTax()
})
</script>

<style scoped>
/* The whole fleet row is the expand affordance. */
.fleet-row {
    cursor: pointer;
}
.fleet-row:hover {
    background: rgba(var(--v-theme-on-surface), 0.04);
}
.fleet-expanded > td {
    background: rgba(var(--v-theme-on-surface), 0.02);
}
.sp-group-label {
    font-size: 13px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: rgba(var(--v-theme-on-surface), 0.6);
}
.pos-qty { min-width: 24px; text-align: center; font-variant-numeric: tabular-nums; }
</style>
