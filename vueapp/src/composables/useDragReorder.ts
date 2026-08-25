import { ref, watch, type Ref, type WatchSource } from 'vue'

// Shared drag-drop reorder logic used by every admin list with a sort_order
// column. The consumer supplies the canonical rows ref, an optional visibility
// filter, and a save callback that hits the bulk-reorder endpoint. We expose
// `visibleRows` (the mutable array vuedraggable binds to) and `onReorderEnd`
// (the @end handler that interleaves the new visible order back into rows,
// renumbers 10/20/30…, and persists).
//
// Why interleave: when a filter hides some rows, drag-drop should only shuffle
// the visible subset — hidden rows must hold their canonical slot, otherwise
// toggling the filter on later would reveal an unrelated reshuffle.

export interface DragReorderItem {
    id: string
    sortOrder: number
}

export interface UseDragReorderOpts<T extends DragReorderItem> {
    /** Canonical full list — kept as the source of truth. */
    rows: Ref<T[]>
    /** Optional visibility predicate. When omitted, every row is visible/draggable. */
    filter?: (row: T) => boolean
    /** Reactive sources the filter reads — pass them so resync runs when they change. */
    filterDeps?: WatchSource[]
    /** Bulk-reorder endpoint call. Receives every row's new sortOrder, including hidden ones. */
    save: (items: { id: string; sortOrder: number }[]) => Promise<unknown>
    /** Toast / log on successful save. Optional. */
    onSuccess?: () => void
    /** Recovery on failure — typically reloads from the server. Optional. */
    onError?: (err: unknown) => Promise<void> | void
}

export function useDragReorder<T extends DragReorderItem>(opts: UseDragReorderOpts<T>) {
    const visibleRows = ref<T[]>([]) as Ref<T[]>
    function sync() {
        visibleRows.value = opts.filter
            ? opts.rows.value.filter(opts.filter)
            : [...opts.rows.value]
    }
    // deep: true is load-bearing, not defensive. `rows` is a Ref<T[]>, and a shallow watch on one
    // fires only when `.value` is REPLACED, never when the array is mutated in place. A consumer
    // that does `rows.value.push(newRow)` would leave visibleRows stale forever: the row is in the
    // canonical list but renders nowhere and is invisible to anything reading visibleRows.
    //
    // That is not hypothetical. It is exactly how TicketTiersList's buffer mode adds a tier to a
    // not-yet-created event, and it made a new event unsaveable: the pass was added, the table
    // stayed empty, and EventDialog's "add at least one purchasable item" guard (which counts
    // getBuffered(), i.e. visibleRows) refused the save.
    watch([opts.rows, ...(opts.filterDeps ?? [])], sync, { immediate: true, deep: true })

    async function onReorderEnd(evt: { oldIndex?: number; newIndex?: number }) {
        // SortableJS fires @end even when nothing moved (a click without a drag).
        // Skip the round-trip and the success toast in that case.
        if (evt.oldIndex === evt.newIndex) return
        const visibleIds = new Set(visibleRows.value.map(r => r.id))
        let visibleIdx = 0
        const rebuilt = opts.rows.value.map(r =>
            visibleIds.has(r.id) ? visibleRows.value[visibleIdx++] : r)
        rebuilt.forEach((r, i) => { r.sortOrder = (i + 1) * 10 })
        opts.rows.value = rebuilt
        const items = rebuilt.map(r => ({ id: r.id, sortOrder: r.sortOrder }))
        try {
            await opts.save(items)
            opts.onSuccess?.()
        } catch (err) {
            if (opts.onError) await opts.onError(err)
        }
    }

    return { visibleRows, onReorderEnd }
}
