<script setup lang="ts">
import { computed } from 'vue'
const props = defineProps<{ original: string; updated: string; originalLabel: string; updatedLabel: string }>()
const lines = computed(() => {
  const before = props.original.split('\n'), after = props.updated.split('\n')
  return Array.from({ length: Math.max(before.length, after.length) }, (_, index) => ({ index,
    before: before[index], after: after[index], changed: before[index] !== after[index] }))
})
</script>
<template>
  <section data-slot="markdown-diff" class="min-w-0 space-y-3">
    <div class="grid grid-cols-2 gap-3 text-sm font-semibold"><span>{{ originalLabel }}</span><span>{{ updatedLabel }}</span></div>
    <ScrollSurface axis="both" class="max-h-[55dvh]"><div v-for="line in lines" :key="line.index" class="grid grid-cols-2 gap-3 py-1 text-sm" :class="line.changed ? 'bg-warning/10' : ''"><pre class="whitespace-pre-wrap break-words"><span v-if="line.changed" class="text-destructive">− </span>{{ line.before ?? '' }}</pre><pre class="whitespace-pre-wrap break-words"><span v-if="line.changed" class="text-success">+ </span>{{ line.after ?? '' }}</pre></div></ScrollSurface>
  </section>
</template>
