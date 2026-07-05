<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'

const props = withDefaults(defineProps<{
  variant?: 'default' | 'destructive' | 'warning' | 'success' | 'info'
  class?: HTMLAttributes['class']
}>(), {
  variant: 'default',
})
</script>

<template>
  <div
    data-slot="alert"
    :class="cn(
      'relative w-full rounded-md border px-4 py-3 text-sm',
      variant === 'destructive' && 'noctf-status-danger',
      variant === 'warning' && 'noctf-status-warning',
      variant === 'success' && 'noctf-status-success',
      variant === 'info' && 'noctf-status-info',
      variant === 'default' && 'border-border bg-muted/40 text-foreground',
      props.class,
    )"
  >
    <slot />
  </div>
</template>
