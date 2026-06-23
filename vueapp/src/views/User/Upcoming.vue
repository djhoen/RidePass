<template>
    <v-container>
        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <div v-if="loading && items.length === 0" class="text-center my-12">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <!-- Events: one card per event, split into upcoming + (toggleable) past. -->
        <section v-if="eventLikeItems.length > 0" class="mb-10">
            <div class="d-flex align-center mb-4">
                <h1 class="text-h5">My Upcoming Events</h1>
                <v-spacer></v-spacer>
                <v-btn v-if="pastEvents.length" variant="text" size="small" @click="showPast = !showPast">
                    {{ showPast ? 'Hide past events' : `Show past events (${pastEvents.length})` }}
                </v-btn>
            </div>

            <div v-if="upcomingEvents.length === 0" class="text-body-2 text-medium-emphasis mb-2">
                Nothing coming up , your past events are below.
            </div>
            <v-row>
                <v-col v-for="item in upcomingEvents" :key="item.kind + item.id" cols="12" sm="6" md="4">
                    <UpcomingEventCard :item="item" @order="openOrder" />
                </v-col>
            </v-row>

            <template v-if="showPast && pastEvents.length">
                <h3 class="text-subtitle-1 font-weight-bold mt-8 mb-3 text-medium-emphasis">Past events</h3>
                <v-row>
                    <v-col v-for="item in pastEvents" :key="item.kind + item.id" cols="12" sm="6" md="4">
                        <UpcomingEventCard :item="item" @order="openOrder" class="up-past" />
                    </v-col>
                </v-row>
            </template>
        </section>

        <!-- Active passes: season passes + memberships. Range-based. -->
        <section v-if="passLikeItems.length > 0" class="mb-10">
            <h2 class="text-h5 mb-4">Active passes and memberships</h2>
            <v-row>
                <v-col v-for="item in passLikeItems" :key="item.kind + item.id" cols="12" sm="6" md="4">
                    <a :href="tenantUserUrl(item)" rel="noopener" class="up-card-link">
                        <v-card class="h-100 up-card">
                            <div class="up-image up-image--pass">
                                <v-icon class="up-pass-icon" :icon="kindIcon(item.kind)" size="40"></v-icon>
                                <v-chip size="x-small" :color="kindChipColor(item.kind)" class="up-chip">{{ kindLabel(item.kind) }}</v-chip>
                            </div>
                            <v-card-text class="pa-3">
                                <div class="text-subtitle-1 font-weight-bold mb-1 text-truncate">{{ item.itemName }}</div>
                                <div class="text-caption text-medium-emphasis d-flex align-center ga-1">
                                    <v-icon icon="mdi-map-marker" size="14"></v-icon>
                                    <span class="text-truncate">{{ item.tenantDisplayName }}</span>
                                </div>
                                <div class="text-caption text-medium-emphasis d-flex align-center ga-1 mt-1">
                                    <v-icon icon="mdi-calendar-check" size="14"></v-icon>
                                    <span v-if="item.validToUtc">Valid through {{ formatDate(item.validToUtc) }}</span>
                                    <span v-else>Lifetime</span>
                                </div>
                            </v-card-text>
                        </v-card>
                    </a>
                </v-col>
            </v-row>
        </section>

        <v-card v-if="!loading && items.length === 0" variant="outlined">
            <v-card-text class="text-center text-medium-emphasis py-12">
                <v-icon icon="mdi-calendar-blank-outline" size="48" class="mb-3"></v-icon>
                <div class="mb-2">Nothing on your schedule yet.</div>
                <v-btn variant="tonal" to="/Discover" prepend-icon="mdi-map-search">Find a track</v-btn>
            </v-card-text>
        </v-card>

        <!-- Order detail (invoice) -->
        <v-dialog v-model="orderDialog" max-width="560">
            <v-card class="invoice">
                <v-card-title class="d-flex align-center">
                    <span class="text-truncate">Order detail</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="orderDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div v-if="orderLoading" class="text-center py-6">
                        <v-progress-circular indeterminate color="primary"></v-progress-circular>
                    </div>
                    <v-alert v-else-if="orderError" type="error" variant="tonal">{{ orderError }}</v-alert>
                    <div v-else-if="orderDetail && orderDetail.items.length === 0" class="text-medium-emphasis py-4">
                        No items found for this order.
                    </div>
                    <template v-else-if="orderDetail">
                        <!-- Invoice header -->
                        <div class="d-flex align-start mb-4">
                            <div class="flex-grow-1" style="min-width: 0">
                                <div class="text-h6 font-weight-bold text-truncate">{{ orderDetail.eventTitle || 'Event order' }}</div>
                                <div v-if="orderToken || orderPlacedAt" class="text-caption text-medium-emphasis mt-1">
                                    <span v-if="orderToken" :title="orderToken ?? undefined">Order {{ shortOrderId }}</span>
                                    <span v-if="orderToken && orderPlacedAt"> &middot; </span>
                                    <span v-if="orderPlacedAt">Placed {{ formatDate(orderPlacedAt) }}</span>
                                </div>
                            </div>
                            <v-chip v-if="allPaid" size="small" color="success" variant="tonal" prepend-icon="mdi-check-circle">Paid</v-chip>
                        </div>

                        <!-- Line items -->
                        <div class="invoice-head d-flex text-caption text-medium-emphasis font-weight-medium pb-1">
                            <div class="flex-grow-1">Item</div>
                            <div class="text-right" style="width: 96px">Amount</div>
                        </div>
                        <div v-for="it in orderDetail.items" :key="it.id" class="d-flex align-start py-2 order-row">
                            <div class="flex-grow-1" style="min-width: 0">
                                <div class="font-weight-medium">
                                    {{ it.tierName }}
                                    <v-chip size="x-small" class="ml-1" :color="it.kind === 'race_entry' ? 'primary' : 'secondary'" variant="tonal">
                                        {{ it.kind === 'race_entry' ? 'Race class' : (it.audience === 'spectator' ? 'Spectator gate' : 'Rider gate') }}
                                    </v-chip>
                                </div>
                                <div class="text-caption text-medium-emphasis">
                                    <span v-if="it.riderName">{{ it.riderName }}</span>
                                    <span v-if="it.raceNumber"> · #{{ it.raceNumber }}</span>
                                    <span> · </span>
                                    <span :class="it.registrationComplete ? 'text-success' : 'text-warning'">
                                        {{ it.registrationComplete ? 'Registered' : 'Needs registration' }}
                                    </span>
                                </div>
                                <div v-if="it.waiverSigned" class="text-caption text-success d-flex align-center ga-1 mt-1">
                                    <v-icon icon="mdi-file-sign" size="13"></v-icon>
                                    <span>Waiver signed</span>
                                </div>
                            </div>
                            <div class="text-body-2 ml-2 text-right" style="width: 96px">{{ money(it.basePriceCents) }}</div>
                        </div>

                        <!-- Totals -->
                        <v-divider class="mt-2 mb-1"></v-divider>
                        <div class="d-flex justify-space-between py-1 text-body-2">
                            <span class="text-medium-emphasis">Subtotal</span>
                            <span>{{ money(orderSubtotalCents) }}</span>
                        </div>
                        <div v-if="orderServiceFeeCents > 0" class="d-flex justify-space-between py-1 text-body-2">
                            <span class="text-medium-emphasis">Service fee</span>
                            <span>{{ money(orderServiceFeeCents) }}</span>
                        </div>
                        <div v-else-if="orderServiceFeeCents < 0" class="d-flex justify-space-between py-1 text-body-2">
                            <span class="text-medium-emphasis">Discount</span>
                            <span>-{{ money(-orderServiceFeeCents) }}</span>
                        </div>
                        <div class="d-flex justify-space-between py-1 text-body-2">
                            <span class="text-medium-emphasis">Tax</span>
                            <span class="text-medium-emphasis">$0.00</span>
                        </div>
                        <v-divider class="my-1"></v-divider>
                        <div class="d-flex justify-space-between py-1 font-weight-bold text-subtitle-1">
                            <span>Total</span>
                            <span>{{ money(orderTotalCents) }}</span>
                        </div>
                    </template>
                </v-card-text>
                <v-card-actions v-if="orderDetail && hasPaidItems" class="px-4 pb-4 pt-0">
                    <v-spacer></v-spacer>
                    <v-btn variant="tonal" prepend-icon="mdi-email-fast-outline"
                        :loading="resending" @click="resendConfirmation">
                        Resend confirmation
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" timeout="5000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import tenantHelper from '@/helpers/TenantHelper'
import UpcomingEventCard from '@/components/UpcomingEventCard.vue'
import { UpcomingService, type UpcomingItem, type UpcomingKind, type EventOrderDetail } from '@/services/UpcomingService'

