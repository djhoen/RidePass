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
            <input v-if="uploadImage" ref="imageFileInput" type="file" accept="image/png,image/jpeg,image/webp"
                class="d-none" @change="onImageFileChange" />
        </div>

        <editor-content :editor="editor" class="editor-surface" />

        <v-snackbar v-model="snackbar" color="error" :timeout="4000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, watch, onBeforeUnmount } from 'vue'
import { useEditor, EditorContent } from '@tiptap/vue-3'
import StarterKit from '@tiptap/starter-kit'
import Underline from '@tiptap/extension-underline'
import Link from '@tiptap/extension-link'
import Image from '@tiptap/extension-image'

const props = defineProps<{
    modelValue: string
    /** Optional: when provided, shows an image-insert toolbar button that uploads the
     *  chosen file and inserts the returned URL inline. Omit to keep the editor image-free. */
    uploadImage?: (file: File) => Promise<string>
}>()
const emit = defineEmits<{ (e: 'update:modelValue', value: string): void }>()

const imageFileInput = ref<HTMLInputElement | null>(null)
const uploadingImage = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')

const editor = useEditor({
    content: props.modelValue,
    extensions: [
        StarterKit,
        Underline,
        Link.configure({ openOnClick: false, autolink: true }),
        Image,
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

function toggleLink() {
    if (!editor.value) return
    const existing = editor.value.getAttributes('link').href as string | undefined
    const url = window.prompt('Link URL', existing ?? 'https://')
    if (url === null) return
    if (url === '') {
        editor.value.chain().focus().extendMarkRange('link').unsetLink().run()
        return
    }
    editor.value.chain().focus().extendMarkRange('link').setLink({ href: url }).run()
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
