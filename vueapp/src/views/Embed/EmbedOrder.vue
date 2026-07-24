<template>
    <!-- Chromeless F&B ordering widget: framed on a track's own website via embed.js.
         Reuses the full rider Order Food page (public menu browse + cart; signing in
         happens inside the iframe at checkout, then payment completes inline). -->
    <div class="embed-order">
        <OrderFood />
    </div>
</template>

<script setup lang="ts">
import OrderFood from '@/views/OrderFood.vue'
</script>

<style scoped>
/* The page's own container padding is enough; just keep the widget flush. */
.embed-order :deep(.of-container) {
    max-width: none;
    padding-top: 12px;
}
</style>
