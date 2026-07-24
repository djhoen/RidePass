<template>
    <v-container>
        <div class="d-flex align-center mb-4 ga-3 flex-wrap">
            <h1 class="text-h4">Shop Settings</h1>
            <v-spacer></v-spacer>
            <v-btn variant="tonal" prepend-icon="mdi-package-variant" to="/Admin/BikeShop">Inventory</v-btn>
        </div>

        <v-alert v-if="!branding.bikeShopEnabled" type="info" variant="tonal" class="mb-4">
            The bike shop is turned off. Enable it under Settings &rarr; Features to start selling.
        </v-alert>

        <p class="text-caption text-medium-emphasis mb-4">
            Configuration you set up once. Day-to-day stock lives under
            <router-link to="/Admin/BikeShop">Inventory</router-link>, and rental pricing is on the
            <router-link to="/Admin/BikeShop/Rentals?tab=settings">Rentals</router-link> page.
        </p>

        <v-tabs v-model="tab" class="mb-4">
            <v-tab value="service">Service &amp; fees</v-tab>
            <v-tab value="statuses">Work order stages</v-tab>
            <v-tab value="agreements">Agreements</v-tab>
            <v-tab value="inspections">Inspection checklist</v-tab>
            <v-tab value="tax">Tax</v-tab>
            <v-tab value="insurance">Damage Protection</v-tab>
        </v-tabs>

        <!-- ── Tax categories ───────────────────────────────────────────── -->
        <div v-if="tab === 'tax'">
            <p class="text-caption text-medium-emphasis mb-3">
                Retail sales tax, applied per product. A product without a category falls back to the
                default, so if there's no default those products sell untaxed.
            </p>
            <v-alert v-if="taxLoaded && activeTaxCategories.length === 0" type="warning"
                variant="tonal" density="compact" class="mb-3">
                <strong>No tax categories.</strong> Everything sold in the shop is being rung up with
                no sales tax.
            </v-alert>
            <v-alert v-else-if="taxLoaded && !defaultTaxCategory" type="warning"
                variant="tonal" density="compact" class="mb-3">
                <strong>No default tax category.</strong> Products without one assigned sell untaxed.
                Mark one as the default.
            </v-alert>
            <SimpleCrud label="tax category" :rows="taxCategories" @new="openTax()" @edit="openTax">
                <template #cols="{ row }">
                    <td>{{ row.name }}</td>
                    <td class="text-right">{{ (row.rateBps / 100).toFixed(2) }}%</td>
                    <td><v-chip v-if="row.isDefault" size="x-small" color="primary">Default</v-chip></td>
                </template>
            </SimpleCrud>
        </div>

        <!-- ── Service notifications + shop supply fee ──────────────────── -->
        <div v-else-if="tab === 'service'">
            <ServiceSettingsTab />
        </div>

        <!-- ── Work order stages (custom statuses) ──────────────────────── -->
        <div v-else-if="tab === 'statuses'">
            <WorkOrderStatusesTab />
        </div>

        <!-- ── Rental agreement / work-order terms ──────────────────────── -->
        <div v-else-if="tab === 'agreements'">
            <AgreementsTab />
        </div>

        <!-- ── What mechanics check on an inspection ────────────────────── -->
        <div v-else-if="tab === 'inspections'">
            <InspectionChecklistTab />
        </div>

        <!-- ── Damage protection (rental insurance) ─────────────────────── -->
        <div v-else-if="tab === 'insurance'">
            <v-card class="pa-4" max-width="720">
                <div class="text-subtitle-1 mb-1">Damage protection</div>
                <p class="text-caption text-medium-emphasis mb-4">
                    An optional add-on renters can buy at checkout that waives the refundable security
                    deposit if the bike comes back damaged.
                </p>

                <v-switch v-model="rentalInsuranceEnabled" label="Offer damage protection on rentals"
                    color="primary" hide-details density="compact"></v-switch>

                <template v-if="rentalInsuranceEnabled">
                    <v-text-field v-model="rentalInsuranceLabel" label="Label" placeholder="Damage Protection"
                        density="compact" variant="outlined" class="mt-4"></v-text-field>

                    <v-text-field v-model.number="rentalInsurancePct" label="Rate (% of rental value)" type="number"
                        min="0" step="0.01" density="compact" variant="outlined" class="mt-4"
                        hint="Buying it waives the refundable security deposit." persistent-hint></v-text-field>
                </template>

                <v-btn color="primary" class="mt-4" :loading="savingInsurance" @click="saveInsuranceSettings">
                    Save
                </v-btn>
            </v-card>
        </div>

        <TaxDialog v-model="taxDialog" :tax="editingTax" @saved="reloadTax" @flash="flash" />

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3500">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { branding } from '@/stores/branding'
import { TenantService } from '@/services/TenantService'
import { BikeShopService, type ShopTaxCategory } from '@/services/BikeShopService'
import SimpleCrud from '@/components/bikeshop/SimpleCrud.vue'
import TaxDialog from '@/components/bikeshop/TaxDialog.vue'
import ServiceSettingsTab from '@/components/bikeshop/ServiceSettingsTab.vue'
import WorkOrderStatusesTab from '@/components/bikeshop/WorkOrderStatusesTab.vue'
import AgreementsTab from '@/components/bikeshop/AgreementsTab.vue'
import InspectionChecklistTab from '@/components/bikeshop/InspectionChecklistTab.vue'

