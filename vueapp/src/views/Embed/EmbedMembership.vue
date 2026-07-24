<template>
    <!-- Chromeless membership signup widget. The status endpoint is anonymous-friendly
         (price card renders before sign-in); buying bounces through login in-iframe. -->
    <div class="embed-membership">
        <Membership />
    </div>
</template>

<script setup lang="ts">
import Membership from '@/views/User/Membership.vue'
</script>
