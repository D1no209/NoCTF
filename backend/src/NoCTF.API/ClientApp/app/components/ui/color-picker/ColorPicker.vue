<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { SliderRoot, SliderTrack, SliderRange, SliderThumb } from 'reka-ui'
import { Input } from '../input'
import { Label } from '../label'
import { hexToHsv, hsvToHex, normalizeHex } from './color'

const props = defineProps<{ modelValue: string; id: string }>()
const emit = defineEmits<{ 'update:modelValue': [color: string] }>()
const hsv = reactive(hexToHsv(normalizeHex(props.modelValue) ?? '#39FF14'))
const draft = ref(props.modelValue)
const invalid = ref(false)
watch(() => props.modelValue, value => {
  const normalized = normalizeHex(value)
  if (!normalized) return
  const next = hexToHsv(normalized)
  if (next.s > 0 && next.v > 0) hsv.h = next.h
  hsv.s = next.s; hsv.v = next.v; draft.value = normalized; invalid.value = false
})
const axes = computed(() => [
  { key: 'h' as const, max: 360, label: 'palette.hue', value: hsv.h },
  { key: 's' as const, max: 100, label: 'palette.saturation', value: hsv.s },
  { key: 'v' as const, max: 100, label: 'palette.value', value: hsv.v },
])
function publish() { const color = hsvToHex(hsv.h, hsv.s, hsv.v); draft.value = color; invalid.value = false; emit('update:modelValue', color) }
function changeAxis(axis: 'h' | 's' | 'v', values: number[] | undefined) { hsv[axis] = values?.[0] ?? hsv[axis]; publish() }
function applyHex() { const color = normalizeHex(draft.value); invalid.value = !color; if (color) emit('update:modelValue', color) }
function move(event: PointerEvent) {
  const plane = event.currentTarget as HTMLElement
  if (!plane.hasPointerCapture(event.pointerId)) return
  const box = plane.getBoundingClientRect()
  hsv.s = Math.max(0, Math.min(100, (event.clientX - box.left) / box.width * 100))
  hsv.v = Math.max(0, Math.min(100, 100 - (event.clientY - box.top) / box.height * 100))
  publish()
}
function start(event: PointerEvent) { if (event.button !== 0) return; (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId); move(event) }
</script>

<template>
  <div data-slot="color-picker" class="flex flex-col gap-4">
    <div data-slot="color-plane" :style="{ '--picker-hue': hsv.h }" aria-hidden="true" @pointerdown="start" @pointermove="move">
      <span data-slot="color-cursor" :style="{ left: `${hsv.s}%`, top: `${100 - hsv.v}%` }" />
    </div>
    <div v-for="axis in axes" :key="axis.key" class="flex flex-col gap-2">
      <div class="flex justify-between text-xs"><Label :id="`${id}-${axis.key}`">{{ $t(axis.label) }}</Label><span class="tabular-nums">{{ Math.round(axis.value) }}</span></div>
      <SliderRoot data-slot="color-slider" :model-value="[axis.value]" :min="0" :max="axis.max" :step="1" @update:model-value="changeAxis(axis.key, $event)">
        <SliderTrack data-slot="color-slider-track"><SliderRange data-slot="color-slider-range" /></SliderTrack>
        <SliderThumb data-slot="color-slider-thumb" :aria-labelledby="`${id}-${axis.key}`" />
      </SliderRoot>
    </div>
    <div class="flex flex-col gap-2">
      <Label :for="`${id}-hex`">{{ $t('palette.hex') }}</Label>
      <Input :id="`${id}-hex`" v-model="draft" maxlength="7" spellcheck="false" :aria-invalid="invalid || undefined" :aria-describedby="invalid ? `${id}-error` : undefined" @change="applyHex" @keydown.enter.prevent="applyHex" />
      <p v-if="invalid" :id="`${id}-error`" class="text-xs text-destructive">{{ $t('palette.invalidHex') }}</p>
    </div>
  </div>
</template>
