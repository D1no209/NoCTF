<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'

const props = withDefaults(defineProps<{
  variant?: 'default' | 'destructive' | 'warning' | 'success'
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
      variant === 'destructive' && 'border-destructive/35 bg-destructive/10 text-destructive',
      variant === 'warning' && 'border-yellow-500/35 bg-yellow-500/10 text-yellow-700 dark:text-yellow-300',
      variant === 'success' && 'border-green-500/35 bg-green-500/10 text-green-700 dark:text-green-300',
      variant === 'default' && 'border-border bg-muted/40 text-foreground',
      props.class,
    )"
  >
    <slot />
  </div>
</template>
