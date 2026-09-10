<script setup lang="ts">
import { RovingFocusItem } from 'reka-ui'
import { Button } from '../button'

defineProps<{ value: string; label: string; selected: boolean; controls?: string }>()
const emit = defineEmits<{ select: [value: string] }>()
</script>

<template>
  <RovingFocusItem as-child :active="selected" focusable>
    <Button variant="ghost" role="option" :aria-selected="selected" :aria-label="label" :aria-controls="controls"
      :data-wave-item="value" data-slot="wave-selection-item" class="noctf-wave-item" @click="emit('select', value)">
      <slot><span>{{ label }}</span></slot>
    </Button>
  </RovingFocusItem>
</template>
