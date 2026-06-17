<template>
    <v-container>
        <h1 class="text-h4 mb-4">Tenants</h1>

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
                        <th style="width: 130px">Service charge</th>
                        <th style="width: 120px">Concessions</th>
                        <th style="width: 160px">Timezone</th>
                        <th style="width: 180px">Created</th>
                        <th style="width: 160px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="t in tenants" :key="t.id">
                        <td><code>{{ t.subdomain }}</code></td>
                        <td>
                            {{ t.displayName }}
                            <v-chip v-if="!t.isPublished" size="x-small" color="warning" variant="tonal" class="ml-1">Draft</v-chip>
                        </td>
                        <td>{{ t.status }}</td>
                        <td>
                            {{ (t.serviceChargeBps / 100).toFixed(2) }}%
                            <span v-if="t.monthlyServiceChargeCapCents !== null" class="text-caption text-medium-emphasis">
                                cap ${{ (t.monthlyServiceChargeCapCents / 100).toFixed(2) }}
                            </span>
                        </td>
                        <td>
                            <v-switch :model-value="t.concessionsEnabled"
                                @update:model-value="(v: boolean | null) => toggleConcessions(t, !!v)"
                                color="primary" density="compact" hide-details inset
                                :loading="togglingId === t.id" :disabled="togglingId !== null"></v-switch>
                        </td>
                        <td>{{ t.timezone }}</td>
                        <td>{{ formatDate(t.createdAtUtc) }}</td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" @click="openEdit(t)">Edit</v-btn>
                            <v-btn v-if="t.isPublished" variant="text" size="small"
                                :href="tenantUrl(t.subdomain)" target="_blank">Visit</v-btn>
                            <v-btn v-else variant="text" size="small" color="warning"
                                :href="previewUrl(t.subdomain)" target="_blank">Preview</v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loadingTenants && tenants.length === 0">
                        <td colspan="8" class="text-center text-medium-emphasis py-8">No tenants yet.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <!-- Create tenant dialog -->
        <v-dialog v-model="createDialog" max-width="640" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>New Tenant</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="createDialog = false"></v-btn>
                </v-card-title>
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
                    <v-text-field v-model="createForm.displayName" label="Display Name" density="compact" class="mt-4"></v-text-field>
                    <v-select v-model="createForm.tenantType" :items="tenantTypeOptions"
                        label="Tenant type" density="compact"
                        hint="Drives event-type / waiver / pass-product defaults at creation. Locked after creation."
                        persistent-hint class="mt-4"></v-select>
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

        <!-- One-time credential reveal -->
        <v-dialog v-model="credsDialog" max-width="560" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Tenant created</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="credsDialog = false"></v-btn>
                </v-card-title>
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

        <!-- Edit tenant dialog -->
        <v-dialog v-model="editDialog" max-width="680" persistent>
            <v-card v-if="editTenant">
                <v-card-title class="d-flex align-center">
                    <span>
                        Edit {{ editTenant.displayName }}
                        <span class="text-medium-emphasis text-body-2">(<code>{{ editTenant.subdomain }}</code>)</span>
                    </span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="editDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.displayName" label="Display name" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="3">
                            <v-select v-model="editForm.status" :items="statusOptions"
                                item-title="title" item-value="value" label="Status" density="compact"></v-select>
                        </v-col>
                        <v-col cols="12" md="3">
                            <v-autocomplete v-model="editForm.timezone" :items="timezoneOptions"
                                item-title="title" item-value="value" label="Timezone" density="compact"></v-autocomplete>
                        </v-col>
                    </v-row>

                    <v-switch v-model="editForm.isPublished" color="primary" inset density="compact" hide-details
                        :label="editForm.isPublished ? 'Published — visible in public discovery' : 'Not published — hidden from the map, featured, search, and events'"
                        class="mb-2"></v-switch>

                    <div class="text-subtitle-2 mt-2 mb-1">Billing</div>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model.number="editForm.serviceChargePct" type="number" step="0.01" min="0" max="100"
                                label="Service charge" suffix="%" density="compact"
                                hint="Flat % RidePass takes from each sale." persistent-hint></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model.number="editForm.serviceChargeCapDollars" type="number" step="0.01" min="0"
                                label="Monthly cap (blank = none)" prefix="$" density="compact" clearable
                                hint="Once reached, 0% is taken until next UTC month." persistent-hint></v-text-field>
                        </v-col>
                    </v-row>

                    <div class="text-subtitle-2 mt-4 mb-1">Address</div>
                    <v-text-field v-model="editForm.addressLine" label="Address line" density="compact"></v-text-field>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.city" label="City" density="compact" class="mt-4"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="3">
                            <v-text-field v-model="editForm.region" label="State / region" density="compact" class="mt-4"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="3">
                            <v-text-field v-model="editForm.postalCode" label="Postal code" density="compact" class="mt-4"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-text-field v-model="editForm.country" label="Country" density="compact" class="mt-4"></v-text-field>

                    <div class="d-flex align-center ga-2 mt-4">
                        <v-text-field v-model.number="editForm.latitude" type="number" step="0.0001"
                            label="Latitude" density="compact" hide-details></v-text-field>
                        <v-text-field v-model.number="editForm.longitude" type="number" step="0.0001"
                            label="Longitude" density="compact" hide-details></v-text-field>
                        <v-btn variant="tonal" :loading="geocoding" prepend-icon="mdi-map-search"
                            @click="lookupCoords">Look up</v-btn>
                    </div>
                    <div class="text-caption text-medium-emphasis mt-1">
                        Coordinates place the track on the apex "Tracks near you" map. "Look up" geocodes the address above.
                    </div>

                    <div class="text-subtitle-2 mt-4 mb-1">Contact</div>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.contactEmail" type="email" label="Contact email" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.phone" type="tel" label="Phone" density="compact"></v-text-field>
                        </v-col>
                    </v-row>

                    <div class="text-subtitle-2 mt-4 mb-1">LoamPassMx</div>
                    <v-text-field v-model="editForm.loampassMxDestinationId" label="LoamMx destination ID"
                        density="compact" clearable
                        hint="Set this to make the track a LoamPassMx track (riders can link their Loam Pass and redeem credits). Blank = not a LoamPassMx track."
                        persistent-hint></v-text-field>

                    <div v-if="editError" class="text-error text-caption mt-2">{{ editError }}</div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="editDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="savingEdit" @click="saveEdit">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import dayjs from 'dayjs'
