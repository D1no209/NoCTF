<script setup lang="ts">
import type { PointsCurveValue } from '~/utils/game-config'
import { ctfPointsAtSolveCount } from '~/utils/game-config'

const props = defineProps<{ curve: PointsCurveValue }>()

const width = 640
const height = 168
const inset = { top: 20, right: 18, bottom: 34, left: 52 }

const preview = computed(() => {
  const { initialPoints, minimumPoints, decayFactor } = props.curve
  if (initialPoints === null || minimumPoints === null || decayFactor === null
    || initialPoints < minimumPoints || minimumPoints < 0 || decayFactor <= 1)
    return null

  const plotWidth = width - inset.left - inset.right
  const plotHeight = height - inset.top - inset.bottom
  const scoreRange = Math.max(initialPoints - minimumPoints, 1)
  const samples = 48
  const points = Array.from({ length: samples + 1 }, (_, index) => {
    const progress = index / samples
    const solveCount = 1 + (decayFactor - 1) * progress
    const score = ctfPointsAtSolveCount(props.curve, solveCount) ?? minimumPoints
    const x = inset.left + plotWidth * progress
    const y = inset.top + plotHeight * ((initialPoints - score) / scoreRange)
    return `${x.toFixed(2)},${y.toFixed(2)}`
  }).join(' ')

  return { initialPoints, minimumPoints, decayFactor, points }
})

function formatValue(value: number): string {
  return new Intl.NumberFormat(localeTag(), { maximumFractionDigits: 2 }).format(value)
}
</script>

<template>
  <figure v-if="preview" class="col-span-full border border-border bg-muted/30 px-3 py-2">
    <figcaption class="mb-1 flex items-center justify-between gap-3 text-xs text-muted-foreground">
      <span>{{ $t('分值衰减曲线预览') }}</span>
      <span class="font-mono tabular-nums">
        {{ formatValue(preview.initialPoints) }} → {{ formatValue(preview.minimumPoints) }}
      </span>
    </figcaption>
    <svg
      class="h-36 w-full overflow-visible"
      :viewBox="`0 0 ${width} ${height}`"
      role="img"
      :aria-label="$t('分值随解题队伍数量增加而衰减')"
      preserveAspectRatio="none"
    >
      <line :x1="inset.left" :x2="width - inset.right" :y1="inset.top" :y2="inset.top" class="stroke-border" stroke-dasharray="3 5" />
      <line :x1="inset.left" :x2="width - inset.right" :y1="height - inset.bottom" :y2="height - inset.bottom" class="stroke-border" stroke-dasharray="3 5" />
      <line :x1="inset.left" :x2="inset.left" :y1="inset.top" :y2="height - inset.bottom" class="stroke-muted-foreground" />
      <line :x1="inset.left" :x2="width - inset.right" :y1="height - inset.bottom" :y2="height - inset.bottom" class="stroke-muted-foreground" />
      <polyline
        :points="preview.points"
        fill="none"
        class="stroke-primary"
        stroke-width="3"
        stroke-linecap="square"
        stroke-linejoin="round"
        stroke-dasharray="8 6"
        vector-effect="non-scaling-stroke"
      />
      <text x="4" :y="inset.top + 4" class="fill-muted-foreground text-[11px] font-mono">{{ formatValue(preview.initialPoints) }}</text>
      <text x="4" :y="height - inset.bottom + 4" class="fill-muted-foreground text-[11px] font-mono">{{ formatValue(preview.minimumPoints) }}</text>
      <text :x="inset.left" :y="height - 10" text-anchor="middle" class="fill-muted-foreground text-[11px] font-mono">1</text>
      <text :x="width - inset.right" :y="height - 10" text-anchor="end" class="fill-muted-foreground text-[11px] font-mono">
        {{ formatValue(preview.decayFactor) }} {{ $t('队') }}
      </text>
    </svg>
  </figure>
</template>