const service = new UpcomingService()

const items = ref<UpcomingItem[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
const showPast = ref(false)

const orderDialog = ref(false)
const orderLoading = ref(false)
const orderDetail = ref<EventOrderDetail | null>(null)
const orderError = ref('')
const orderEventId = ref<string | null>(null)
// Order date + reference token for the dialog header (already on the UpcomingItem that opened it).
const orderPlacedAt = ref<string | null>(null)
const orderToken = ref<string | null>(null)
const shortOrderId = computed(() =>
    orderToken.value ? '#' + orderToken.value.replace(/-/g, '').slice(0, 8).toUpperCase() : '')
const resending = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('success')
// Invoice math: each line shows its base (list) price; the rider's service-fee
// share is baked into amountCents, so the fee line = total - subtotal.
const orderItems = computed(() => orderDetail.value?.items ?? [])
const orderSubtotalCents = computed(() => orderItems.value.reduce((s, i) => s + i.basePriceCents, 0))
const orderTotalCents = computed(() => orderItems.value.reduce((s, i) => s + i.amountCents, 0))
const orderServiceFeeCents = computed(() => orderTotalCents.value - orderSubtotalCents.value)
const allPaid = computed(() => orderItems.value.length > 0 && orderItems.value.every(i => i.status === 'paid' || i.status === 'redeemed'))
const hasPaidItems = computed(() => orderItems.value.some(i => i.status === 'paid' || i.status === 'redeemed'))

const eventLikeItems = computed(() =>
    items.value.filter(i => i.kind === 'event_ticket' || i.kind === 'pass'))
const passLikeItems = computed(() =>
    items.value.filter(i => i.kind === 'season_pass' || i.kind === 'membership'))

const upcomingEvents = computed(() =>
    eventLikeItems.value.filter(i => !i.occursAtUtc || !dayjs.utc(i.occursAtUtc).isBefore(dayjs())))
const pastEvents = computed(() =>
    eventLikeItems.value.filter(i => i.occursAtUtc && dayjs.utc(i.occursAtUtc).isBefore(dayjs()))
        .sort((a, b) => dayjs.utc(b.occursAtUtc!).valueOf() - dayjs.utc(a.occursAtUtc!).valueOf()))

onMounted(load)

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const r = await service.list()
        items.value = (r.data as any).data
    } catch (err: any) {
        loadError.value = err.response?.data?.error || 'Failed to load upcoming items.'
    } finally {
        loading.value = false
    }
}

