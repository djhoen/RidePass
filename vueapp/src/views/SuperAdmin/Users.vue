<template>
    <v-container>
        <h1 class="text-h4 mb-4">Users</h1>

        <div class="d-flex align-center mb-3 ga-2 flex-wrap">
            <v-text-field v-model="userQuery" label="Search by name or email" density="compact" hide-details clearable
                prepend-inner-icon="mdi-magnify" style="max-width: 340px"
                @keyup.enter="loadUsers" @click:clear="onClearSearch"></v-text-field>
            <v-btn @click="loadUsers">Search</v-btn>
            <v-spacer></v-spacer>
            <v-btn color="primary" prepend-icon="mdi-shield-plus" @click="openCreateSuperAdmin">Add Super Admin</v-btn>
        </div>

        <div class="d-flex align-center mb-3 ga-2 flex-wrap">
            <v-select v-model="filterRole" :items="filterRoleOptions" label="Role" density="compact" hide-details
                clearable style="max-width: 200px" :disabled="searchActive"
                @update:model-value="loadUsers"></v-select>
            <v-autocomplete v-model="filterTenantId" :items="tenantOptions" label="Tenant" density="compact" hide-details
                clearable style="max-width: 280px" :disabled="searchActive"
                @update:model-value="loadUsers"></v-autocomplete>
            <v-select v-model="filterStatus" :items="statusOptions" label="Status" density="compact" hide-details
                clearable style="max-width: 180px" :disabled="searchActive"
                @update:model-value="loadUsers"></v-select>
            <v-chip v-if="searchActive" size="small" color="info" variant="tonal">
                Search matches all users; filters are ignored.
            </v-chip>
            <v-btn v-else-if="hasFilters" variant="text" size="small" @click="clearFilters">Clear filters</v-btn>
        </div>
        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Email</th>
                        <th style="width: 150px">Phone</th>
                        <th style="width: 130px">Role</th>
                        <th style="width: 160px">Tenant</th>
                        <th style="width: 120px">Status</th>
                        <th style="width: 200px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="u in users" :key="u.id">
                        <td>{{ u.firstName }} {{ u.lastName }}</td>
                        <td>{{ u.email }}</td>
                        <td>{{ u.phone || '—' }}</td>
                        <td>{{ u.role }}</td>
                        <td>
                            <code v-if="u.tenantSubdomain">{{ u.tenantSubdomain }}</code>
                            <span v-else class="text-medium-emphasis">— global —</span>
                        </td>
                        <td>{{ u.status }}</td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" color="primary" @click="openEdit(u)">Edit</v-btn>
                            <v-btn v-if="u.role !== 'super_admin'" variant="text" size="small"
                                :loading="impersonatingId === u.id" @click="startImpersonation(u)">
                                Impersonate
                            </v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loadingUsers && users.length === 0">
                        <td colspan="7" class="text-center text-medium-emphasis py-8">No users match.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="superAdminDialog" max-width="560" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Add Super Admin</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="superAdminDialog = false"></v-btn>
                </v-card-title>
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
                    <v-text-field v-model="superAdminForm.password" type="password" label="Password (min 8 chars)" density="compact" class="mt-4"></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="superAdminDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="creatingSuperAdmin" @click="submitCreateSuperAdmin">Create</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="editDialog" max-width="760" persistent scrollable>
            <v-card v-if="editForm">
                <v-card-title class="d-flex align-center">
                    <span>Edit user</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="editDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div class="text-caption text-medium-emphasis mb-3">
                        {{ editTenantSubdomain ? `Tenant: ${editTenantSubdomain}` : 'Global account (no tenant)' }}
                    </div>

                    <div class="text-overline">Account</div>
                    <v-row dense>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.email" type="email" label="Email" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="3">
                            <v-select v-model="editForm.role" :items="roleOptions" label="Role" density="compact"></v-select>
                        </v-col>
                        <v-col cols="12" md="3">
                            <v-select v-model="editForm.status" :items="statusOptions" label="Status" density="compact"></v-select>
                        </v-col>
                    </v-row>
                    <v-switch v-model="editForm.emailVerified" color="primary" density="compact" hide-details
                        :label="`Email verified: ${editForm.emailVerified ? 'yes' : 'no'}`"></v-switch>

                    <div class="text-overline mt-4">Profile</div>
                    <v-row dense>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.firstName" label="First name" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.lastName" label="Last name" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.phone" label="Phone" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.birthdate" type="date" label="Birthdate" density="compact"></v-text-field>
                        </v-col>
                    </v-row>

                    <div class="text-overline mt-4">Emergency contact</div>
                    <v-row dense>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.emergencyContactName" label="Name" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.emergencyContactPhone" label="Phone" density="compact"></v-text-field>
                        </v-col>
                    </v-row>

                    <div class="text-overline mt-4">Address</div>
                    <v-row dense>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.addressLine" label="Address" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.addressLine2" label="Address line 2" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="5">
                            <v-text-field v-model="editForm.city" label="City" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="6" md="3">
                            <v-text-field v-model="editForm.state" label="State" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="6" md="4">
                            <v-text-field v-model="editForm.postalCode" label="Postal code" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.country" label="Country" density="compact"></v-text-field>
                        </v-col>
                    </v-row>

                    <div class="text-overline mt-4">Racer</div>
                    <v-row dense>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.bike" label="Bike" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.raceNumber" label="Race number" density="compact"></v-text-field>
                        </v-col>
                    </v-row>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="saving" @click="editDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="submitEdit">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { SuperAdminService, type SuperAdminUser, type UpdateUserPayload, type TenantSummary } from '@/services/SuperAdminService'