const service = new BikeShopService()
const tenantService = new TenantService()
const route = useRoute()

// ?tab= so the Rentals page's "manage retail tax" link lands straight on Tax.
const validTabs = ['service', 'statuses', 'agreements', 'inspections', 'tax', 'insurance']
const requested = String(route.query.tab ?? '')
const tab = ref(validTabs.includes(requested) ? requested : 'service')

const taxCategories = ref<ShopTaxCategory[]>([])
const taxLoaded = ref(false)
const taxDialog = ref(false)
const editingTax = ref<ShopTaxCategory | null>(null)

// Only ACTIVE categories count: an inactive default taxes nothing, so treating it as configured
// would be exactly the false reassurance these warnings exist to prevent.
const activeTaxCategories = computed(() => taxCategories.value.filter(c => c.isActive))
const defaultTaxCategory = computed(() =>
    activeTaxCategories.value.find(c => c.isDefault) ?? null)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')
function flash(text: string, color: 'success' | 'error' = 'success') {
    snackbarText.value = text; snackbarColor.value = color; snackbar.value = true
}

function openTax(t: ShopTaxCategory | null = null) { editingTax.value = t; taxDialog.value = true }

// ── Damage protection (rental insurance) ─────────────────────────────────
const rentalInsuranceEnabled = ref(branding.rentalInsuranceEnabled ?? false)
const rentalInsuranceLabel = ref(branding.rentalInsuranceLabel ?? 'Damage Protection')
// Percent (not bps) for the field; converted to bps only when saving.
const rentalInsurancePct = ref((branding.rentalInsuranceBps ?? 0) / 100)
const savingInsurance = ref(false)

async function saveInsuranceSettings() {
    savingInsurance.value = true
    try {
        const pct = typeof rentalInsurancePct.value === 'number'
            ? rentalInsurancePct.value
            : parseFloat(String(rentalInsurancePct.value))
        const bps = Number.isFinite(pct) && pct > 0 ? Math.round(pct * 100) : 0
        const label = rentalInsuranceLabel.value?.trim() || 'Damage Protection'
        await tenantService.updateRentalSettings({
            // Pass the existing fee/tax settings through unchanged; this tab only edits insurance.
            riderPaidBps: branding.rentalRiderPaidServiceChargeBps ?? 10000,
            taxBps: branding.rentalTaxBps ?? null,
            serviceChargeTaxable: branding.rentalTaxServiceChargeTaxable ?? true,
            rentalInsuranceEnabled: rentalInsuranceEnabled.value,
            rentalInsuranceLabel: label,
            rentalInsuranceBps: bps,
        })
        branding.rentalInsuranceEnabled = rentalInsuranceEnabled.value
        branding.rentalInsuranceLabel = label
        branding.rentalInsuranceBps = bps
        rentalInsuranceLabel.value = label
        flash('Damage protection settings saved.', 'success')
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not save damage protection settings. Please try again.', 'error')
    } finally {
        savingInsurance.value = false
    }
}

async function reloadTax() {
    try {
        taxCategories.value = (await service.listTaxCategories()).data.data
        taxLoaded.value = true
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not load tax categories. Refresh to try again.', 'error')
    }
}

onMounted(reloadTax)
</script>
