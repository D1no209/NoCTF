<script setup lang="ts">
import { Check } from '@lucide/vue'
import { Button } from '../button'
import { colorInk } from './color'
defineProps<{ color: string; label: string; selected: boolean }>()
const emit = defineEmits<{ select: [color: string] }>()
</script>
<template>
  <Button variant="ghost" size="icon" :aria-label="label" :aria-pressed="selected" :style="{ backgroundColor: color, color: colorInk(color) }" @click="emit('select', color)"><Check v-if="selected" class="size-4" /></Button>
</template>
