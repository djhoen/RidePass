<template>
    <v-card class="mb-6">
        <div class="d-flex align-center pa-3 ga-2">
            <v-btn icon="mdi-chevron-left" variant="text" size="small" :disabled="!canGoPrev" @click="navigate(-1)"></v-btn>
            <div class="text-h6" style="min-width: 190px; text-align: center">{{ monthLabel }}</div>
            <v-btn icon="mdi-chevron-right" variant="text" size="small" :disabled="!canGoNext" @click="navigate(1)"></v-btn>
            <v-spacer></v-spacer>
            <v-btn variant="tonal" size="small" @click="goToday">Today</v-btn>
        </div>

        <div class="cal-weekdays">
            <div v-for="d in weekdayLabels" :key="d" class="cal-weekday">{{ d }}</div>
        </div>

        <div class="cal-grid">
            <div v-for="day in days" :key="day.key" class="cal-cell"
                :class="{ 'cal-cell--muted': !day.inMonth, 'cal-cell--today': day.isToday, 'cal-cell--blackout': day.blackoutReasons.length > 0 }">
                <div class="cal-daynum">{{ day.dayNum }}</div>
                <v-tooltip v-if="day.blackoutReasons.length" :text="day.blackoutReasons.join(', ')" location="top">
                    <template #activator="{ props }">
                        <div v-bind="props" class="cal-blackout">
                            <v-icon size="11" class="mr-1">mdi-calendar-remove</v-icon>{{ day.blackoutReasons[0] }}
                        </div>
                    </template>
                </v-tooltip>
                <div class="cal-events">
                    <v-tooltip v-for="ev in day.events" :key="ev.id" :text="`${timeLabel(ev)} ${ev.title}`" location="top">
                        <template #activator="{ props }">
                            <button v-bind="props" type="button" class="cal-event"
                                :style="{ backgroundColor: ev.eventTypeColor || '#1976d2' }" @click="$emit('select', ev)">
                                <span class="cal-event-time">{{ timeLabel(ev) }}</span> {{ ev.title }}
                            </button>
                        </template>
                    </v-tooltip>
                </div>
            </div>
        </div>
    </v-card>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import dayjs from 'dayjs'
import type { EventDto } from '@/services/EventService'
import type { BlackoutDto } from '@/services/BlackoutService'

// minMonth/maxMonth (YYYY-MM-DD month starts) optionally bound navigation. When omitted,
// paging is unbounded (consumers that refetch per month). When set (e.g. the embed widget
// fetches a fixed window), the chevrons clamp to the loaded range so the visitor can't page
// into an always-empty grid.
const props = defineProps<{ monthStart: string; events: EventDto[]; timezone: string; blackouts?: BlackoutDto[]; minMonth?: string; maxMonth?: string }>()
const emit = defineEmits<{ (e: 'update:monthStart', v: string): void; (e: 'select', ev: EventDto): void }>()

const tz = computed(() => props.timezone || 'UTC')
const monthRef = computed(() => dayjs(props.monthStart))
const monthLabel = computed(() => monthRef.value.format('MMMM YYYY'))
const canGoPrev = computed(() => !props.minMonth
    || monthRef.value.startOf('month').isAfter(dayjs(props.minMonth).startOf('month')))
const canGoNext = computed(() => !props.maxMonth
    || monthRef.value.startOf('month').isBefore(dayjs(props.maxMonth).startOf('month')))
const weekdayLabels = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

// Bucket events by their start date in the tenant's timezone.
const eventsByDate = computed(() => {
    const map: Record<string, EventDto[]> = {}
    for (const e of props.events) {
        const key = dayjs.utc(e.startsAtUtc).tz(tz.value).format('YYYY-MM-DD')
        ;(map[key] ??= []).push(e)
    }
    for (const k in map) map[k].sort((a, b) => a.startsAtUtc.localeCompare(b.startsAtUtc))
    return map
})