async function openOrder(item: UpcomingItem) {
    orderDialog.value = true
    orderLoading.value = true
    orderDetail.value = null
    orderError.value = ''
    orderEventId.value = item.id
    orderPlacedAt.value = item.createdAtUtc
    orderToken.value = item.redemptionToken
    try {
        const r = await service.eventOrder(item.id)
        orderDetail.value = (r.data as any).data
    } catch (err: any) {
        orderError.value = err.response?.data?.error || 'Could not load this order. Refresh and try again.'
    } finally {
        orderLoading.value = false
    }
}

async function resendConfirmation() {
    if (!orderEventId.value) return
    resending.value = true
    try {
        const r = await service.resendConfirmation(orderEventId.value)
        const email = (r.data as any).data?.email
        snackbarColor.value = 'success'
        snackbarText.value = email ? `Confirmation sent to ${email}.` : 'Confirmation sent.'
    } catch (err: any) {
        snackbarColor.value = 'error'
        snackbarText.value = err.response?.data?.error || 'Could not resend the confirmation.'
    } finally {
        resending.value = false
        snackbar.value = true
    }
}

function kindLabel(kind: UpcomingKind): string {
    switch (kind) {
        case 'event_ticket': return 'Event ticket'
        case 'pass':         return 'Day pass'
        case 'season_pass':  return 'Season pass'
        case 'membership':   return 'Membership'
    }
}
function kindChipColor(kind: UpcomingKind): string {
    switch (kind) {
        case 'event_ticket': return 'primary'
        case 'pass':         return 'secondary'
        case 'season_pass':  return 'info'
        case 'membership':   return 'success'
    }
}
function kindIcon(kind: UpcomingKind): string {
    return kind === 'membership' ? 'mdi-card-account-details-outline' : 'mdi-ticket-percent-outline'
}
function formatDate(utc: string): string {
    return dayjs.utc(utc).local().format('MMM D, YYYY')
}
function money(cents: number): string {
    return cents === 0 ? 'Free' : `$${(cents / 100).toFixed(2)}`
}

function tenantUserUrl(item: UpcomingItem): string {
    // Send each kind to the page that actually lists it: season passes and memberships
    // are NOT shown on MyPasses (they live on their own pages).
    const path = item.kind === 'season_pass' ? '/User/SeasonPasses'
        : item.kind === 'membership' ? '/Membership'
        : '/User/MyPasses'
    const proto = window.location.protocol
    const port = window.location.port ? `:${window.location.port}` : ''
    return `${proto}//${item.tenantSubdomain}.${tenantHelper.rootDomain()}${port}${path}`
}
</script>

<style scoped>
.up-card-link { text-decoration: none; color: inherit; display: block; height: 100%; }
.up-card { overflow: visible; transition: transform 0.15s ease; }
.up-card:hover { transform: translateY(-2px); }
.up-past { opacity: 0.72; }

.up-image {
    position: relative;
    height: 120px;
    border-top-left-radius: inherit;
    border-top-right-radius: inherit;
}
.up-image--pass {
    background: linear-gradient(135deg, #1f2937, #475569);
    display: flex;
    align-items: center;
    justify-content: center;
}
.up-pass-icon { color: rgba(255, 255, 255, 0.55); }
.up-chip { position: absolute; top: 8px; right: 8px; }

.order-row { border-bottom: 1px solid rgba(0, 0, 0, 0.06); }
.invoice-head { border-bottom: 2px solid rgba(0, 0, 0, 0.12); }
</style>
