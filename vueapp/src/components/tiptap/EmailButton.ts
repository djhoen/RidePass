import { Node, mergeAttributes } from '@tiptap/core'

/**
 * A call-to-action button for emails. In the editor it is an atom block (click to edit, not a
 * text run) rendered as an anchor with class rp-button; at send time EmailHtml turns that anchor
 * into a table-based button in the track's primary color, which is the only kind of button that
 * survives Outlook. Kept as a plain anchor in the stored HTML so the blog/page sanitizer and the
 * admin preview both show something sensible without knowing about email.
 */
export interface EmailButtonAttrs {
    href: string
    label: string
}

declare module '@tiptap/core' {
    interface Commands<ReturnType> {
        emailButton: {
            insertEmailButton: (attrs: EmailButtonAttrs) => ReturnType
            updateEmailButton: (attrs: EmailButtonAttrs) => ReturnType
        }
    }
}

export const EmailButton = Node.create({
    name: 'emailButton',
    group: 'block',
    atom: true,
    draggable: true,
    selectable: true,

    addAttributes() {
        return {
            href: { default: '#' },
            label: { default: 'Learn more' },
        }
    },

    parseHTML() {
        return [{
            tag: 'a.rp-button',
            getAttrs: (el) => {
                const a = el as HTMLAnchorElement
                return { href: a.getAttribute('href') ?? '#', label: a.textContent ?? 'Learn more' }
            },
        }]
    },

    renderHTML({ HTMLAttributes }) {
        const { href, label, ...rest } = HTMLAttributes as { href: string; label: string }
        return ['a', mergeAttributes(rest, { class: 'rp-button', href, target: '_blank', rel: 'noopener' }), label]
    },

    addCommands() {
        return {
            insertEmailButton: (attrs) => ({ commands }) =>
                commands.insertContent({ type: this.name, attrs }),
            updateEmailButton: (attrs) => ({ commands }) =>
                commands.updateAttributes(this.name, attrs),
        }
    },
})
