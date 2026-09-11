<template>
    <div class="rich-text-view" v-html="sanitized"></div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import DOMPurify from 'dompurify'

const props = defineProps<{ html: string }>()

const sanitized = computed(() => {
    if (!props.html) return ''
    const clean = DOMPurify.sanitize(props.html, {
        ALLOWED_TAGS: [
            'p', 'br', 'strong', 'em', 'u', 's',
            'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
            'ul', 'ol', 'li', 'blockquote', 'code', 'pre', 'hr', 'a', 'img',
        ],
        ALLOWED_ATTR: ['href', 'target', 'rel', 'src', 'alt', 'title', 'class', 'data-video-id'],
        ALLOW_DATA_ATTR: false,
    })
    return withVideoPlayers(clean)
})

/**
 * The editor stores a YouTube video as a linked thumbnail (what an email can show). On a web
 * page it can play in place, so after sanitizing, each rp-video anchor becomes a player. The
 * iframe is built here from a validated 11-character id, never from stored markup.
 */
function withVideoPlayers(html: string): string {
    if (!html.includes('rp-video')) return html
    const doc = new DOMParser().parseFromString(html, 'text/html')
    doc.querySelectorAll('a.rp-video').forEach(a => {
        const id = a.getAttribute('data-video-id') ?? ''
        if (!/^[A-Za-z0-9_-]{11}$/.test(id)) return
        const wrap = doc.createElement('div')
        wrap.className = 'rp-video-frame'
        const frame = doc.createElement('iframe')
        frame.setAttribute('src', `https://www.youtube-nocookie.com/embed/${id}`)
        frame.setAttribute('title', a.querySelector('img')?.getAttribute('alt') || 'Video')
        frame.setAttribute('loading', 'lazy')
        frame.setAttribute('allow', 'accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture')
        frame.setAttribute('allowfullscreen', '')
        frame.setAttribute('referrerpolicy', 'strict-origin-when-cross-origin')
        wrap.appendChild(frame)
        a.replaceWith(wrap)
    })
    return doc.body.innerHTML
}
</script>

<style scoped>
.rich-text-view :deep(.rp-video-frame) {
    position: relative;
    width: 100%;
    max-width: 720px;
    aspect-ratio: 16 / 9;
    margin: 12px 0;
    border-radius: 6px;
    overflow: hidden;
    background: #000;
}
.rich-text-view :deep(.rp-video-frame iframe) {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    border: 0;
}
.rich-text-view :deep(a.rp-button) {
    display: inline-block;
    padding: 10px 20px;
    margin: 12px 0;
    border-radius: 6px;
    background: rgb(var(--v-theme-primary));
    color: #fff !important;
    font-weight: 600;
    text-decoration: none;
}
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
