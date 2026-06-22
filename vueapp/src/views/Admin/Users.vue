<template>
    <v-container>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <h1 class="text-h4">Users</h1>
            <v-spacer></v-spacer>
            <v-btn variant="text" @click="load">Refresh</v-btn>
            <v-btn color="primary" prepend-icon="mdi-account-plus" @click="openCreate">Add User</v-btn>
        </div>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Email</th>
                        <th style="width: 180px">Role</th>
                        <th style="width: 120px">Status</th>
                        <th style="width: 140px">Created</th>
                        <th style="width: 240px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="u in users" :key="u.id">
                        <td>{{ u.firstName }} {{ u.lastName }}</td>
                        <td>{{ u.email }}</td>
                        <td>
                            <div class="d-flex flex-wrap ga-1">
                                <v-chip v-for="r in (u.roles?.length ? u.roles : [u.role])" :key="r"
                                    size="small" :color="roleColor(r)">{{ roleTitle(r) }}</v-chip>
                            </div>
                        </td>
                        <td>
                            <v-chip size="small" :color="u.status === 'active' ? 'success' : 'grey'">
                                {{ u.status }}
                            </v-chip>
                        </td>
                        <td>{{ formatDate(u.createdAtUtc) }}</td>
                        <td class="text-right">
                            <v-menu>
                                <template #activator="{ props }">
                                    <v-btn variant="text" size="small" v-bind="props">Actions</v-btn>
                                </template>
                                <v-list density="compact">
                                    <v-list-item @click="openChangeRole(u)">
                                        <template #prepend><v-icon icon="mdi-shield-account"></v-icon></template>
                                        <v-list-item-title>Change Role</v-list-item-title>
                                    </v-list-item>
                                    <v-list-item v-if="u.status === 'active'" @click="setStatus(u, 'disabled')">
                                        <template #prepend><v-icon icon="mdi-account-off" color="warning"></v-icon></template>
                                        <v-list-item-title>Disable</v-list-item-title>
                                    </v-list-item>
                                    <v-list-item v-else @click="setStatus(u, 'active')">
                                        <template #prepend><v-icon icon="mdi-account-check" color="success"></v-icon></template>
                                        <v-list-item-title>Re-enable</v-list-item-title>
                                    </v-list-item>
                                    <v-list-item @click="resetPassword(u)">
                                        <template #prepend><v-icon icon="mdi-key-change"></v-icon></template>
                                        <v-list-item-title>Reset Password</v-list-item-title>
                                    </v-list-item>
                                </v-list>
                            </v-menu>
                        </td>
                    </tr>
                    <tr v-if="!loading && users.length === 0">
                        <td colspan="6" class="text-center text-medium-emphasis py-8">
                            No tenant users yet. Add one to get started.
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <!-- Create user -->
        <v-dialog v-model="createDialog" max-width="600" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Add User</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="createDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="createForm.firstName" label="First name" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="createForm.lastName" label="Last name" density="compact"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-text-field v-model="createForm.email" type="email" label="Email" density="compact" class="mt-4"></v-text-field>
                    <v-select v-model="createForm.roles" :items="roleOptions" item-title="title" item-value="value"
                        label="Roles" density="compact" class="mt-4" multiple chips closable-chips></v-select>
                    <p class="text-caption text-medium-emphasis">
                        Pick one or more. The user's permissions are the union of every role selected.
                    </p>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="creating" @click="createDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="creating" @click="submitCreate">Create</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Change roles -->
        <v-dialog v-model="roleDialog" max-width="500">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Change Roles</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="roleDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="mb-3">{{ roleTarget?.firstName }} {{ roleTarget?.lastName }} — {{ roleTarget?.email }}</p>
                    <v-select v-model="roleFormValues" :items="roleOptions" item-title="title" item-value="value"
                        label="Roles" density="compact" multiple chips closable-chips></v-select>
                    <p class="text-caption text-medium-emphasis">
                        Permissions are the union of every selected role.
                    </p>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="savingRole" @click="roleDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="savingRole" @click="saveRole">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- One-time credential reveal -->
        <v-dialog v-model="credsDialog" max-width="540" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ credsTitle }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="credsDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-alert type="warning" variant="tonal" class="mb-3">
                        Copy this password now — it is shown only once.
                    </v-alert>
                    <div class="text-body-2 mb-1"><strong>Email:</strong> {{ credsEmail }}</div>
                    <div class="text-body-2"><strong>Temporary Password:</strong> <code>{{ credsPassword }}</code></div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn color="primary" @click="credsDialog = false">Done</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3500">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import dayjs from 'dayjs'
