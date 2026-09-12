<script setup lang="ts">
defineProps<{ rank: 'First' | 'Second' | 'Third'; label: string; highlighted?: boolean }>()
</script>

<template>
  <span data-slot="blood-mark" class="blood-mark" :data-rank="rank" :data-highlighted="highlighted || undefined" role="img" tabindex="0" :aria-label="label">
    <span data-slot="blood-mark-glyph" aria-hidden="true" />
  </span>
</template>
