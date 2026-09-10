<script setup lang="ts">
defineProps<{ label: string }>()
</script>

<template>
  <aside data-slot="floating-sidebar" :aria-label="label"><slot /></aside>
</template>
