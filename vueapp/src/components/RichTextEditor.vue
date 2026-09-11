<template>
    <div class="rich-text-editor">
        <div v-if="editor" class="toolbar">
            <v-btn-group density="compact" variant="text" divided>
                <v-btn size="small" :color="editor.isActive('bold') ? 'primary' : undefined"
                    @click="editor.chain().focus().toggleBold().run()" aria-label="Bold">
                    <v-icon>mdi-format-bold</v-icon>
                </v-btn>
                <v-btn size="small" :color="editor.isActive('italic') ? 'primary' : undefined"
                    @click="editor.chain().focus().toggleItalic().run()" aria-label="Italic">
                    <v-icon>mdi-format-italic</v-icon>
                </v-btn>
                <v-btn size="small" :color="editor.isActive('underline') ? 'primary' : undefined"
                    @click="editor.chain().focus().toggleUnderline().run()" aria-label="Underline">
                    <v-icon>mdi-format-underline</v-icon>
                </v-btn>
                <v-btn size="small" :color="editor.isActive('strike') ? 'primary' : undefined"
                    @click="editor.chain().focus().toggleStrike().run()" aria-label="Strikethrough">
                    <v-icon>mdi-format-strikethrough</v-icon>
                </v-btn>
            </v-btn-group>

            <v-btn-group density="compact" variant="text" divided class="ml-2">
                <v-btn size="small" :color="editor.isActive('heading', { level: 1 }) ? 'primary' : undefined"
                    @click="editor.chain().focus().toggleHeading({ level: 1 }).run()" aria-label="Heading 1">H1</v-btn>
                <v-btn size="small" :color="editor.isActive('heading', { level: 2 }) ? 'primary' : undefined"
                    @click="editor.chain().focus().toggleHeading({ level: 2 }).run()" aria-label="Heading 2">H2</v-btn>
                <v-btn size="small" :color="editor.isActive('heading', { level: 3 }) ? 'primary' : undefined"
                    @click="editor.chain().focus().toggleHeading({ level: 3 }).run()" aria-label="Heading 3">H3</v-btn>
            </v-btn-group>

            <v-btn-group density="compact" variant="text" divided class="ml-2">
                <v-btn size="small" :color="editor.isActive('bulletList') ? 'primary' : undefined"
                    @click="editor.chain().focus().toggleBulletList().run()" aria-label="Bullet list">
                    <v-icon>mdi-format-list-bulleted</v-icon>
                </v-btn>
                <v-btn size="small" :color="editor.isActive('orderedList') ? 'primary' : undefined"
                    @click="editor.chain().focus().toggleOrderedList().run()" aria-label="Ordered list">
                    <v-icon>mdi-format-list-numbered</v-icon>
                </v-btn>
                <v-btn size="small" :color="editor.isActive('blockquote') ? 'primary' : undefined"
                    @click="editor.chain().focus().toggleBlockquote().run()" aria-label="Blockquote">
                    <v-icon>mdi-format-quote-close</v-icon>
                </v-btn>
            </v-btn-group>

            <v-btn-group density="compact" variant="text" divided class="ml-2">
                <v-btn size="small" :color="editor.isActive('link') ? 'primary' : undefined"
                    @click="toggleLink" aria-label="Insert link">
                    <v-icon>mdi-link</v-icon>
                </v-btn>
                <v-btn size="small" @click="editor.chain().focus().setHorizontalRule().run()" aria-label="Horizontal rule">
                    <v-icon>mdi-minus</v-icon>
                </v-btn>
                <v-btn size="small" @click="editor.chain().focus().unsetAllMarks().clearNodes().run()" aria-label="Clear formatting">
                    <v-icon>mdi-format-clear</v-icon>
                </v-btn>
            </v-btn-group>

            <!-- Inline image insert — only rendered when the consumer opts in via the
                 uploadImage prop (keeps the Blog editor unchanged unless it opts in). -->
            <v-btn-group v-if="uploadImage" density="compact" variant="text" divided class="ml-2">
                <v-btn size="small" :loading="uploadingImage" aria-label="Insert image" @click="imageFileInput?.click()">
                    <v-icon>mdi-image</v-icon>
                </v-btn>
            </v-btn-group>
            <input v-if="uploadImage" ref="imageFileInput" type="file" accept="image/png,image/jpeg,image/webp,image/gif"
                class="d-none" @change="onImageFileChange" />
            <!-- YouTube video: stored as a linked thumbnail so it works in email; pages show a player. -->
            <v-btn-group density="compact" variant="text" divided class="ml-2">
                <v-btn size="small" :active="editor?.isActive('videoEmbed')" aria-label="Insert video" @click="openVideoDialog">
                    <v-icon>mdi-youtube</v-icon>
                </v-btn>
            </v-btn-group>
            <!-- Email call-to-action button: only for the campaign and automation editors. -->
            <v-btn-group v-if="emailButtons" density="compact" variant="text" divided class="ml-2">
                <v-btn size="small" :active="editor?.isActive('emailButton')" aria-label="Insert button" @click="openButtonDialog">
                    <v-icon>mdi-gesture-tap-button</v-icon>
                </v-btn>
            </v-btn-group>
        </div>

        <!-- Button dialog: label + link. Editing an existing button pre-fills it. -->
        <v-dialog v-model="buttonDialog" max-width="440">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ buttonExisting ? 'Edit button' : 'Insert button' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="buttonDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="buttonLabel" label="Button text" density="compact" autofocus hide-details
                        placeholder="Buy tickets"></v-text-field>
                    <v-text-field v-model="buttonUrl" label="Link URL" density="compact" class="mt-4" hide-details
                        placeholder="https://example.com or /Events" @keyup.enter="applyButton"></v-text-field>
                    <div class="text-caption text-medium-emphasis mt-2">
                        Rendered as a solid button in your primary color. A link starting with / points at your site.
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-btn v-if="buttonExisting" color="error" variant="text" @click="removeButton">Remove</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="buttonDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :disabled="!buttonLabel.trim() || !buttonUrl.trim()" @click="applyButton">
                        {{ buttonExisting ? 'Update' : 'Insert' }}
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>
        <div class="d-none">
        </div>

        <!-- Video dialog: a YouTube link or the embed snippet from Share > Embed. -->
        <v-dialog v-model="videoDialog" max-width="480">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Insert video</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="videoDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-textarea v-model="videoInput" label="YouTube link or embed code" density="compact" rows="2" auto-grow autofocus
                        hide-details placeholder="https://www.youtube.com/watch?v=... or <iframe ...>"></v-textarea>
                    <div class="text-caption mt-2" :class="videoId ? 'text-success' : 'text-medium-emphasis'">
                        <template v-if="videoId">Found video {{ videoId }}.</template>
                        <template v-else-if="videoInput.trim()">That doesn't look like a YouTube link. Paste the page URL or the Share, Embed code.</template>
                        <template v-else>Emails can't play video, so it goes in as a thumbnail that opens the video. On a web page it plays in place.</template>
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="videoDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :disabled="!videoId" @click="applyVideo">Insert</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Link dialog: replaces the browser-native window.prompt (banned in this project). -->
        <v-dialog v-model="linkDialog" max-width="440">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ linkExisting ? 'Edit link' : 'Insert link' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="linkDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="linkUrl" label="Link URL" density="compact" autofocus hide-details
                        placeholder="https://example.com" @keyup.enter="applyLink"></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-btn v-if="linkExisting" color="error" variant="text" @click="removeLink">Remove link</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn @click="linkDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :disabled="!linkUrl.trim()" @click="applyLink">Apply</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <editor-content :editor="editor" class="editor-surface" />

        <v-snackbar v-model="snackbar" color="error" :timeout="4000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onBeforeUnmount } from 'vue'
