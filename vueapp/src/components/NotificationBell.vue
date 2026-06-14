<template>
    <v-menu location="bottom end" :close-on-content-click="false" v-model="menuOpen">
        <template #activator="{ props }">
            <v-btn v-bind="props" icon variant="text" aria-label="Notifications">
                <v-badge :model-value="unread > 0" :content="unread > 99 ? '99+' : unread" color="error" :offset-x="-2" :offset-y="-2">
                    <v-icon>mdi-bell</v-icon>
                </v-badge>
            </v-btn>
        </template>

        <v-card min-width="360" max-width="420">
            <v-card-title class="d-flex align-center py-2">
                <span class="text-subtitle-1">Notifications</span>
                <v-spacer></v-spacer>
                <v-btn v-if="unread > 0" size="x-small" variant="text" @click="markAll">Mark all read</v-btn>
                <v-btn icon size="x-small" variant="text" :title="'Notification settings'" @click="openSettings">
                    <v-icon>mdi-cog</v-icon>
                </v-btn>
            </v-card-title>
            <v-divider></v-divider>
            <v-list density="compact" max-height="500" class="overflow-y-auto py-0">
                <v-list-item v-if="loading" class="text-center text-medium-emphasis py-4">
                    Loading…
                </v-list-item>
                <v-list-item v-else-if="items.length === 0" class="text-center text-medium-emphasis py-6">
                    No notifications.
                </v-list-item>
                <v-list-item v-for="n in items" :key="n.id" :class="{ 'bg-blue-lighten-5': !n.isRead }"
                    @click="onClick(n)" style="cursor: pointer">
                    <template #prepend>
                        <v-icon :color="n.isRead ? 'grey' : 'primary'">{{ iconFor(n.kind) }}</v-icon>
                    </template>
                    <v-list-item-title :class="n.isRead ? '' : 'font-weight-bold'">
                        {{ n.title }}
                    </v-list-item-title>
                    <v-list-item-subtitle class="text-caption" style="white-space: normal">
                        {{ n.body }}
                    </v-list-item-subtitle>
                    <template #append>
                        <span class="text-caption text-medium-emphasis">{{ relativeTime(n.createdAt) }}</span>
                    </template>
                </v-list-item>
            </v-list>
        </v-card>
    </v-menu>

    <v-dialog v-model="settingsOpen" max-width="560">
        <v-card>
            <v-card-title class="d-flex align-center">
                <span>Notification settings</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" @click="settingsOpen = false"></v-btn>
            </v-card-title>
            <v-card-text>
                <p v-if="catalog.length === 0" class="text-medium-emphasis">
                    No configurable notifications for your role.
                </p>
                <div v-else>
                    <p class="text-caption text-medium-emphasis mb-3">
                        In-app notifications are always delivered. Toggle below to control which kinds also send you an email.
                    </p>
                    <div v-for="d in catalog" :key="d.kind" class="d-flex align-start py-2 border-b">
                        <div class="flex-grow-1">
                            <div class="text-body-2 font-weight-medium">{{ d.label }}</div>
                            <div class="text-caption text-medium-emphasis">{{ d.description }}</div>
                        </div>
                        <v-switch v-model="prefs[d.kind]" color="primary" hide-details density="compact"
                            @update:model-value="(v) => savePref(d.kind, !!v)"></v-switch>
                    </div>
                </div>
            </v-card-text>
            <v-card-actions>
                <v-spacer></v-spacer>
                <v-btn @click="settingsOpen = false">Close</v-btn>
            </v-card-actions>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRouter } from 'vue-router'
import dayjs from 'dayjs'
import relativeTimePlugin from 'dayjs/plugin/relativeTime'
import { NotificationService, type AppNotification, type NotificationKindDescriptor } from '@/services/NotificationService'

dayjs.extend(relativeTimePlugin)

const router = useRouter()
const service = new NotificationService()

const items = ref<AppNotification[]>([])
const unread = ref(0)
const loading = ref(false)
const menuOpen = ref(false)

const settingsOpen = ref(false)
const catalog = ref<NotificationKindDescriptor[]>([])
const prefs = ref<Record<string, boolean>>({})

let pollHandle: number | null = null

async function poll() {
    try {
        const r = await service.unreadCount()
        unread.value = (r.data as any).data.count
    } catch {
        // Silent — auth interceptor handles 401, anything else is transient.
    }
}

async function loadList() {
    loading.value = true
    try {
        const r = await service.list(50)
        items.value = (r.data as any).data
    } finally {
        loading.value = false
    }
}

watch(menuOpen, async (open) => {
    if (open) await loadList()
})

async function onClick(n: AppNotification) {
    if (!n.isRead) {
        try {
            await service.markRead(n.id)
            n.isRead = true
            unread.value = Math.max(0, unread.value - 1)
        } catch { /* ignore */ }
    }
    if (n.linkUrl) {
        menuOpen.value = false
        router.push(n.linkUrl)
    }
}

async function openSettings() {
    menuOpen.value = false
    settingsOpen.value = true
    try {
        const [c, p] = await Promise.all([service.getCatalog(), service.getPreferences()])
        catalog.value = (c.data as any).data
        const stored: Record<string, boolean> = {}
        for (const row of (p.data as any).data) stored[row.kind] = row.emailEnabled
        // default = true for any kind without an explicit row
        const merged: Record<string, boolean> = {}
        for (const d of catalog.value) merged[d.kind] = d.kind in stored ? stored[d.kind] : true
        prefs.value = merged
    } catch { /* ignore — empty catalog for non-super-admins */ }
}

async function savePref(kind: string, value: boolean) {
    try {
        await service.setPreference(kind, value)
    } catch {
        // Roll back the toggle on failure
        prefs.value[kind] = !value
    }
}

async function markAll() {
    try {
        await service.markAllRead()
        items.value.forEach(n => n.isRead = true)
        unread.value = 0
    } catch { /* ignore */ }
}

function iconFor(kind: string): string {
    if (kind.startsWith('dispute')) return 'mdi-alert-circle'
    if (kind.startsWith('refund')) return 'mdi-cash-refund'
    if (kind.startsWith('payout')) return 'mdi-bank-transfer'
    return 'mdi-information'
}

function relativeTime(iso: string): string {
    return dayjs.utc(iso).fromNow()
}

onMounted(() => {
    poll()
    pollHandle = window.setInterval(poll, 60_000)   // re-poll unread count every minute
})

onBeforeUnmount(() => {
    if (pollHandle !== null) window.clearInterval(pollHandle)
})
</script>
