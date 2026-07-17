<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'

const props = withDefaults(defineProps<{
  tone?: 'default' | 'primary' | 'signal' | 'warning'
  inset?: boolean
  class?: HTMLAttributes['class']
}>(), {
  tone: 'default',
  inset: false,
})
</script>

<template>
  <section
    :class="cn('command-panel', props.inset && 'command-panel--inset', props.class)"
    :data-tone="props.tone"
  >
    <slot />
  </section>
</template>

<style scoped>
.command-panel {
  position: relative;
  min-width: 0;
  border: 0;
  border-radius: 16px;
  background: var(--v2-surface);
  box-shadow: var(--v2-raised-lg);
}

.command-panel--inset {
  box-shadow: var(--v2-inset);
}
</style>