import { useEditor, EditorContent } from '@tiptap/vue-3'
import StarterKit from '@tiptap/starter-kit'
import Underline from '@tiptap/extension-underline'
import Link from '@tiptap/extension-link'
import Image from '@tiptap/extension-image'
import { EmailButton } from '@/components/tiptap/EmailButton'
import { VideoEmbed, parseYouTubeId } from '@/components/tiptap/VideoEmbed'

const props = defineProps<{
    modelValue: string
    /** Optional: when provided, shows an image-insert toolbar button that uploads the
     *  chosen file and inserts the returned URL inline. Omit to keep the editor image-free. */
    uploadImage?: (file: File) => Promise<string>
    /** Optional: enable the email call-to-action button block (campaigns and automations). */
    emailButtons?: boolean
}>()
const emit = defineEmits<{ (e: 'update:modelValue', value: string): void }>()

const imageFileInput = ref<HTMLInputElement | null>(null)
const uploadingImage = ref(false)
const snackbar = ref(false)
const linkDialog = ref(false)
const linkUrl = ref('')
const linkExisting = ref(false)
const snackbarText = ref('')
const buttonDialog = ref(false)
const buttonLabel = ref('')
const buttonUrl = ref('')
const buttonExisting = ref(false)
const videoDialog = ref(false)
const videoInput = ref('')
const videoId = computed(() => parseYouTubeId(videoInput.value))

const editor = useEditor({
    content: props.modelValue,
    extensions: [
        StarterKit,
        Underline,
        Link.configure({ openOnClick: false, autolink: true }),
        Image,
        VideoEmbed,
        ...(props.emailButtons ? [EmailButton] : []),
    ],
    onUpdate: ({ editor }) => {
        emit('update:modelValue', editor.getHTML())
    },
})

watch(() => props.modelValue, (incoming) => {
    const current = editor.value?.getHTML()
    if (editor.value && incoming !== current) {
        editor.value.commands.setContent(incoming || '', { emitUpdate: false })
    }
})

onBeforeUnmount(() => {
    editor.value?.destroy()
})

function openVideoDialog() {
    videoInput.value = ''
    videoDialog.value = true
}
function applyVideo() {
    if (!editor.value || !videoId.value) return
    editor.value.chain().focus().insertVideoEmbed({ videoId: videoId.value }).run()
    videoDialog.value = false
}

