<template>
    <!-- Chromeless rental-booking widget: framed on a track's own website via embed.js.
         Browse rentable gear anonymously, pick a window, sign in inline at checkout. -->
    <div class="embed-rentals">
        <Rentals />
    </div>
</template>

<script setup lang="ts">
import Rentals from '@/views/Rentals.vue'
</script>
