<template>
    <v-overlay v-model="modelValue" class="align-center justify-center">
        <v-progress-circular indeterminate size="64" color="primary"></v-progress-circular>
    </v-overlay>
</template>

<script setup lang="ts">
const modelValue = defineModel<boolean>();
</script>
