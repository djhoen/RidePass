<template>
    <v-container fluid class="inbox-container">
        <h1 class="text-h4 mb-2">Inbox</h1>
        <p class="text-body-2 text-medium-emphasis mb-4">
            Two-way SMS conversations with your customers. Inbound texts appear here; refresh to
            check for new messages. Replies go out from your provisioned number.
        </p>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">
            {{ loadError }}
        </v-alert>

        <v-row no-gutters class="inbox-pane">
            <!-- LEFT: conversation list -->
            <v-col cols="12" md="4" class="inbox-list-col">
                <v-card class="d-flex flex-column" style="height: 100%">
                    <v-toolbar density="compact" color="surface" flat>
                        <v-toolbar-title class="text-subtitle-1">
                            Conversations
                            <v-chip v-if="unreadCount > 0" size="x-small" color="primary" class="ml-2">
                                {{ unreadCount }} unread
                            </v-chip>
                        </v-toolbar-title>
                        <v-spacer></v-spacer>
                        <v-btn icon size="small" :loading="loadingList" @click="refresh">
                            <v-icon icon="mdi-refresh"></v-icon>
                        </v-btn>
                    </v-toolbar>

                    <div class="px-3 py-1 d-flex align-center">
                        <v-checkbox
                            v-model="includeArchived"
                            label="Show archived"
                            density="compact"
                            hide-details
                            @update:model-value="refresh"></v-checkbox>
                    </div>

                    <v-divider></v-divider>

                    <div v-if="loadingList && conversations.length === 0" class="pa-6 text-center">
                        <v-progress-circular indeterminate color="primary" size="32"></v-progress-circular>
                    </div>

                    <v-list v-else-if="conversations.length > 0" lines="two" class="inbox-list flex-grow-1" density="compact">
                        <v-list-item
                            v-for="c in conversations"
                            :key="c.id"
                            :active="c.id === selectedId"
                            :class="{ 'font-weight-bold': c.unread }"
                            @click="select(c)">
                            <template #prepend>
                                <v-icon
                                    :icon="c.unread ? 'mdi-circle' : 'mdi-circle-outline'"
                                    :color="c.unread ? 'primary' : 'grey'"
                                    size="x-small"
                                    class="mr-1"></v-icon>
                            </template>
                            <v-list-item-title class="d-flex align-center">
                                <span>{{ c.customerName || c.customerPhone }}</span>
                                <v-chip v-if="c.optedOut" size="x-small" color="error" variant="tonal" class="ml-2">
                                    opted out
                                </v-chip>
                                <v-chip v-if="c.status === 'archived'" size="x-small" variant="tonal" class="ml-2">
                                    archived
                                </v-chip>
                            </v-list-item-title>
                            <v-list-item-subtitle>
                                <span v-if="c.customerName">{{ c.customerPhone }} · </span>{{ formatRelative(c.lastMessageAtUtc) }}
                            </v-list-item-subtitle>
                        </v-list-item>
                    </v-list>

                    <div v-else class="pa-6 text-center text-medium-emphasis">
                        <v-icon icon="mdi-inbox-outline" size="48" class="mb-2"></v-icon>
                        <div>No conversations yet.</div>
                        <div class="text-caption">Inbound texts to your number will appear here.</div>
                    </div>
                </v-card>
            </v-col>

            <!-- RIGHT: thread + reply -->
            <v-col cols="12" md="8" class="inbox-thread-col">
                <v-card class="d-flex flex-column" style="height: 100%">
                    <template v-if="selected">
                        <v-toolbar density="compact" color="surface" flat>
                            <v-toolbar-title class="text-subtitle-1">
                                {{ selected.customerName || selected.customerPhone }}
                                <span v-if="selected.customerName" class="text-caption text-medium-emphasis ml-2">
                                    {{ selected.customerPhone }}
                                </span>
                            </v-toolbar-title>
                            <v-chip v-if="selected.optedOut" size="small" color="error" variant="tonal" class="ml-2">
                                opted out
                            </v-chip>
                            <v-spacer></v-spacer>
                            <v-btn
                                size="small"
                                variant="tonal"
                                :loading="archiving"
                                @click="toggleArchive">
                                {{ selected.status === 'archived' ? 'Restore' : 'Archive' }}
                            </v-btn>
                        </v-toolbar>

                        <v-divider></v-divider>

                        <div ref="threadScroller" class="thread-scroller flex-grow-1 pa-4">
                            <div v-if="loadingDetail" class="text-center pa-6">
                                <v-progress-circular indeterminate color="primary" size="28"></v-progress-circular>
                            </div>

                            <template v-else>
                                <div
                                    v-for="m in selected.messages"
                                    :key="m.id"
                                    :class="['msg-row', m.direction === 'outbound' ? 'outbound' : 'inbound']">
                                    <div class="msg-bubble">
                                        <div class="msg-body">{{ m.body }}</div>
                                        <div class="msg-meta text-caption">
                                            {{ formatDateTime(m.createdAtUtc) }}
                                            <span v-if="m.direction === 'outbound'" class="ml-2">
                                                · {{ m.status }}
                                                <span v-if="m.numSegments">· {{ m.numSegments }} seg</span>
                                            </span>
                                            <span v-if="m.errorMessage" class="text-error ml-2">
                                                · {{ m.errorMessage }}
                                            </span>
                                        </div>
                                    </div>
                                </div>
                            </template>
                        </div>

                        <v-divider></v-divider>

                        <div class="reply-box pa-3">
                            <v-alert
                                v-if="selected.optedOut"
                                type="warning"
                                variant="tonal"
                                density="compact"
                                class="mb-2">
                                This customer has opted out. Replies are blocked until they text START.
                            </v-alert>
                            <div class="d-flex ga-2 align-end">
                                <v-textarea
                                    v-model="replyBody"
                                    placeholder="Type a reply…"
                                    rows="2"
                                    auto-grow
                                    max-rows="6"
                                    density="compact"
                                    hide-details
                                    :disabled="selected.optedOut || sending"
                                    @keydown.ctrl.enter.prevent="send"
                                    @keydown.meta.enter.prevent="send"></v-textarea>
                                <v-btn
                                    color="primary"
                                    :loading="sending"
                                    :disabled="selected.optedOut || replyBody.trim().length === 0"
                                    @click="send">
                                    Send
                                </v-btn>
                            </div>
                            <div class="text-caption text-medium-emphasis mt-1">
                                Ctrl/Cmd + Enter to send.
                            </div>
                        </div>
                    </template>

                    <div v-else class="d-flex align-center justify-center text-medium-emphasis" style="height: 100%">
                        <div class="text-center">
                            <v-icon icon="mdi-message-text-outline" size="64" class="mb-2"></v-icon>
                            <div>Select a conversation to view the thread.</div>
                        </div>
                    </div>
                </v-card>
            </v-col>
        </v-row>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import relativeTime from 'dayjs/plugin/relativeTime'
