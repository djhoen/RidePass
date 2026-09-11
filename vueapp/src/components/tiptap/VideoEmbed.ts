import { Node, mergeAttributes, nodePasteRule } from '@tiptap/core'

/**
 * A YouTube video in editor content. Email clients do not render iframes, so what is stored
 * is what works everywhere: an anchor (class rp-video, data-video-id) around the video's
 * thumbnail, linking to the watch page. In an email that is exactly what goes out, with a play
 * badge drawn by the editor for the author's benefit only; on a web page RichTextView swaps the
 * anchor for a real player. Pasting an embed snippet or a YouTube link creates one of these.
 */
export interface VideoEmbedAttrs {
    videoId: string
    title?: string | null
}

const ID = '([A-Za-z0-9_-]{11})'
/** Watch, short, share, and embed URLs, in text or inside a pasted iframe snippet. */
const URL_RE = new RegExp(
    `https?:\\/\\/(?:www\\.|m\\.)?(?:youtube\\.com\\/(?:watch\\?(?:[^\\s"'<]*&)?v=|shorts\\/|embed\\/|live\\/)|youtube-nocookie\\.com\\/embed\\/|youtu\\.be\\/)${ID}`)
const IFRAME_PASTE_RE = new RegExp(`<iframe[^>]*src=["']${URL_RE.source}[^"']*["'][^>]*>(?:\\s*<\\/iframe>)?`, 'g')
const URL_PASTE_RE = new RegExp(`(?:^|\\s)(${URL_RE.source})(?:[^\\s<]*)`, 'g')

/** The 11-character id from any YouTube URL or embed snippet, or null. */
export function parseYouTubeId(input: string): string | null {
    const m = URL_RE.exec((input ?? '').trim())
    return m ? m[1] : null
}

export function youTubeThumbnail(videoId: string): string {
    return `https://img.youtube.com/vi/${videoId}/hqdefault.jpg`
}

export function youTubeWatchUrl(videoId: string): string {
    return `https://www.youtube.com/watch?v=${videoId}`
}

declare module '@tiptap/core' {
    interface Commands<ReturnType> {
        videoEmbed: {
            insertVideoEmbed: (attrs: VideoEmbedAttrs) => ReturnType
        }
    }
}

export const VideoEmbed = Node.create({
    name: 'videoEmbed',
    group: 'block',
    atom: true,
    draggable: true,
    selectable: true,

    addAttributes() {
        return {
            videoId: { default: '' },
            title: { default: null },
        }
    },

    parseHTML() {
        return [
            {
                // Above the link mark and the image node, which would otherwise claim these tags.
                tag: 'a.rp-video',
                priority: 100,
                getAttrs: (el) => {
                    const a = el as HTMLAnchorElement
                    const id = a.getAttribute('data-video-id') || parseYouTubeId(a.getAttribute('href') ?? '')
                    if (!id) return false
                    const img = a.querySelector('img')
                    return { videoId: id, title: img?.getAttribute('alt') || null }
                },
            },
            {
                // Stored content from before this node, or HTML pasted with the embed intact.
                tag: 'iframe',
                priority: 100,
                getAttrs: (el) => {
                    const id = parseYouTubeId((el as HTMLIFrameElement).getAttribute('src') ?? '')
                    return id ? { videoId: id, title: (el as HTMLIFrameElement).getAttribute('title') || null } : false
                },
            },
        ]
    },

    renderHTML({ node }) {
        const { videoId, title } = node.attrs as VideoEmbedAttrs
        return [
            'a',
            mergeAttributes({
                class: 'rp-video', href: youTubeWatchUrl(videoId), target: '_blank', rel: 'noopener',
                'data-video-id': videoId,
            }),
            ['img', { src: youTubeThumbnail(videoId), alt: title || 'Watch the video' }],
        ]
    },

    addCommands() {
        return {
            insertVideoEmbed: (attrs) => ({ commands }) =>
                commands.insertContent({ type: this.name, attrs }),
        }
    },

    addPasteRules() {
        // The embed snippet YouTube's Share > Embed box gives, or a plain link, pasted as text.
        return [
            nodePasteRule({
                find: IFRAME_PASTE_RE,
                type: this.type,
                getAttributes: (match) => ({ videoId: match[1] }),
            }),
            nodePasteRule({
                find: URL_PASTE_RE,
                type: this.type,
                getAttributes: (match) => ({ videoId: match[2] }),
            }),
        ]
    },
})
