<template>
    <div class="rich-text-view" v-html="sanitized"></div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import DOMPurify from 'dompurify'

const props = defineProps<{ html: string }>()

const sanitized = computed(() => {
    if (!props.html) return ''
    return DOMPurify.sanitize(props.html, {
        ALLOWED_TAGS: [
            'p', 'br', 'strong', 'em', 'u', 's',
            'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
            'ul', 'ol', 'li', 'blockquote', 'code', 'pre', 'hr', 'a', 'img',
        ],
        ALLOWED_ATTR: ['href', 'target', 'rel', 'src', 'alt', 'title'],
        ALLOW_DATA_ATTR: false,
    })
})
</script>

<style scoped>
.rich-text-view :deep(p) { margin: 0 0 0.6em 0; }
.rich-text-view :deep(h1) { font-size: 1.6em; margin: 0.4em 0 0.3em; }
.rich-text-view :deep(h2) { font-size: 1.35em; margin: 0.4em 0 0.3em; }
.rich-text-view :deep(h3) { font-size: 1.15em; margin: 0.4em 0 0.3em; }
.rich-text-view :deep(ul),
.rich-text-view :deep(ol) { padding-left: 1.4em; margin: 0 0 0.6em; }
.rich-text-view :deep(blockquote) {
    border-left: 3px solid rgba(0, 0, 0, 0.2);
    margin: 0 0 0.6em;
    padding-left: 0.8em;
    color: rgba(0, 0, 0, 0.7);
}
.rich-text-view :deep(a) {
    color: rgb(var(--v-theme-primary));
    text-decoration: underline;
}
.rich-text-view :deep(img) {
    max-width: 100%;
    height: auto;
    border-radius: 4px;
    margin: 0.4em 0;
}
</style>
