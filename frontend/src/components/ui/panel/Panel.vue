<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'

const props = withDefaults(defineProps<{
  class?: HTMLAttributes['class']
  variant?: 'default' | 'dark'
  border?: 'default' | 'transparent'
}>(), {
  variant: 'default',
  border: 'transparent',
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
          props.border === 'transparent' ? 'border-transparent' : '',
          props.variant === 'dark' ? 'panel-face-dark' : 'panel-face-default',
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
  border-color: var(--panel-base-border);
  background: var(--panel-base-surface);
  box-shadow: 2px 2px 0 var(--panel-base-shadow);
}

.panel-base-dark {
  border-color: var(--panel-dark-base-border);
  background: var(--panel-dark-base-surface);
  box-shadow: 2px 2px 0 var(--panel-dark-base-shadow);
}

.panel-face {
  min-height: inherit;
}

.panel-face-default {
  border-color: var(--panel-border);
  background: linear-gradient(180deg, var(--panel-face-start) 0%, var(--panel-face-end) 100%);
  color: var(--foreground);
  box-shadow: var(--panel-shadow);
}

.panel-face-dark {
  border-color: var(--panel-dark-border);
  background: linear-gradient(180deg, var(--panel-dark-face-start) 0%, var(--panel-dark-face-end) 100%);
  color: var(--primary-foreground);
  box-shadow: var(--panel-shadow-dark);
}
</style>
