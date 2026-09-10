<script setup lang="ts">
import { Check, Info, TriangleAlert, X } from '@lucide/vue'
defineProps<{ tone?: 'success' | 'info' | 'warning' | 'error' }>()
</script>

<template>
  <span class="notice-icon" :data-tone="tone" aria-hidden="true">
    <span class="notice-icon-frame" />
    <Check v-if="tone === 'success'" class="notice-icon-symbol" />
    <X v-else-if="tone === 'error'" class="notice-icon-symbol" />
    <TriangleAlert v-else-if="tone === 'warning'" class="notice-icon-symbol" />
    <Info v-else class="notice-icon-symbol" />
  </span>
</template>
