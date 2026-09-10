<script setup lang="ts">
import ScrollSurface from './ScrollSurface.vue'
defineProps<{ axis?: 'x' | 'y' | 'both' }>()
</script>

<template>
  <ScrollSurface :axis="axis" data-slot="scroll-area"><slot /></ScrollSurface>
</template>
