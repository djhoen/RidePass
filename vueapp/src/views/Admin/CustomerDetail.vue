<template>
    <v-container>
        <div class="d-flex align-center mb-4 flex-wrap ga-3">
            <v-btn variant="text" prepend-icon="mdi-arrow-left" @click="back">Customers</v-btn>
            <v-spacer></v-spacer>
        </div>

        <div v-if="loading" class="text-center py-8">
            <v-progress-circular indeterminate></v-progress-circular>
        </div>

        <v-alert v-else-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <div v-else-if="!detail" class="text-center text-medium-emphasis py-8">
            Customer not found at this tenant.
        </div>

        <template v-else>
            <!-- Profile -->
            <v-card class="mb-4">
                <v-card-title>
                    {{ detail.user.firstName }} {{ detail.user.lastName }}
                </v-card-title>
                <v-card-text>
                    <div class="d-flex flex-wrap ga-6">
                        <div>
                            <div class="text-caption text-medium-emphasis">Email</div>
                            <div>{{ detail.user.email }}</div>
                        </div>
                        <div v-if="detail.user.phone">
                            <div class="text-caption text-medium-emphasis">Phone</div>
                            <div>{{ detail.user.phone }}</div>
                        </div>
                        <div v-if="detail.user.birthdate">
                            <div class="text-caption text-medium-emphasis">Birthdate</div>
                            <div>{{ detail.user.birthdate.substring(0, 10) }}</div>
                        </div>
                        <div v-if="detail.user.emergencyContactName">
                            <div class="text-caption text-medium-emphasis">Emergency Contact</div>
                            <div>{{ detail.user.emergencyContactName }} — {{ detail.user.emergencyContactPhone || '—' }}</div>
                        </div>
                        <div>
                            <div class="text-caption text-medium-emphasis">Total at this track</div>
                            <div>${{ (totalSpent / 100).toFixed(2) }} across {{ totalPurchases }} purchase{{ totalPurchases === 1 ? '' : 's' }}</div>
                        </div>
                    </div>
                </v-card-text>
            </v-card>

            <!-- Waiver(s) — only shown for actual riders. -->
            <v-card v-if="detail.waiverSignatures.length > 0" class="mb-4">
                <v-card-title>Waiver Signatures</v-card-title>
                <v-table density="comfortable">
                    <thead>
                        <tr>
                            <th>Waiver</th>
                            <th style="width: 200px">Signed At</th>
                            <th>Signed By</th>
                            <th style="width: 120px">Signature</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="w in detail.waiverSignatures" :key="w.id">
                            <td>{{ w.waiverTitle }} <span class="text-caption text-medium-emphasis">v{{ w.waiverVersion }}</span></td>
                            <td>{{ formatWhen(w.signedAt) }}</td>
                            <td>
                                <span v-if="w.signedByParent">
                                    Parent: {{ w.parentName || '—' }}
                                    <span v-if="w.parentPhone" class="text-caption text-medium-emphasis"> — {{ w.parentPhone }}</span>
                                </span>
                                <span v-else>Self</span>
                            </td>
                            <td>
                                <v-btn v-if="w.signatureDataUrl" size="small" variant="text" @click="viewSignature(w)">View</v-btn>
                                <span v-else class="text-medium-emphasis">—</span>
                            </td>
                        </tr>
                    </tbody>
                </v-table>
            </v-card>
            <v-card v-else class="mb-4" color="grey-lighten-4">
                <v-card-text class="text-medium-emphasis">No waiver on file.</v-card-text>
            </v-card>

            <!-- Purchase / reservation history -->
            <v-card>
                <v-tabs v-model="historyTab">
                    <v-tab value="day">Passes ({{ detail.passes.length }})</v-tab>
                    <v-tab value="event">Event Tickets ({{ detail.eventTickets.length }})</v-tab>
                    <v-tab value="season">Season Passes ({{ detail.seasonPasses.length }})</v-tab>
                </v-tabs>

                <v-window v-model="historyTab">
                    <v-window-item value="day">
                        <v-table density="compact">
                            <thead>
                                <tr>
                                    <th style="width: 180px">When</th>
                                    <th style="width: 140px">Valid On</th>
                                    <th style="width: 110px" class="text-right">Amount</th>
                                    <th style="width: 120px">Status</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr v-for="p in detail.passes" :key="p.id">
                                    <td>{{ formatWhen(p.createdAt) }}</td>
                                    <td>{{ p.validOnDate ? p.validOnDate.substring(0, 10) : '—' }}</td>
                                    <td class="text-right">${{ (p.amountCents / 100).toFixed(2) }}</td>
                                    <td>
                                        <v-chip size="small" :color="statusColor(p.status)">{{ p.status }}</v-chip>
                                    </td>
                                </tr>
                                <tr v-if="detail.passes.length === 0">
                                    <td colspan="4" class="text-center text-medium-emphasis py-4">No passes.</td>
                                </tr>
                            </tbody>
                        </v-table>
                    </v-window-item>

                    <v-window-item value="event">
                        <v-table density="compact">
                            <thead>
                                <tr>
                                    <th style="width: 180px">When</th>
                                    <th style="width: 110px" class="text-right">Amount</th>
                                    <th style="width: 120px">Status</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr v-for="t in detail.eventTickets" :key="t.id">
                                    <td>{{ formatWhen(t.createdAt) }}</td>
                                    <td class="text-right">${{ (t.amountCents / 100).toFixed(2) }}</td>
                                    <td>
                                        <v-chip size="small" :color="statusColor(t.status)">{{ t.status }}</v-chip>
                                    </td>
                                </tr>
                                <tr v-if="detail.eventTickets.length === 0">
                                    <td colspan="3" class="text-center text-medium-emphasis py-4">No event tickets.</td>
                                </tr>
                            </tbody>
                        </v-table>
                    </v-window-item>

                    <v-window-item value="season">
                        <v-table density="compact">
                            <thead>
                                <tr>
                                    <th style="width: 180px">When</th>
                                    <th>Valid Range</th>
                                    <th style="width: 110px" class="text-right">Amount</th>
                                    <th style="width: 110px" class="text-right">Credits Left</th>
                                    <th style="width: 120px">Status</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr v-for="s in detail.seasonPasses" :key="s.id">
                                    <td>{{ formatWhen(s.createdAt) }}</td>
                                    <td>{{ s.validFromDate.substring(0, 10) }} → {{ s.validToDate.substring(0, 10) }}</td>
                                    <td class="text-right">${{ (s.amountCents / 100).toFixed(2) }}</td>
                                    <td class="text-right">
                                        {{ s.creditsRemaining ?? '—' }}
                                        <!-- Non-null credits = a credits ("ride pack") pass; support can adjust. -->
                                        <v-btn v-if="s.creditsRemaining !== null" icon="mdi-pencil" variant="text"
                                            size="x-small" aria-label="Adjust credits"
                                            @click="openAdjustCredits(s)"></v-btn>
                                    </td>
                                    <td>
                                        <v-chip size="small" :color="statusColor(s.status)">{{ s.status }}</v-chip>
                                    </td>
                                </tr>
                                <tr v-if="detail.seasonPasses.length === 0">
                                    <td colspan="5" class="text-center text-medium-emphasis py-4">No season passes.</td>
                                </tr>
                            </tbody>
                        </v-table>
                    </v-window-item>
                </v-window>
            </v-card>

            <!-- Bike shop footprint (sales, rentals, service, credit). Hidden when the tenant
                 doesn't run the shop. -->
            <v-card v-if="branding.bikeShopEnabled" class="mt-4">
                <v-card-title>Bike Shop</v-card-title>
                <v-card-text>
                    <ShopHistoryPanel :user-id="customerId" />
                </v-card-text>
            </v-card>
        </template>

        <!-- Signature image preview -->
        <v-dialog v-model="signatureDialog" max-width="640">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Signature</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="signatureDialog = false"></v-btn>
                </v-card-title>
                <v-card-text class="text-center">
                    <img v-if="signatureUrl" :src="signatureUrl" alt="Signature" style="max-width: 100%; background: #fff; border: 1px solid #ccc;" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="signatureDialog = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Adjust ride credits on a credits ("ride pack") season pass. Audit-logged server-side. -->
        <v-dialog v-model="creditsDialog" max-width="420">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Adjust ride credits</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="creditsDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model.number="creditsInput" type="number" min="0" label="Credits remaining"
                        density="compact" hide-details autofocus></v-text-field>
                    <v-textarea v-model="creditsReason" label="Reason (required — audit-logged)" rows="2"
                        density="compact" class="mt-4" hide-details></v-textarea>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="creditsSaving" @click="creditsDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="creditsSaving"
                        :disabled="creditsInput === null || creditsInput < 0 || !creditsReason.trim()"
                        @click="saveCredits">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { CustomerService, type CustomerDetailDto, type CustomerSeasonPassDto, type CustomerWaiverDto } from '@/services/CustomerService'
