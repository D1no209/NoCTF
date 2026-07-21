<script setup lang="ts">
import { computed } from 'vue'
import rough from 'roughjs'

export interface HandDrawnPoint {
  x: number
  y: number
}

export interface HandDrawnSeries {
  id: string
  name: string
  color: string
  points: HandDrawnPoint[]
}

interface Props {
  series: HandDrawnSeries[]
  width?: number
  height?: number
  margin?: { top: number, right: number, bottom: number, left: number }
  xTicks?: { value: number, label: string }[]
  yTicks?: { value: number, label: string }[]
  xLabel?: string
  yLabel?: string
  highlightedId?: string | null
  empty?: boolean
  emptyLabel?: string
}

const props = withDefaults(defineProps<Props>(), {
  width: 660,
  height: 240,
  margin: () => ({ top: 32, right: 24, bottom: 40, left: 58 }),
  xTicks: () => [],
  yTicks: () => [],
  xLabel: '',
  yLabel: '',
  highlightedId: null,
  empty: false,
  emptyLabel: 'No data',
})

const emit = defineEmits<{
  highlight: [id: string | null]
}>()

const innerWidth = computed(() => Math.max(1, props.width - props.margin.left - props.margin.right))
const innerHeight = computed(() => Math.max(1, props.height - props.margin.top - props.margin.bottom))

const bounds = computed(() => {
  const allX = props.series.flatMap(s => s.points.map(p => p.x)).concat(props.xTicks.map(t => t.value))
  const allY = props.series.flatMap(s => s.points.map(p => p.y)).concat(props.yTicks.map(t => t.value))
  const minX = allX.length ? Math.min(...allX) : 0
  const maxX = allX.length ? Math.max(...allX) : 1
  const minY = allY.length ? Math.min(...allY) : 0
  const maxY = allY.length ? Math.max(...allY) : 1
  return {
    minX,
    maxX,
    minY: Math.min(0, minY),
    maxY: Math.max(1, maxY),
  }
})

function scaleX(value: number) {
  const { minX, maxX } = bounds.value
  return props.margin.left + ((value - minX) / Math.max(1, maxX - minX)) * innerWidth.value
}

function scaleY(value: number) {
  const { minY, maxY } = bounds.value
  return props.height - props.margin.bottom - ((value - minY) / Math.max(1, maxY - minY)) * innerHeight.value
}

const rc = rough.generator()

const axisPaths = computed(() => {
  const left = props.margin.left
  const right = props.width - props.margin.right
  const top = props.margin.top
  const bottom = props.height - props.margin.bottom
  return [
    ...rc.toPaths(rc.line(left, bottom, right, bottom, { roughness: 0.8, bowing: 0.6, strokeWidth: 1.5, stroke: 'currentColor' })),
    ...rc.toPaths(rc.line(left, top, left, bottom, { roughness: 0.8, bowing: 0.6, strokeWidth: 1.5, stroke: 'currentColor' })),
  ]
})

const gridPaths = computed(() => {
  const left = props.margin.left
  const right = props.width - props.margin.right
  const top = props.margin.top
  const bottom = props.height - props.margin.bottom
  const paths: { d: string, stroke?: string, fill?: string }[] = []
  for (const tick of props.yTicks) {
    const y = scaleY(tick.value)
    paths.push(...rc.toPaths(rc.line(left, y, right, y, { roughness: 0.4, bowing: 0.3, strokeWidth: 0.5, stroke: 'currentColor' })))
  }
  for (const tick of props.xTicks) {
    const x = scaleX(tick.value)
    paths.push(...rc.toPaths(rc.line(x, top, x, bottom, { roughness: 0.4, bowing: 0.3, strokeWidth: 0.5, stroke: 'currentColor' })))
  }
  return paths
})

