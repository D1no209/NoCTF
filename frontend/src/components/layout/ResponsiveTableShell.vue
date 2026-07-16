<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/card'

const props = withDefaults(defineProps<{
  minWidth?: string
  dense?: boolean
  class?: HTMLAttributes['class']
}>(), {
  minWidth: '720px',
})
</script>

<template>
  <Card :class="cn('overflow-hidden p-0', props.class)">
    <div class="w-full overflow-x-auto">
      <div :style="{ minWidth }" :class="dense ? 'text-sm' : ''">
        <slot />
      </div>
    </div>
  </Card>
</template>
