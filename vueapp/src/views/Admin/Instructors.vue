<template>
    <v-container>
        <div class="d-flex align-center mb-4 flex-wrap ga-3">
            <h1 class="text-h4">Instructors</h1>
            <v-spacer></v-spacer>
            <v-switch v-model="showInactive" color="primary" density="compact" hide-details
                label="Show inactive" style="flex: 0 0 auto"></v-switch>
            <v-btn variant="text" @click="load">Refresh</v-btn>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">Add Instructor</v-btn>
        </div>

        <p class="text-caption text-medium-emphasis mb-4">
            Coaches you can assign to lesson events. When you assign an instructor to a lesson, they can’t be
            booked on another lesson that overlaps the same time. Assign them on each lesson from
            <router-link to="/Admin/Events">Manage Events</router-link> → the lesson’s Entry &amp; Add-ons tab.
        </p>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th style="width: 64px"></th>
                        <th>Name</th>
                        <th>Contact</th>
                        <th style="width: 90px">Active</th>
                        <th style="width: 90px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-if="visibleRows.length === 0">
                        <td colspan="5" class="text-center text-medium-emphasis py-6">
                            No instructors yet. Add your first coach to start scheduling lessons.
                        </td>
                    </tr>
                    <tr v-for="i in visibleRows" :key="i.id">
                        <td>
                            <v-avatar size="40" color="grey-lighten-2">
                                <v-img v-if="i.imageUrl" :src="absoluteUrl(i.imageUrl)" :alt="i.name"></v-img>
                                <span v-else class="text-caption">{{ initials(i.name) }}</span>
                            </v-avatar>
                        </td>
                        <td>
                            <div class="font-weight-medium">{{ i.name }}</div>
                            <div v-if="i.bio" class="text-caption text-medium-emphasis text-truncate" style="max-width: 340px">
                                {{ i.bio }}
                            </div>
                        </td>
                        <td>
                            <div v-if="i.email" class="text-caption">{{ i.email }}</div>
                            <div v-if="i.phone" class="text-caption text-medium-emphasis">{{ i.phone }}</div>
                            <span v-if="!i.email && !i.phone" class="text-caption text-medium-emphasis">—</span>
                        </td>
                        <td>
                            <v-chip :color="i.isActive ? 'success' : 'grey'" size="small" variant="tonal">
                                {{ i.isActive ? 'Active' : 'Inactive' }}
                            </v-chip>
                        </td>
                        <td class="text-right">
                            <v-btn icon="mdi-pencil" size="small" variant="text" @click="openEdit(i)"></v-btn>
                            <v-btn icon="mdi-delete" size="small" variant="text" color="error" @click="remove(i)"></v-btn>
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <!-- Create / edit -->
        <v-dialog v-model="dialogOpen" max-width="560">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ editing ? 'Edit Instructor' : 'Add Instructor' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="dialogOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.name" label="Name" density="compact"
                        :rules="[v => !!(v && v.trim()) || 'Name is required']"></v-text-field>
                    <v-text-field v-model="form.email" label="Email (optional)" type="email"
                        density="compact" class="mt-4"></v-text-field>
                    <v-text-field v-model="form.phone" label="Phone (optional)"
                        density="compact" class="mt-4"></v-text-field>
                    <v-textarea v-model="form.bio" label="Bio (optional)" rows="3" auto-grow
                        density="compact" class="mt-4"></v-textarea>
                    <v-text-field v-model="form.imageUrl" label="Photo URL (optional)"
                        density="compact" class="mt-4"></v-text-field>
                    <v-text-field v-model.number="form.maxStudentsPerSession" type="number" min="1" max="100"
                        label="Max students per session" density="compact" class="mt-4"
                        hint="Caps any training group this coach runs, alongside the group's own limit."
                        persistent-hint></v-text-field>
                    <v-switch v-model="form.isActive" label="Active" color="primary"
                        density="compact" hide-details class="mt-2"></v-switch>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="dialogOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="save">
                        {{ editing ? 'Save changes' : 'Add' }}
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar.show" :color="snackbar.color" :timeout="4500">
            {{ snackbar.text }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { InstructorService, type Instructor, type UpsertInstructor } from '@/services/InstructorService'
import { useConfirm } from '@/composables/useConfirm'

const service = new InstructorService()
const confirm = useConfirm()

const rows = ref<Instructor[]>([])
const showInactive = ref(false)
const dialogOpen = ref(false)
const saving = ref(false)
const editing = ref<Instructor | null>(null)

const snackbar = ref({ show: false, text: '', color: 'success' as 'success' | 'error' })
function flash(text: string, color: 'success' | 'error' = 'success') {
    snackbar.value = { show: true, text, color }
}

const visibleRows = computed(() =>
    showInactive.value ? rows.value : rows.value.filter(i => i.isActive))

const form = ref<UpsertInstructor>({
    name: '', email: null, phone: null, bio: null, imageUrl: null, isActive: true, sortOrder: 100,
    maxStudentsPerSession: 8,
})

const apiUrl: string = (import.meta as any).env?.VITE_API_ENDPOINT ?? ''
function apiOrigin(): string {
    try { return new URL(apiUrl, window.location.origin).origin } catch { return '' }
}
function absoluteUrl(url: string | null): string {
    if (!url) return ''
    if (/^https?:\/\//i.test(url)) return url
    return `${apiOrigin()}${url}`
}
function initials(name: string): string {
    return name.split(/\s+/).filter(Boolean).slice(0, 2).map(p => p[0]?.toUpperCase() ?? '').join('')
}

async function load() {
    try {
        const r = await service.listForAdmin()
        rows.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Couldn’t load instructors. Check your connection and refresh.', 'error')
    }
}

function openCreate() {
    editing.value = null
    form.value = { name: '', email: null, phone: null, bio: null, imageUrl: null, isActive: true, sortOrder: 100,
        maxStudentsPerSession: 8 }
    dialogOpen.value = true
}

function openEdit(i: Instructor) {
    editing.value = i
    form.value = {
        name: i.name, email: i.email, phone: i.phone, bio: i.bio,
        imageUrl: i.imageUrl, isActive: i.isActive, sortOrder: i.sortOrder,
        maxStudentsPerSession: i.maxStudentsPerSession ?? 8,
    }
    dialogOpen.value = true
}

async function save() {
    if (!form.value.name.trim()) { flash('Name is required.', 'error'); return }
    const payload: UpsertInstructor = {
        name: form.value.name.trim(),
        email: form.value.email?.trim() || null,
        phone: form.value.phone?.trim() || null,
        bio: form.value.bio?.trim() || null,
        imageUrl: form.value.imageUrl?.trim() || null,
        isActive: form.value.isActive,
        sortOrder: form.value.sortOrder,
        maxStudentsPerSession: form.value.maxStudentsPerSession || 8,
    }
    try {
        saving.value = true
        if (editing.value) {
            await service.update(editing.value.id, payload)
            flash('Instructor saved.')
        } else {
            await service.create(payload)
            flash('Instructor added.')
        }
        dialogOpen.value = false
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Couldn’t save the instructor. Please try again.', 'error')
    } finally {
        saving.value = false
    }
}

async function remove(i: Instructor) {
    const ok = await confirm({
        title: 'Delete instructor',
        message: `Delete "${i.name}"? If they’re assigned to any lessons this won’t be allowed — deactivate them instead.`,
        confirmText: 'Delete',
        cancelText: 'Cancel',
    })
    if (!ok) return
    try {
        await service.remove(i.id)
        flash('Instructor deleted.')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Couldn’t delete the instructor. Please try again.', 'error')
    }
}

onMounted(load)
</script>