import { InboxService, type ConversationListItem, type ConversationDetail } from '@/services/InboxService'

dayjs.extend(relativeTime)

const service = new InboxService()

const conversations = ref<ConversationListItem[]>([])
const selectedId = ref<string | null>(null)
const selected = ref<ConversationDetail | null>(null)
const includeArchived = ref(false)

const loadingList = ref(false)
const loadingDetail = ref(false)
const sending = ref(false)
const archiving = ref(false)
const loadError = ref<string | null>(null)
const replyBody = ref('')
const threadScroller = ref<HTMLElement | null>(null)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const unreadCount = computed(() => conversations.value.filter(c => c.unread).length)

onMounted(refresh)

async function refresh() {
    loadingList.value = true
    loadError.value = null
    try {
        const r = await service.list(includeArchived.value)
        conversations.value = (r.data as any).data
        // If a conversation was selected, refresh its detail too so the reply
        // we just sent (or new inbound) shows up without a manual click.
        if (selectedId.value) {
            const stillVisible = conversations.value.find(c => c.id === selectedId.value)
            if (stillVisible) {
                await loadDetail(selectedId.value)
            } else {
                selectedId.value = null
                selected.value = null
            }
        }
    } catch (err: any) {
        loadError.value = err.response?.data?.error || 'Failed to load conversations.'
    } finally {
        loadingList.value = false
    }
}

