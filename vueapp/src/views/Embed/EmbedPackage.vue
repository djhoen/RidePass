<template>
    <!-- Chromeless package landing/booking widget (the :id route param feeds the
         PackageLanding's slugOrId; the embed route resolves slug or id server-side). -->
    <div class="embed-package"><PackageLanding /></div>
</template>

<script setup lang="ts">
import PackageLanding from '@/views/PackageLanding.vue'
</script>
