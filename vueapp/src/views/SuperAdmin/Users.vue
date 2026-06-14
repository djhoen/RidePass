<template>
    <v-container>
        <h1 class="text-h4 mb-4">Users</h1>

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

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { SuperAdminService, type SuperAdminUser } from '@/services/SuperAdminService'
import { useConfirm } from '@/composables/useConfirm'
import authHelper from '@/helpers/AuthHelper'

const router = useRouter()
const service = new SuperAdminService()
const confirm = useConfirm()

const users = ref<SuperAdminUser[]>([])
const loadingUsers = ref(false)
const userQuery = ref('')
const impersonatingId = ref<string | null>(null)

const superAdminDialog = ref(false)
const creatingSuperAdmin = ref(false)
const superAdminForm = ref({ firstName: '', lastName: '', email: '', password: '' })

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(loadUsers)

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
        authHelper.startImpersonation({
            token: data.token,
            userId: data.userId,
            role: data.role,
            label: `${data.firstName} ${data.lastName} <${data.email}>`,
        })
        if (data.tenantSubdomain) {
            const rootDomain = import.meta.env.VITE_ROOT_DOMAIN ?? 'ridepass.local'
            const port = window.location.port ? `:${window.location.port}` : ''
            window.location.href = `${window.location.protocol}//${data.tenantSubdomain}.${rootDomain}${port}/`
        } else {
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
