<script setup lang="ts">
defineProps<{
  text?: string
}>()
</script>

<template>
  <span class="group relative inline-flex">
    <slot />
    <span
      v-if="text"
      class="pointer-events-none absolute bottom-full left-1/2 z-50 mb-2 hidden -translate-x-1/2 whitespace-nowrap rounded-md border bg-popover px-2 py-1 text-xs text-popover-foreground shadow-md group-hover:block group-focus-within:block"
    >
      {{ text }}
    </span>
  </span>
</template>
