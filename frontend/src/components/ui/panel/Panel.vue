<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { cn } from '@/lib/utils'

const props = withDefaults(defineProps<{
  class?: HTMLAttributes['class']
  variant?: 'default' | 'dark'
  border?: 'default' | 'transparent'
}>(), {
  variant: 'default',
  border: 'transparent',
})

const borderClass = computed(() => {
  if (props.border === 'transparent')
    return 'border-transparent'
  return props.variant === 'dark' ? 'border-[#5f5f5f]' : 'border-[#bdbdbd]'
})
</script>

<template>
  <div class="panel-shell relative z-[3]">
    <div
      class="panel-base"
      :class="props.variant === 'dark' ? 'panel-base-dark' : 'panel-base-default'"
      aria-hidden="true"
    />
    <div
      data-slot="panel"
      :class="
        cn(
          'panel-face relative z-[2] flex flex-col border-[2px]',
          borderClass,
          props.variant === 'dark'
            ? 'bg-[linear-gradient(180deg,#4b4b4b_0%,#383838_100%)] text-zinc-100 shadow-panel-dark'
            : 'bg-[linear-gradient(180deg,#e4e4e4_0%,#dadada_100%)] text-black shadow-panel',
          props.class,
        )
      "
    >
      <slot />
    </div>
  </div>
</template>

<style scoped>
.panel-shell {
  position: relative;
  isolation: isolate;
}

.panel-base {
  position: absolute;
  inset: 10px 0 0 10px;
  z-index: 1;
  border: 2px solid;
}

.panel-base-default {
  border-color: #d4d4d4;
  background: #f5f5f5;
  box-shadow: 2px 2px 0 #e0e0e0;
}

.panel-base-dark {
  border-color: #8b8b8b;
  background: #d7d7d7;
  box-shadow: 2px 2px 0 #b6b6b6;
}

.panel-face {
  min-height: inherit;
}
</style>
