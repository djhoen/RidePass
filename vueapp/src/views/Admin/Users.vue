<template>
    <v-container>
        <h1 class="text-h4 mb-6">User Management</h1>

        <v-card class="mb-4">
            <v-card-text>
                <v-form @submit.prevent="searchUsers">
                    <v-row align="center">
                        <v-col cols="12" sm="4">
                            <v-text-field v-model="search.email" label="Email" density="compact"
                                hide-details></v-text-field>
                        </v-col>
                        <v-col cols="12" sm="4">
                            <v-text-field v-model="search.name" label="Name" density="compact"
                                hide-details></v-text-field>
                        </v-col>
                        <v-col cols="12" sm="4">
                            <v-btn type="submit" color="primary" :loading="loading">Search</v-btn>
                        </v-col>
                    </v-row>
                </v-form>
            </v-card-text>
        </v-card>

        <v-card v-if="users.length > 0">
            <v-table>
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Email</th>
                        <th>Roles</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="user in users" :key="user.id">
                        <td>{{ user.firstName }} {{ user.lastName }}</td>
                        <td>{{ user.email }}</td>
                        <td>
                            <v-chip v-for="role in user.roles" :key="role" size="small" class="mr-1">
                                {{ role }}
                            </v-chip>
                        </td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" @click="editUser(user)">Edit Roles</v-btn>
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="dialog" max-width="500">
            <v-card>
                <v-card-title>Edit User Roles</v-card-title>
                <v-card-text>
                    <p class="mb-4">{{ selectedUser?.firstName }} {{ selectedUser?.lastName }}</p>
                    <v-checkbox v-for="role in availableRoles" :key="role" v-model="selectedRoles" :label="role"
                        :value="role" density="compact" hide-details></v-checkbox>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="dialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="saveRoles">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { UserService } from '@/services/UserService'

const userService = new UserService()

const search = ref({ email: '', name: '' })
const users = ref<any[]>([])
const loading = ref(false)
const saving = ref(false)
const dialog = ref(false)
const selectedUser = ref<any>(null)
const selectedRoles = ref<string[]>([])
const availableRoles = ['Admin', 'User', 'Editor']
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('success')

async function searchUsers() {
    try {
        loading.value = true
        const response = await userService.searchUsers(search.value)
        users.value = response.data
    } catch {
        snackbarText.value = 'Failed to search users.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        loading.value = false
    }
}

function editUser(user: any) {
    selectedUser.value = user
    selectedRoles.value = [...(user.roles || [])]
    dialog.value = true
}

async function saveRoles() {
    try {
        saving.value = true
        await userService.saveUserRoles({ userId: selectedUser.value.id, roles: selectedRoles.value })
        selectedUser.value.roles = [...selectedRoles.value]
        dialog.value = false
        snackbarText.value = 'Roles updated successfully!'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch {
        snackbarText.value = 'Failed to save roles.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        saving.value = false
    }
}
</script>
