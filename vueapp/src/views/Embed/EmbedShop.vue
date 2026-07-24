<template>
    <!-- Chromeless bike-shop storefront widget. The page already browses anonymously
         and enforces sign-in at checkout server-side, so the wrapper is thin. -->
    <div class="embed-shop">
        <Shop />
    </div>
</template>

<script setup lang="ts">
import Shop from '@/views/Shop.vue'
</script>
