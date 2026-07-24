<template>
    <!-- Chromeless gift card purchase widget. Anonymous visitors can fill the form;
         the buy step bounces through login inside the iframe (same-origin). -->
    <div class="embed-giftcard">
        <BuyGiftCard />
    </div>
</template>

<script setup lang="ts">
import BuyGiftCard from '@/views/BuyGiftCard.vue'
</script>