function openButtonDialog() {
    if (!editor.value) return
    const existing = editor.value.isActive('emailButton') ? editor.value.getAttributes('emailButton') : null
    buttonExisting.value = !!existing
    buttonLabel.value = existing?.label ?? ''
    buttonUrl.value = existing?.href ?? 'https://'
    buttonDialog.value = true
}
function applyButton() {
    if (!editor.value) return
    const attrs = { label: buttonLabel.value.trim(), href: buttonUrl.value.trim() }
    if (!attrs.label || !attrs.href) return
    if (buttonExisting.value) editor.value.chain().focus().updateEmailButton(attrs).run()
    else editor.value.chain().focus().insertEmailButton(attrs).run()
    buttonDialog.value = false
}
function removeButton() {
    editor.value?.chain().focus().deleteSelection().run()
    buttonDialog.value = false
}

function toggleLink() {
    if (!editor.value) return
    const existing = editor.value.getAttributes('link').href as string | undefined
    linkExisting.value = !!existing
    linkUrl.value = existing ?? 'https://'
    linkDialog.value = true
}
function applyLink() {
    const url = linkUrl.value.trim()
    if (!editor.value || !url) return
    editor.value.chain().focus().extendMarkRange('link').setLink({ href: url }).run()
    linkDialog.value = false
}
function removeLink() {
    editor.value?.chain().focus().extendMarkRange('link').unsetLink().run()
    linkDialog.value = false
}

async function onImageFileChange(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0]
    if (!file || !props.uploadImage || !editor.value) return
    uploadingImage.value = true
    try {
        const url = await props.uploadImage(file)
        editor.value.chain().focus().setImage({ src: url }).run()
    } catch (err: any) {
        snackbarText.value = err?.response?.data?.error || 'Image upload failed. Try again.'
        snackbar.value = true
    } finally {
        uploadingImage.value = false
        if (imageFileInput.value) imageFileInput.value.value = ''
    }
}
</script>

<style scoped>
.rich-text-editor {
    border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
    border-radius: 4px;
    background: rgb(var(--v-theme-surface));
    color: rgb(var(--v-theme-on-surface));
}
.toolbar {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
    padding: 6px 8px;
    border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
    background: rgb(var(--v-theme-surface-variant), 0.06);
    color: rgb(var(--v-theme-on-surface));
}
/* The text-variant buttons inherit color from the theme; on dark backgrounds they
   render white-on-white otherwise. Force an on-surface color so they're always visible. */
.toolbar :deep(.v-btn) {
    color: rgb(var(--v-theme-on-surface));
}
.editor-surface {
    padding: 12px 14px;
    min-height: 200px;
    max-height: 500px;
    overflow-y: auto;
    color: rgb(var(--v-theme-on-surface));
}
</style>

<style>
/* Global styles for tiptap content — scoped styles can't reach prosemirror nodes. */
.rich-text-editor .ProseMirror {
    outline: none;
    min-height: 180px;
}
.rich-text-editor .ProseMirror p { margin: 0 0 0.6em 0; }
.rich-text-editor .ProseMirror h1 { font-size: 1.6em; margin: 0.4em 0 0.3em; }
.rich-text-editor .ProseMirror h2 { font-size: 1.35em; margin: 0.4em 0 0.3em; }
.rich-text-editor .ProseMirror h3 { font-size: 1.15em; margin: 0.4em 0 0.3em; }
.rich-text-editor .ProseMirror ul,
.rich-text-editor .ProseMirror ol { padding-left: 1.4em; margin: 0 0 0.6em; }
.rich-text-editor .ProseMirror blockquote {
    border-left: 3px solid rgba(var(--v-border-color), var(--v-border-opacity));
    margin: 0 0 0.6em;
    padding-left: 0.8em;
    opacity: 0.85;
}
.rich-text-editor .ProseMirror a.rp-video {
    display: block;
    position: relative;
    width: min(100%, 480px);
    margin: 12px 0;
    border-radius: 6px;
    overflow: hidden;
    background: #000;
}
.rich-text-editor .ProseMirror a.rp-video img {
    display: block;
    width: 100%;
    height: auto;
    opacity: 0.9;
}
.rich-text-editor .ProseMirror a.rp-video::after {
    content: '\25B6';
    position: absolute;
    left: 50%;
    top: 50%;
    transform: translate(-50%, -50%);
    width: 56px;
    height: 56px;
    border-radius: 50%;
    background: rgba(220, 38, 38, 0.92);
    color: #fff;
    font-size: 22px;
    line-height: 56px;
    text-align: center;
    padding-left: 4px;
    box-sizing: border-box;
}
.rich-text-editor .ProseMirror a.rp-video.ProseMirror-selectednode {
    outline: 2px solid rgb(var(--v-theme-primary));
}
.rich-text-editor .ProseMirror hr {
    border: none;
    border-top: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
    margin: 0.8em 0;
}
.rich-text-editor .ProseMirror a {
    color: rgb(var(--v-theme-primary));
    text-decoration: underline;
}
.rich-text-editor .ProseMirror img {
    max-width: 100%;
    height: auto;
    border-radius: 4px;
    margin: 0.4em 0;
}
</style>
