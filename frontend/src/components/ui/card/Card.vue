<script setup lang="ts">
import type { HTMLAttributes } from "vue"
import { computed } from "vue"
import { cn } from "@/lib/utils"

const props = withDefaults(defineProps<{
  class?: HTMLAttributes["class"]
  decorated?: boolean
}>(), {
  decorated: true,
})

const GRID = 8
const CELL = 8
const SIZE = GRID * CELL

const DIAGONAL = GRID - 1

const cells = computed(() => {
  const list: { x: number; y: number; opacity: number }[] = []
  for (let row = 0; row < GRID; row++) {
    for (let col = 0; col < GRID; col++) {
      const sum = col + row
      if (sum < DIAGONAL)
        continue
      const distance = (sum - DIAGONAL) / DIAGONAL
      const opacity = Math.min(1, Math.max(0.03, distance ** 1.5))
      list.push({
        x: col * CELL,
        y: row * CELL,
        opacity: Number(opacity.toFixed(3)),
      })
    }
  }
  return list
})
</script>

<template>
  <div
    data-slot="card"
    :class="
      cn(
        'relative flex flex-col gap-4 border-2 border-card-border bg-card py-4 text-card-foreground shadow-card',
        props.class,
      )
    "
  >
    <slot />

    <svg
      v-if="props.decorated"
      :width="SIZE"
      :height="SIZE"
      :viewBox="`0 0 ${SIZE} ${SIZE}`"
      class="pointer-events-none absolute -bottom-1 -right-1 z-0"
      aria-hidden="true"
    >
      <rect
        v-for="cell in cells"
        :key="`${cell.x}-${cell.y}`"
        :x="cell.x"
        :y="cell.y"
        :width="CELL"
        :height="CELL"
        fill="black"
        :fill-opacity="cell.opacity"
      />
    </svg>
  </div>
</template>