async function select(c: ConversationListItem) {
    selectedId.value = c.id
    await loadDetail(c.id)
    // Mark-read after detail loads so a slow request doesn't blank the unread
    // badge before the user actually sees the thread.
    if (c.unread) {
        try {
            await service.markRead(c.id)
            c.unread = false
        } catch {
            // Non-fatal — they'll mark read on next click.
        }
    }
}

async function loadDetail(id: string) {
    loadingDetail.value = true
    try {
        const r = await service.get(id)
        selected.value = (r.data as any).data
        await nextTick()
        scrollToBottom()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load conversation.', 'error')
    } finally {
        loadingDetail.value = false
    }
}

async function send() {
    if (!selected.value || sending.value) return
    const body = replyBody.value.trim()
    if (body.length === 0) return
    sending.value = true
    try {
        await service.reply(selected.value.id, body)
        replyBody.value = ''
        await loadDetail(selected.value.id)
        // Bump the conversation to the top of the list and refresh its
        // lastMessageAtUtc so ordering matches what the user just did.
        const idx = conversations.value.findIndex(c => c.id === selected.value!.id)
        if (idx >= 0) {
            const c = conversations.value[idx]
            c.lastMessageAtUtc = new Date().toISOString()
            conversations.value.splice(idx, 1)
            conversations.value.unshift(c)
        }
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to send reply.', 'error')
    } finally {
        sending.value = false
    }
}

async function toggleArchive() {
    if (!selected.value || archiving.value) return
    archiving.value = true
    const next = selected.value.status === 'archived' ? 'active' : 'archived'
    try {
        await service.setStatus(selected.value.id, next)
        selected.value.status = next
        // Mirror the change into the list row so the chip updates without a
        // full reload; drop the row from view if it's now archived and the
        // "show archived" toggle is off.
        const listRow = conversations.value.find(c => c.id === selected.value!.id)
        if (listRow) listRow.status = next
        if (next === 'archived' && !includeArchived.value) {
            conversations.value = conversations.value.filter(c => c.id !== selected.value!.id)
            selectedId.value = null
            selected.value = null
        }
        flash(next === 'archived' ? 'Conversation archived.' : 'Conversation restored.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to update status.', 'error')
    } finally {
        archiving.value = false
    }
}

function scrollToBottom() {
    const el = threadScroller.value
    if (el) el.scrollTop = el.scrollHeight
}

function formatRelative(utc: string): string {
    return dayjs.utc(utc).local().fromNow()
}

function formatDateTime(utc: string): string {
    return dayjs.utc(utc).local().format('MMM D, h:mm A')
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>

<style scoped>
.inbox-container {
    height: calc(100vh - 64px);
    display: flex;
    flex-direction: column;
}

.inbox-pane {
    flex: 1;
    min-height: 0;
}

.inbox-list-col,
.inbox-thread-col {
    height: 100%;
    padding: 4px;
}

.inbox-list {
    overflow-y: auto;
}

.thread-scroller {
    overflow-y: auto;
    background-color: rgba(0, 0, 0, 0.02);
}

.msg-row {
    display: flex;
    margin-bottom: 12px;
}

.msg-row.inbound {
    justify-content: flex-start;
}

.msg-row.outbound {
    justify-content: flex-end;
}

.msg-bubble {
    max-width: 70%;
    padding: 8px 12px;
    border-radius: 12px;
    background-color: rgb(var(--v-theme-surface-variant));
}

.msg-row.outbound .msg-bubble {
    background-color: rgb(var(--v-theme-primary));
    color: rgb(var(--v-theme-on-primary));
}

.msg-body {
    white-space: pre-wrap;
    word-break: break-word;
}

.msg-meta {
    opacity: 0.7;
    margin-top: 4px;
}

.reply-box {
    background-color: rgb(var(--v-theme-surface));
}
</style>