import { SuperAdminService, type TenantSummary, type CreateTenantResult, type UpdateTenantPayload } from '@/services/SuperAdminService'
import { geocode } from '@/helpers/Geocode'
import authHelper from '@/helpers/AuthHelper'

const service = new SuperAdminService()

const tenants = ref<TenantSummary[]>([])
const loadingTenants = ref(false)
const togglingId = ref<string | null>(null)

const createDialog = ref(false)
const creating = ref(false)
const createForm = ref({
    subdomain: '',
    displayName: '',
    tenantType: 'motocross' as 'motocross' | 'mountain_bike',
    timezone: 'America/New_York',
    adminFirstName: '',
    adminLastName: '',
    adminEmail: '',
})

const tenantTypeOptions = [
    { value: 'motocross', title: 'Motocross (MX)' },
    { value: 'mountain_bike', title: 'Mountain Bike (MTB)' },
]

const credsDialog = ref(false)
const createdResult = ref<CreateTenantResult | null>(null)

const timezoneOptions = [
    { title: 'Eastern (New York)', value: 'America/New_York' },
    { title: 'Central (Chicago)', value: 'America/Chicago' },
    { title: 'Mountain (Denver)', value: 'America/Denver' },
    { title: 'Mountain — no DST (Phoenix)', value: 'America/Phoenix' },
    { title: 'Pacific (Los Angeles)', value: 'America/Los_Angeles' },
    { title: 'Alaska (Anchorage)', value: 'America/Anchorage' },
    { title: 'Hawaii–Aleutian (Honolulu)', value: 'Pacific/Honolulu' },
]

const statusOptions = [
    { title: 'Active', value: 'active' },
    { title: 'Suspended', value: 'suspended' },
    { title: 'Pending', value: 'pending' },
]

// Form holds service charge in display units (% and $); converted to bps/cents
// on save. Everything else maps straight to the API payload.
interface TenantEditForm {
    displayName: string
    status: string
    timezone: string
    isPublished: boolean
    serviceChargePct: number
    serviceChargeCapDollars: number | null
    addressLine: string | null
    city: string | null
    region: string | null
    postalCode: string | null
    country: string | null
    latitude: number | null
    longitude: number | null
    contactEmail: string | null
    phone: string | null
    loampassMxDestinationId: string | null
}

const editDialog = ref(false)
const editTenant = ref<TenantSummary | null>(null)
const savingEdit = ref(false)
const geocoding = ref(false)
const editError = ref<string | null>(null)
const editForm = ref<TenantEditForm>(emptyEditForm())

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(loadTenants)

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