// Mark every day a blackout covers (in the tenant tz) with its reason(s). A blackout
// can span multiple days; we mark from its start day through its end day. Backing the
// end off by a second keeps a blackout that ends at the next midnight from bleeding into
// the following day.
const blackoutsByDate = computed(() => {
    const map: Record<string, string[]> = {}
    for (const b of props.blackouts ?? []) {
        const start = dayjs.utc(b.startsAtUtc).tz(tz.value).startOf('day')
        const endDay = dayjs.utc(b.endsAtUtc).tz(tz.value).subtract(1, 'second').startOf('day')
        if (endDay.isBefore(start, 'day')) { // bad/instant range: just mark the start day
            ;(map[start.format('YYYY-MM-DD')] ??= []).push(b.reason || 'Blackout')
            continue
        }
        let cur = start
        for (let guard = 0; !cur.isAfter(endDay, 'day') && guard < 400; guard++) {
            ;(map[cur.format('YYYY-MM-DD')] ??= []).push(b.reason || 'Blackout')
            cur = cur.add(1, 'day')
        }
    }
    return map
})

// A fixed 6-row grid (42 cells) starting on the Sunday on/before the 1st.
const days = computed(() => {
    const first = monthRef.value.startOf('month')
    const gridStart = first.startOf('week')
    const todayKey = dayjs().tz(tz.value).format('YYYY-MM-DD')
    const out: Array<{ key: string; dayNum: number; inMonth: boolean; isToday: boolean; events: EventDto[]; blackoutReasons: string[] }> = []
    for (let i = 0; i < 42; i++) {
        const d = gridStart.add(i, 'day')
        const key = d.format('YYYY-MM-DD')
        out.push({
            key,
            dayNum: d.date(),
            inMonth: d.month() === first.month(),
            isToday: key === todayKey,
            events: eventsByDate.value[key] ?? [],
            blackoutReasons: blackoutsByDate.value[key] ?? [],
        })
    }
    return out
})

function navigate(delta: number) {
    if (delta < 0 && !canGoPrev.value) return
    if (delta > 0 && !canGoNext.value) return
    emit('update:monthStart', monthRef.value.add(delta, 'month').startOf('month').format('YYYY-MM-DD'))
}
function goToday() {
    emit('update:monthStart', dayjs().startOf('month').format('YYYY-MM-DD'))
}
function timeLabel(ev: EventDto): string {
    return dayjs.utc(ev.startsAtUtc).tz(tz.value).format('h:mma')
}
</script>

<style scoped>
.cal-weekdays,
.cal-grid {
    display: grid;
    grid-template-columns: repeat(7, 1fr);
}

.cal-weekday {
    padding: 6px 8px;
    font-size: 12px;
    font-weight: 600;
    color: rgba(0, 0, 0, 0.6);
    border-top: 1px solid rgba(0, 0, 0, 0.08);
}

.cal-cell {
    /* min-height keeps empty weeks from collapsing, but cells must be allowed to GROW:
       overflow:hidden with a fixed height sliced the event entries on busy days (three
       or more events). The grid row stretches to its tallest cell, and the embed iframe
       auto-resizes to the taller widget, so growth is safe in both hosts. */
    min-height: 104px;
    border: 1px solid rgba(0, 0, 0, 0.06);
    padding: 4px;
}

.cal-cell--muted {
    background: rgba(0, 0, 0, 0.02);
}

.cal-cell--muted .cal-daynum {
    color: rgba(0, 0, 0, 0.35);
}

.cal-cell--today {
    background: rgba(25, 118, 210, 0.08);
}

/* Blacked-out days: subtle red wash + a small label so they read as "closed". */
.cal-cell--blackout {
    background: repeating-linear-gradient(
        45deg,
        rgba(211, 47, 47, 0.06),
        rgba(211, 47, 47, 0.06) 6px,
        rgba(211, 47, 47, 0.12) 6px,
        rgba(211, 47, 47, 0.12) 12px
    );
}

.cal-blackout {
    display: flex;
    align-items: center;
    font-size: 10px;
    font-weight: 600;
    color: #c62828;
    line-height: 1.3;
    margin-bottom: 2px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}

.cal-daynum {
    font-size: 12px;
    font-weight: 600;
    margin-bottom: 2px;
}

.cal-events {
    display: flex;
    flex-direction: column;
    gap: 2px;
}

.cal-event {
    display: block;
    width: 100%;
    text-align: left;
    font-size: 11px;
    line-height: 1.35;
    color: #fff;
    border: none;
    border-radius: 4px;
    padding: 1px 5px;
    cursor: pointer;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}

.cal-event-time {
    opacity: 0.85;
    font-variant-numeric: tabular-nums;
}
</style>
