<script setup lang="ts">
import { computed } from 'vue'
import rough from 'roughjs'

const props = withDefaults(defineProps<{
  width?: number
  color?: string
  roughness?: number
  bowing?: number
  strokeWidth?: number
}>(), {
  width: 100,
  color: 'currentColor',
  roughness: 1.6,
  bowing: 1.4,
  strokeWidth: 2,
})

const rc = rough.generator()
const paths = computed(() => {
  return rc.toPaths(rc.line(0, 0, props.width, 0, {
    roughness: props.roughness,
    bowing: props.bowing,
    stroke: props.color,
    strokeWidth: props.strokeWidth,
  }))
})
</script>

<template>
  <svg :viewBox="`0 -5 ${width} 10`" class="h-2 w-auto overflow-visible" preserveAspectRatio="none">
    <path
      v-for="(p, i) in paths"
      :key="i"
      :d="p.d"
      :stroke="p.stroke"
      :fill="p.fill"
    />
  </svg>
</template>
