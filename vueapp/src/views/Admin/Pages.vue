<template>
    <v-container>
        <div class="d-flex align-center mb-2 ga-2 flex-wrap">
            <h1 class="text-h4">Pages</h1>
            <v-spacer></v-spacer>
            <v-btn color="primary" prepend-icon="mdi-plus" to="/Admin/Pages/New">New page</v-btn>
        </div>
        <p class="text-body-2 text-medium-emphasis mb-6">
            Build custom pages (About, Rules, Sponsors, etc.) and optionally add them to your site's navigation.
            Drag rows to reorder how they appear in the nav.
        </p>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th style="width: 40px"></th>
                        <th style="width: 64px"></th>
                        <th>Title</th>
                        <th style="width: 120px">Status</th>
                        <th style="width: 110px" class="text-center">In nav</th>
                        <th style="width: 160px">Updated</th>
                        <th style="width: 140px" class="text-right">Actions</th>
                    </tr>
                </thead>
                <draggable tag="tbody" :list="visibleRows" item-key="id" handle=".drag-handle"
                    :animation="180" ghost-class="drag-ghost" @end="onReorderEnd">
                    <template #item="{ element: p }">
                        <tr>
                            <td>
                                <v-icon class="drag-handle" color="grey">mdi-drag</v-icon>
                            </td>
                            <td>
                                <v-avatar v-if="p.heroImageUrl" rounded size="44">
                                    <v-img :src="absoluteUrl(p.heroImageUrl)" cover></v-img>
                                </v-avatar>
                                <v-avatar v-else rounded size="44" color="grey-lighten-2">
                                    <v-icon color="grey">mdi-file-document-outline</v-icon>
                                </v-avatar>
                            </td>
                            <td>
                                <router-link :to="`/Admin/Pages/${p.id}`" class="font-weight-medium text-primary">
                                    {{ p.title }}
                                </router-link>
                                <div class="text-caption text-medium-emphasis">/{{ p.slug }}</div>
                            </td>
                            <td>
                                <v-chip size="small" :color="p.status === 'published' ? 'success' : 'grey'" variant="tonal">
                                    {{ p.status === 'published' ? 'Published' : 'Draft' }}
                                </v-chip>
                            </td>
                            <td class="text-center">
                                <v-icon v-if="p.showInNav" color="primary" aria-label="Shown in navigation">mdi-check</v-icon>
                                <span v-else class="text-medium-emphasis">&mdash;</span>
                            </td>
                            <td class="text-caption">{{ formatDate(p.updatedAtUtc) }}</td>
                            <td class="text-right">
                                <v-btn icon variant="text" size="small" :to="`/Admin/Pages/${p.id}`" aria-label="Edit">
                                    <v-icon>mdi-pencil</v-icon>
                                </v-btn>
                                <v-btn icon variant="text" size="small" color="error" aria-label="Delete"
                                    :disabled="busyId === p.id" @click="remove(p)">
                                    <v-icon>mdi-delete</v-icon>
                                </v-btn>
                            </td>
                        </tr>
                    </template>
                </draggable>
            </v-table>
            <div v-if="!loading && pages.length === 0" class="text-center text-medium-emphasis py-8">
                No pages yet. Click "New page" to create your first.
            </div>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import draggable from 'vuedraggable'
import { PageService, type PageListItem } from '@/services/PageService'
import { useConfirm } from '@/composables/useConfirm'
import { useDragReorder } from '@/composables/useDragReorder'

const pageService = new PageService()
const confirm = useConfirm()

const pages = ref<PageListItem[]>([])
const loading = ref(true)
const busyId = ref<string | null>(null)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const apiUrl: string = import.meta.env.VITE_API_ENDPOINT ?? ''
function apiOrigin(): string {
    try { return new URL(apiUrl, window.location.origin).origin } catch { return '' }
}
function absoluteUrl(url: string | null | undefined): string {
    if (!url) return ''
    if (/^https?:\/\//i.test(url)) return url
    return `${apiOrigin()}${url}`
}

function formatDate(iso: string): string {
    return dayjs(iso).format('MMM D, YYYY')
}

// Drag-drop reorder of the page list (same composable every admin sort list uses).
const { visibleRows, onReorderEnd } = useDragReorder<PageListItem>({
    rows: pages,
    save: (items) => pageService.reorder(items),
    onError: async () => {
        flash("Couldn't reorder pages. Refresh and try again.", 'error')
        await load()
    },
})

async function load() {
    loading.value = true
    try {
        const resp = await pageService.list()
        pages.value = resp.data.data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load pages.', 'error')
    } finally {
        loading.value = false
    }
}

async function remove(p: PageListItem) {
    const ok = await confirm({
        title: 'Delete page?',
        message: `"${p.title}" will be permanently deleted. If it's linked in your navigation, that link will disappear too.`,
        confirmText: 'Delete',
        confirmColor: 'error',
    })
    if (!ok) return
    busyId.value = p.id
    try {
        await pageService.remove(p.id)
        await load()
        flash('Page deleted.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    } finally {
        busyId.value = null
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

onMounted(load)
</script>

<style scoped>
.drag-handle {
    cursor: grab;
}
.drag-handle:active {
    cursor: grabbing;
}
.drag-ghost {
    opacity: 0.5;
}
</style>
