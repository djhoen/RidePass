<template>
    <!-- A rider's lesson self-assessment (Script0318), read-only, for admin screens. -->
    <div class="sa-answers">
        <div class="text-caption text-medium-emphasis mb-2">
            {{ assessment.surveyTitle }} · answered {{ when }}
        </div>
        <div v-if="assessment.answers.length === 0" class="text-body-2 text-medium-emphasis">
            No answers given (all questions were optional).
        </div>
        <div v-for="(a, i) in assessment.answers" :key="i" class="mb-2">
            <div class="text-body-2 font-weight-medium">{{ a.prompt }}</div>
            <div class="text-body-2" style="white-space: pre-wrap">{{ a.answer || '—' }}</div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import dayjs from 'dayjs'
import type { TicketAssessment } from '@/services/LessonAssessmentService'

const props = defineProps<{ assessment: TicketAssessment }>()
const when = computed(() => dayjs(props.assessment.submittedAtUtc).format('MMM D, YYYY h:mm A'))
</script>
