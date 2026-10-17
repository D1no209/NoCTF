<script setup lang="ts">
import { computed } from 'vue'
import { diffLines } from '~/lib/line-diff'

const props = defineProps<{ original: string; updated: string; originalLabel: string; updatedLabel: string }>()
const rows = computed(() => diffLines(props.original, props.updated))
</script>
<template>
  <section data-slot="markdown-diff" class="min-w-0 space-y-3">
    <div class="grid grid-cols-2 gap-3 text-sm font-semibold"><span>{{ originalLabel }}</span><span>{{ updatedLabel }}</span></div>
    <ScrollSurface axis="both" class="max-h-[55dvh]"><div v-for="(line, index) in rows" :key="index" class="grid grid-cols-2 gap-3 py-1 text-sm" :class="line.changed ? 'bg-warning/10' : ''"><pre class="whitespace-pre-wrap break-words"><span v-if="line.changed && line.before !== null" class="text-destructive">− </span>{{ line.before ?? '' }}</pre><pre class="whitespace-pre-wrap break-words"><span v-if="line.changed && line.after !== null" class="text-success">+ </span>{{ line.after ?? '' }}</pre></div></ScrollSurface>
  </section>
</template>