import { useConfirm } from '@/composables/useConfirm'
import authHelper from '@/helpers/AuthHelper'

const router = useRouter()
const service = new SuperAdminService()
const confirm = useConfirm()

const users = ref<SuperAdminUser[]>([])
const loadingUsers = ref(false)
const userQuery = ref('')
const impersonatingId = ref<string | null>(null)

// Filters (applied only when not searching; a search term searches all users platform-wide).
const filterRole = ref<string | null>(null)
const filterTenantId = ref<string | null>(null)
const filterStatus = ref<string | null>(null)
const tenants = ref<TenantSummary[]>([])
// Tenant roles only; riders and super admins are global and reachable via search.
const filterRoleOptions = ['tenant_admin', 'tenant_manager', 'tenant_staff']
const tenantOptions = computed(() =>
    tenants.value.map(t => ({ title: t.displayName ? `${t.displayName} (${t.subdomain})` : t.subdomain, value: t.id })))
const searchActive = computed(() => !!userQuery.value?.trim())
const hasFilters = computed(() => !!(filterRole.value || filterTenantId.value || filterStatus.value))

const superAdminDialog = ref(false)
const creatingSuperAdmin = ref(false)
const superAdminForm = ref({ firstName: '', lastName: '', email: '', password: '' })

const roleOptions = ['rider', 'tenant_admin', 'tenant_staff', 'super_admin']
const statusOptions = ['active', 'suspended', 'pending']

const editDialog = ref(false)
const saving = ref(false)
const editId = ref<string | null>(null)
const editTenantSubdomain = ref<string | null>(null)
const editForm = ref<UpdateUserPayload | null>(null)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(() => { loadTenants(); loadUsers() })

async function loadTenants() {
    try {
        const r = await service.listTenants()
        tenants.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load the tenant list for filtering.', 'error')
    }
}

async function loadUsers() {
    loadingUsers.value = true
    try {
        const q = userQuery.value?.trim()
        // A search term searches all users (filters ignored); otherwise filter the tenant-user list.
        const params = q
            ? { q }
            : {
                role: filterRole.value || undefined,
                tenantId: filterTenantId.value || undefined,
                status: filterStatus.value || undefined,
            }
        const r = await service.listUsers(params)
        users.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load users.', 'error')
    } finally {
        loadingUsers.value = false
    }
}

function onClearSearch() {
    userQuery.value = ''
    loadUsers()
}

function clearFilters() {
    filterRole.value = null
    filterTenantId.value = null
    filterStatus.value = null
    loadUsers()
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

async function openEdit(u: SuperAdminUser) {
    try {
        const r = await service.getUser(u.id)
        const d = (r.data as any).data
        editId.value = d.id
        editTenantSubdomain.value = d.tenantSubdomain
        editForm.value = {
            email: d.email,
            firstName: d.firstName,
            lastName: d.lastName,
            role: d.role,
            status: d.status,
            phone: d.phone,
            // <input type="date"> wants YYYY-MM-DD; the API returns an ISO timestamp.
            birthdate: d.birthdate ? d.birthdate.slice(0, 10) : null,
            emergencyContactName: d.emergencyContactName,
            emergencyContactPhone: d.emergencyContactPhone,
            addressLine: d.addressLine,
            addressLine2: d.addressLine2,
            city: d.city,
            state: d.state,
            postalCode: d.postalCode,
            country: d.country,
            bike: d.bike,
            raceNumber: d.raceNumber,
            emailVerified: d.emailVerified,
        }
        editDialog.value = true
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load user.', 'error')
    }
}

async function submitEdit() {
    if (!editId.value || !editForm.value) return
    saving.value = true
    try {
        const payload: UpdateUserPayload = { ...editForm.value, birthdate: editForm.value.birthdate || null }
        await service.updateUser(editId.value, payload)
        flash('User updated.', 'success')
        editDialog.value = false
        await loadUsers()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Update failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function startImpersonation(u: SuperAdminUser) {
    const ok = await confirm({
        title: 'Impersonate this user?',
        message: `${u.firstName} ${u.lastName} (${u.email}). You will see the app as they do until you stop.`,
        confirmText: 'Impersonate',
    })
    if (!ok) return
    try {
        impersonatingId.value = u.id
        const r = await service.impersonate(u.id)
        const data = (r.data as any).data
        if (data.tenantSubdomain) {
            // The tenant lives on its own subdomain, and localStorage is per-origin, so
            // we can't seed the session from here. Hand the JWT to the subdomain via the
            // URL fragment; main.ts adopts it on load (the "preview_token" bridge) so the
            // super admin lands logged in instead of on the signed-out page. We deliberately
            // do NOT call startImpersonation() here — that would clobber the super admin's
            // own apex session with the impersonated token.
            const rootDomain = import.meta.env.VITE_ROOT_DOMAIN ?? 'ridepass.local'
            const port = window.location.port ? `:${window.location.port}` : ''
            const label = encodeURIComponent(`${data.firstName} ${data.lastName} <${data.email}>`)
            window.location.href = `${window.location.protocol}//${data.tenantSubdomain}.${rootDomain}${port}/`
                + `#preview_token=${encodeURIComponent(data.token)}&preview_label=${label}`
        } else {
            // Same origin (e.g. a global rider): seed the session in place so the
            // stop-impersonation banner works without a cross-origin round trip.
            authHelper.startImpersonation({
                token: data.token,
                userId: data.userId,
                role: data.role,
                label: `${data.firstName} ${data.lastName} <${data.email}>`,
            })
            router.push('/')
        }
    } catch (err: any) {
        flash(err.response?.data?.error || 'Impersonation failed.', 'error')
    } finally {
        impersonatingId.value = null
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