const seriesRender = computed(() => {
  return props.series.map((s) => {
    const sorted = [...s.points].sort((a, b) => a.x - b.x)
    const pts = sorted.map(p => [scaleX(p.x), scaleY(p.y)] as [number, number])
    const highlighted = !props.highlightedId || props.highlightedId === s.id
    const linePaths = pts.length
      ? rc.toPaths(rc.curve(pts, {
          roughness: 1.6,
          bowing: 1.4,
          stroke: s.color,
          strokeWidth: highlighted ? 3 : 2,
          fill: 'none',
        }))
      : []
    const dotPaths = highlighted && pts.length
      ? pts.flatMap(([x, y]) => rc.toPaths(rc.circle(x, y, 6, { roughness: 1.2, stroke: s.color, fill: s.color, fillStyle: 'solid' })))
      : []
    return { id: s.id, linePaths, dotPaths, highlighted }
  })
})

function onSeriesEnter(id: string) {
  emit('highlight', id)
}

function onSeriesLeave() {
  emit('highlight', null)
}
</script>

<template>
  <div class="relative">
    <svg
      :viewBox="`0 0 ${width} ${height}`"
      class="h-64 w-full overflow-visible"
      :class="empty ? 'opacity-60' : ''"
      @mouseleave="onSeriesLeave"
    >
      <g class="hand-drawn-grid text-muted-foreground/30">
        <path
          v-for="(p, i) in gridPaths"
          :key="`grid-${i}`"
          :d="p.d"
          :stroke="p.stroke"
          :fill="p.fill"
        />
      </g>

      <g class="text-[10px]">
        <text
          v-for="tick in yTicks"
          :key="`y-${tick.value}`"
          :x="margin.left - 10"
          :y="scaleY(tick.value) + 3"
          text-anchor="end"
          class="fill-muted-foreground tabular-nums"
        >
          {{ tick.label }}
        </text>
        <text
          v-for="tick in xTicks"
          :key="`x-${tick.value}`"
          :x="scaleX(tick.value)"
          :y="height - margin.bottom + 19"
          text-anchor="middle"
          class="fill-muted-foreground"
        >
          {{ tick.label }}
        </text>
      </g>

      <text
        :x="margin.left - 38"
        :y="margin.top - 10"
        class="fill-muted-foreground text-[10px] font-medium"
      >
        {{ yLabel }}
      </text>
      <text
        :x="width - margin.right"
        :y="height - margin.bottom + 33"
        text-anchor="end"
        class="fill-muted-foreground text-[10px] font-medium"
      >
        {{ xLabel }}
      </text>

      <g class="hand-drawn-axis text-foreground">
        <path
          v-for="(p, i) in axisPaths"
          :key="`axis-${i}`"
          :d="p.d"
          :stroke="p.stroke"
          :fill="p.fill"
        />
      </g>

      <g v-if="!empty">
        <g
          v-for="s in seriesRender"
          :key="s.id"
          class="cursor-pointer transition-opacity duration-200"
          :class="s.highlighted ? 'opacity-100' : 'opacity-25'"
          @mouseenter="onSeriesEnter(s.id)"
          @focus="onSeriesEnter(s.id)"
        >
          <path
            v-for="(p, i) in s.linePaths"
            :key="`line-${i}`"
            :d="p.d"
            :stroke="p.stroke"
            :fill="p.fill"
          />
          <path
            v-for="(p, i) in s.dotPaths"
            :key="`dot-${i}`"
            :d="p.d"
            :stroke="p.stroke"
            :fill="p.fill"
          />
        </g>
      </g>

      <g v-else>
        <path
          :d="`M ${margin.left} ${height / 2} L ${width - margin.right} ${height / 2}`"
          fill="none"
          stroke="currentColor"
          stroke-width="1"
          stroke-dasharray="6 6"
          class="text-muted-foreground/40"
        />
        <text
          :x="width / 2"
          :y="height / 2 - 12"
          text-anchor="middle"
          class="fill-muted-foreground text-xs"
        >
          {{ emptyLabel }}
        </text>
      </g>
    </svg>
  </div>
</template>