import { SeasonPassService } from '@/services/SeasonPassService'
import ShopHistoryPanel from '@/components/ShopHistoryPanel.vue'
import { branding } from '@/stores/branding'

const route = useRoute()
const router = useRouter()
const service = new CustomerService()

const customerId = route.params.userId as string
const detail = ref<CustomerDetailDto | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)
const historyTab = ref<'day' | 'event' | 'season'>('day')

const signatureDialog = ref(false)
const signatureUrl = ref<string | null>(null)

// Use the server-computed totals (same source as the customer list) so the two screens
// always agree, instead of re-summing a partial set of collections client-side.
const totalPurchases = computed(() => detail.value?.totalPurchases ?? 0)
const totalSpent = computed(() => detail.value?.totalSpentCents ?? 0)

onMounted(async () => {
    const userId = customerId
    try {
        const r = await service.getDetail(userId)
        detail.value = (r.data as any).data
    } catch (err: any) {
        detail.value = null
        // A genuine 404 means the customer isn't at this tenant — show the "not found"
        // state. Anything else (network, 403, 500) is a real failure the user must see.
        if (err.response?.status !== 404) {
            loadError.value = err.response?.data?.error ?? 'Couldn’t load this customer. Refresh to try again.'
        }
    } finally {
        loading.value = false
    }
})

