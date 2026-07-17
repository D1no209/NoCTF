<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'

const props = withDefaults(defineProps<{
  tone?: 'default' | 'primary' | 'signal' | 'warning'
  class?: HTMLAttributes['class']
}>(), {
  tone: 'default',
})
</script>

<template>
  <section
    :class="cn('command-panel', `command-panel--${props.tone}`, props.class)"
  >
    <slot />
  </section>
</template>

<style scoped>
.command-panel {
  position: relative;
  min-width: 0;
  border: 1px solid var(--v2-line);
  background: var(--v2-surface);
  box-shadow: 0 10px 24px var(--v2-shadow);
}

.command-panel::before {
  position: absolute;
  top: -1px;
  left: -1px;
  width: 42px;
  height: 2px;
  content: "";
  background: var(--v2-line-bright);
}

.command-panel--signal::before {
  background: var(--v2-cyan);
}

.command-panel--warning::before {
  background: var(--v2-warning);
}
</style>
