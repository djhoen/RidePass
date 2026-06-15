<template>
    <v-card class="mb-6">
        <div class="d-flex align-center pa-3 ga-2">
            <v-btn icon="mdi-chevron-left" variant="text" size="small" @click="navigate(-1)"></v-btn>
            <div class="text-h6" style="min-width: 190px; text-align: center">{{ monthLabel }}</div>
            <v-btn icon="mdi-chevron-right" variant="text" size="small" @click="navigate(1)"></v-btn>
            <v-spacer></v-spacer>
            <v-btn variant="tonal" size="small" @click="goToday">Today</v-btn>
        </div>

        <div class="cal-weekdays">
            <div v-for="d in weekdayLabels" :key="d" class="cal-weekday">{{ d }}</div>
        </div>

        <div class="cal-grid">
            <div v-for="day in days" :key="day.key" class="cal-cell"
                :class="{ 'cal-cell--muted': !day.inMonth, 'cal-cell--today': day.isToday }">
                <div class="cal-daynum">{{ day.dayNum }}</div>
                <div class="cal-events">
                    <button v-for="ev in day.events" :key="ev.id" type="button" class="cal-event"
                        :style="{ backgroundColor: ev.eventTypeColor || '#1976d2' }"
                        :title="`${timeLabel(ev)} ${ev.title}`" @click="$emit('select', ev)">
                        <span class="cal-event-time">{{ timeLabel(ev) }}</span> {{ ev.title }}
                    </button>
                </div>
            </div>
        </div>
    </v-card>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import dayjs from 'dayjs'
import type { EventDto } from '@/services/EventService'

const props = defineProps<{ monthStart: string; events: EventDto[]; timezone: string }>()
const emit = defineEmits<{ (e: 'update:monthStart', v: string): void; (e: 'select', ev: EventDto): void }>()

const tz = computed(() => props.timezone || 'UTC')
const monthRef = computed(() => dayjs(props.monthStart))
const monthLabel = computed(() => monthRef.value.format('MMMM YYYY'))
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

// A fixed 6-row grid (42 cells) starting on the Sunday on/before the 1st.
const days = computed(() => {
    const first = monthRef.value.startOf('month')
    const gridStart = first.startOf('week')
    const todayKey = dayjs().tz(tz.value).format('YYYY-MM-DD')
    const out: Array<{ key: string; dayNum: number; inMonth: boolean; isToday: boolean; events: EventDto[] }> = []
    for (let i = 0; i < 42; i++) {
        const d = gridStart.add(i, 'day')
        const key = d.format('YYYY-MM-DD')
        out.push({
            key,
            dayNum: d.date(),
            inMonth: d.month() === first.month(),
            isToday: key === todayKey,
            events: eventsByDate.value[key] ?? [],
        })
    }
    return out
})

function navigate(delta: number) {
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
    min-height: 104px;
    border: 1px solid rgba(0, 0, 0, 0.06);
    padding: 4px;
    overflow: hidden;
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