function back() { router.push({ name: 'AdminCustomers' }) }
function tz() { return branding.timezone || 'UTC' }
function formatWhen(utc: string): string { return dayjs.utc(utc).tz(tz()).format('YYYY-MM-DD HH:mm') }
function statusColor(status: string): string {
    switch (status) {
        case 'paid': return 'success'
        case 'pending': return 'warning'
        case 'failed': return 'error'
        case 'cancelled': return 'orange'
        case 'refunded': return 'grey'
        case 'redeemed': return 'primary'
        default: return 'default'
    }
}
function viewSignature(w: CustomerWaiverDto) {
    signatureUrl.value = w.signatureDataUrl
    signatureDialog.value = true
}

// ── Ride-credit adjustment (credits passes only) ─────────────────────────────
const seasonPassService = new SeasonPassService()
const creditsDialog = ref(false)
const creditsTarget = ref<CustomerSeasonPassDto | null>(null)
const creditsInput = ref<number | null>(null)
const creditsReason = ref('')
const creditsSaving = ref(false)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')
function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

function openAdjustCredits(s: CustomerSeasonPassDto) {
    creditsTarget.value = s
    creditsInput.value = s.creditsRemaining
    creditsReason.value = ''
    creditsDialog.value = true
}

async function saveCredits() {
    if (!creditsTarget.value || creditsInput.value === null || creditsInput.value < 0) return
    creditsSaving.value = true
    try {
        await seasonPassService.adjustCredits(creditsTarget.value.id, creditsInput.value, creditsReason.value.trim())
        creditsTarget.value.creditsRemaining = creditsInput.value
        creditsDialog.value = false
        flash(`Credits set to ${creditsInput.value}.`, 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Couldn’t update the credits. Check the connection and try again.', 'error')
    } finally {
        creditsSaving.value = false
    }
}
</script>
