<script setup lang="ts">
import { SliderRoot, SliderThumb, SliderTrack } from 'reka-ui'

const props = defineProps<{
  id: string
  label: string
  modelValue: number
}>()
const emit = defineEmits<{ 'update:modelValue': [value: number] }>()

function update(values: number[] | undefined): void {
  emit('update:modelValue', values?.[0] ?? props.modelValue)
}
</script>

<template>
  <div data-slot="opacity-slider" class="flex flex-col gap-2">
    <div class="flex items-center justify-between gap-3 text-xs">
      <Label :id="`${id}-label`">{{ label }}</Label>
      <span class="font-mono tabular-nums">{{ Math.round(modelValue) }}%</span>
    </div>
    <SliderRoot
      :id="id"
      data-slot="opacity-slider-root"
      :model-value="[modelValue]"
      :min="0"
      :max="100"
      :step="1"
      @update:model-value="update"
    >
      <SliderTrack data-slot="opacity-slider-track">
        <span data-slot="opacity-slider-range" :style="{ width: `${modelValue}%` }" />
      </SliderTrack>
      <SliderThumb data-slot="opacity-slider-thumb" :aria-labelledby="`${id}-label`" />
    </SliderRoot>
  </div>
</template>