async function toggleConcessions(t: TenantSummary, enabled: boolean) {
    if (togglingId.value) return
    togglingId.value = t.id
    try {
        await service.updateTenantConcessionsEnabled(t.id, enabled)
        t.concessionsEnabled = enabled
        flash(`Concessions ${enabled ? 'enabled' : 'disabled'} for ${t.subdomain}.`, 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to update concessions.', 'error')
    } finally {
        togglingId.value = null
    }
}

function openCreateTenant() {
    createForm.value = {
        subdomain: '',
        displayName: '',
        tenantType: 'motocross',
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
            tenantType: createForm.value.tenantType,
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

function emptyEditForm(): TenantEditForm {
    return {
        displayName: '', status: 'active', timezone: 'America/New_York', isPublished: false,
        serviceChargePct: 3, serviceChargeCapDollars: null,
        addressLine: null, city: null, region: null, postalCode: null, country: null,
        latitude: null, longitude: null, contactEmail: null, phone: null,
        loampassMxDestinationId: null,
    }
}

function openEdit(t: TenantSummary) {
    editTenant.value = t
    editError.value = null
    editForm.value = {
        displayName: t.displayName,
        status: t.status,
        timezone: t.timezone,
        isPublished: t.isPublished,
        serviceChargePct: t.serviceChargeBps / 100,
        serviceChargeCapDollars: t.monthlyServiceChargeCapCents !== null ? t.monthlyServiceChargeCapCents / 100 : null,
        addressLine: t.addressLine,
        city: t.city,
        region: t.region,
        postalCode: t.postalCode,
        country: t.country,
        latitude: t.latitude,
        longitude: t.longitude,
        contactEmail: t.contactEmail,
        phone: t.phone,
        loampassMxDestinationId: t.loampassMxDestinationId,
    }
    editDialog.value = true
}

async function lookupCoords() {
    const parts = [editForm.value.addressLine, editForm.value.city, editForm.value.region,
        editForm.value.postalCode, editForm.value.country]
        .map(s => (s ?? '').trim()).filter(Boolean)
    if (parts.length === 0) {
        flash('Enter an address to look up.', 'error')
        return
    }
    geocoding.value = true
    try {
        const g = await geocode(parts.join(', '))
        if (g) {
            editForm.value.latitude = Number(g.lat.toFixed(6))
            editForm.value.longitude = Number(g.lng.toFixed(6))
            flash('Coordinates found.', 'success')
        } else {
            flash('No match found for that address.', 'error')
        }
    } catch {
        flash('Geocoding failed.', 'error')
    } finally {
        geocoding.value = false
    }
}

async function saveEdit() {
    if (!editTenant.value) return
    if (!editForm.value.displayName.trim()) {
        editError.value = 'Display name is required.'
        return
    }
    savingEdit.value = true
    editError.value = null
    try {
        const f = editForm.value
        const pct = numOrNull(f.serviceChargePct) ?? 0
        const capDollars = numOrNull(f.serviceChargeCapDollars)
        const body: UpdateTenantPayload = {
            displayName: f.displayName.trim(),
            status: f.status,
            timezone: f.timezone,
            isPublished: f.isPublished,
            serviceChargeBps: Math.round(pct * 100),
            monthlyServiceChargeCapCents: capDollars !== null ? Math.round(capDollars * 100) : null,
            addressLine: norm(f.addressLine),
            city: norm(f.city),
            region: norm(f.region),
            postalCode: norm(f.postalCode),
            country: norm(f.country),
            latitude: numOrNull(f.latitude),
            longitude: numOrNull(f.longitude),
            contactEmail: norm(f.contactEmail),
            phone: norm(f.phone),
            loampassMxDestinationId: norm(f.loampassMxDestinationId),
        }
        await service.updateTenant(editTenant.value.id, body)
        flash('Tenant updated.', 'success')
        editDialog.value = false
        await loadTenants()
    } catch (err: any) {
        editError.value = err.response?.data?.error || 'Failed to update tenant.'
    } finally {
        savingEdit.value = false
    }
}

function norm(s: string | null): string | null {
    const t = (s ?? '').trim()
    return t.length ? t : null
}
function numOrNull(n: number | null): number | null {
    const x = typeof n === 'number' ? n : parseFloat(n as any)
    return Number.isFinite(x) ? x : null
}

function tenantUrl(subdomain: string): string {
    const rootDomain = import.meta.env.VITE_ROOT_DOMAIN ?? 'ridepass.local'
    const port = window.location.port ? `:${window.location.port}` : ''
    return `${window.location.protocol}//${subdomain}.${rootDomain}${port}/`
}

// Preview an unpublished tenant: bridge the super admin's token to the subdomain
// via the URL fragment so the publish gate lets the request through (main.ts
// reads it on load, stores it, and strips it from the URL).
function previewUrl(subdomain: string): string {
    const token = authHelper.getToken() ?? ''
    return `${tenantUrl(subdomain)}#preview_token=${encodeURIComponent(token)}`
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
