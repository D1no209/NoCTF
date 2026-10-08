<script setup lang="ts">
defineProps<{ label: string; contained?: boolean }>()
</script>

<template>
  <aside data-slot="floating-sidebar" :data-contained="contained || undefined" :aria-label="label"><slot /></aside>
</template>

<style>
[data-slot='floating-sidebar'][data-contained='true'] {
  position: relative;
  inset: auto;
  width: auto;
  min-height: 0;
  height: min(24rem, 48dvh);
  z-index: auto;
}
@media (min-width: 1024px) {
  [data-slot='floating-sidebar'][data-contained='true'] { height: 100%; }
}
</style>