import { UserService, type TenantUserListItem } from '@/services/UserService'
import { ASSIGNABLE_ROLES } from '@/helpers/TenantPermissions'
import { useConfirm } from '@/composables/useConfirm'

const service = new UserService()
const confirm = useConfirm()

const users = ref<TenantUserListItem[]>([])
const loading = ref(false)

const createDialog = ref(false)
const creating = ref(false)
const createForm = ref<{ email: string; firstName: string; lastName: string; roles: string[] }>(
    { email: '', firstName: '', lastName: '', roles: ['tenant_cashier'] })

const roleDialog = ref(false)
const savingRole = ref(false)
const roleTarget = ref<TenantUserListItem | null>(null)
const roleFormValues = ref<string[]>(['tenant_cashier'])

const credsDialog = ref(false)
const credsTitle = ref('')
const credsEmail = ref('')
const credsPassword = ref('')

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const roleOptions = ASSIGNABLE_ROLES

onMounted(load)

async function load() {
    loading.value = true
    try {
        const r = await service.listTenantUsers()
        users.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load users.', 'error')
    } finally {
        loading.value = false
    }
}

function openCreate() {
    createForm.value = { email: '', firstName: '', lastName: '', roles: ['tenant_cashier'] }
    createDialog.value = true
}

async function submitCreate() {
    if (!createForm.value.email.trim() || !createForm.value.firstName.trim() || !createForm.value.lastName.trim()) {
        flash('First name, last name, and email are required.', 'error')
        return
    }
    if (createForm.value.roles.length === 0) {
        flash('Pick at least one role.', 'error')
        return
    }
    creating.value = true
    try {
        const r = await service.createTenantUser({
            email: createForm.value.email.trim(),
            firstName: createForm.value.firstName.trim(),
            lastName: createForm.value.lastName.trim(),
            roles: createForm.value.roles,
        })
        const data: any = (r.data as any).data
        credsTitle.value = 'User created'
        credsEmail.value = data.email
        credsPassword.value = data.temporaryPassword
        createDialog.value = false
        credsDialog.value = true
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to create user.', 'error')
    } finally {
        creating.value = false
    }
}

function openChangeRole(u: TenantUserListItem) {
    roleTarget.value = u
    roleFormValues.value = u.roles?.length ? [...u.roles] : [u.role]
    roleDialog.value = true
}

async function saveRole() {
    if (!roleTarget.value) return
    if (roleFormValues.value.length === 0) {
        flash('Pick at least one role.', 'error')
        return
    }
    savingRole.value = true
    try {
        await service.updateTenantUserRoles(roleTarget.value.id, roleFormValues.value)
        roleDialog.value = false
        flash('Roles updated.', 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to change roles.', 'error')
    } finally {
        savingRole.value = false
    }
}

async function setStatus(u: TenantUserListItem, status: 'active' | 'disabled') {
    if (status === 'disabled' && !await confirm({
        title: 'Disable user?',
        message: `Disable ${u.email}? They lose access immediately until you re-enable them.`,
        confirmText: 'Disable',
        confirmColor: 'warning',
    })) return
    try {
        await service.updateTenantUserStatus(u.id, status)
        flash(status === 'disabled' ? 'User disabled.' : 'User re-enabled.', 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to update status.', 'error')
    }
}

async function resetPassword(u: TenantUserListItem) {
    if (!await confirm({ message: `Reset password for ${u.email}? They will need the new temporary password to log in.`, confirmText: 'Reset', confirmColor: 'error' })) return
    try {
        const r = await service.resetTenantUserPassword(u.id)
        const data: any = (r.data as any).data
        credsTitle.value = 'Password reset'
        credsEmail.value = u.email
        credsPassword.value = data.temporaryPassword
        credsDialog.value = true
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to reset password.', 'error')
    }
}

function roleTitle(role: string): string {
    return ASSIGNABLE_ROLES.find(r => r.value === role)?.title ?? role
}

function roleColor(role: string): string {
    switch (role) {
        case 'tenant_admin': return 'error'
        case 'tenant_manager': return 'primary'
        case 'tenant_cashier': return 'secondary'
        case 'tenant_scanner': return 'teal'
        case 'tenant_accountant': return 'indigo'
        default: return 'default'
    }
}

function formatDate(utc: string): string {
    return dayjs.utc(utc).local().format('YYYY-MM-DD')
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
