<script setup lang="ts">
import type { PointsCurveValue } from '~/utils/game-config'
import { ctfPointsAtSolveCount, SCORE_DECAY_MODES, ScoreDecayMode } from '~/utils/game-config'

const props = defineProps<{ curve: PointsCurveValue }>()

const width = 760
const height = 340
const inset = { top: 32, right: 28, bottom: 66, left: 78 }
const maximumVisualSamples = 240
const tooltipBox = { width: 188, height: 50, offsetX: 22, offsetY: 18 }
const hoveredCount = ref<number | null>(null)
const hoverPointer = ref<{ x: number; y: number } | null>(null)
const svgElement = ref<SVGSVGElement | null>(null)

const preview = computed(() => {
  const { initialPoints, minimumPoints, decayTeamCount, decayMode } = props.curve
  if (decayMode === ScoreDecayMode.Custom)
    return null
  if (initialPoints === null || minimumPoints === null || decayTeamCount === null
    || initialPoints < minimumPoints || minimumPoints < 0 || decayTeamCount <= 1)
    return null

  const maxCount = Math.max(2, Math.ceil(decayTeamCount))
  const plotWidth = width - inset.left - inset.right
  const plotHeight = height - inset.top - inset.bottom
  const scoreSpan = Math.max(initialPoints - minimumPoints, 1)
  const scoreAt = (count: number) => ctfPointsAtSolveCount(props.curve, count) ?? minimumPoints
  const position = (count: number, score: number) => ({
    x: inset.left + plotWidth * ((count - 1) / (maxCount - 1)),
    y: inset.top + plotHeight * ((initialPoints - score) / scoreSpan),
  })
  const sampleCount = Math.min(maxCount, maximumVisualSamples)
  const samples = Array.from({ length: sampleCount }, (_, index) => {
    const count = sampleCount === 1
      ? 1
      : Math.round(1 + ((maxCount - 1) * index) / (sampleCount - 1))
    const score = scoreAt(count)
    return { count, score, ...position(count, score) }
  })
  const xTickCount = Math.min(maxCount - 1, 12)
  const xTicks = Array.from({ length: xTickCount + 1 }, (_, index) => {
    const count = Math.round(1 + ((maxCount - 1) * index) / xTickCount)
    return { count, ...position(count, scoreAt(count)) }
  }).filter((tick, index, ticks) => index === 0 || tick.count !== ticks[index - 1]?.count)
  const yTickCount = 8
  const yTicks = Array.from({ length: yTickCount + 1 }, (_, index) => {
    const ratio = index / yTickCount
    const score = Math.round(initialPoints + (minimumPoints - initialPoints) * ratio)
    return { score, y: inset.top + plotHeight * ratio }
  })
  const activeCount = Math.min(maxCount, Math.max(1, hoveredCount.value ?? 1))
  const activeScore = scoreAt(activeCount)
  const active = { count: activeCount, score: activeScore, ...position(activeCount, activeScore) }
  const modeLabel = SCORE_DECAY_MODES.find(option => option.value === decayMode)?.label ?? ''
  return {
    initialPoints,
    minimumPoints,
    maxCount,
    samples,
    xTicks,
    yTicks,
    active,
    modeLabel,
    points: samples.map(point => `${point.x.toFixed(2)},${point.y.toFixed(2)}`).join(' '),
  }
})

function formatInteger(value: number): string {
  return new Intl.NumberFormat(localeTag(), { maximumFractionDigits: 0 }).format(Math.round(value))
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value))
}

function tooltipPosition(anchor: { x: number; y: number }) {
  const minX = inset.left + 4
  const maxX = width - inset.right - tooltipBox.width
  const minY = inset.top + 4
  const maxY = height - inset.bottom - tooltipBox.height - 4
  let x = anchor.x + tooltipBox.offsetX
  if (x > maxX)
    x = anchor.x - tooltipBox.width - tooltipBox.offsetX

  let y = anchor.y - tooltipBox.height - tooltipBox.offsetY
  if (y < minY)
    y = anchor.y + tooltipBox.offsetY

  return {
    x: clamp(x, minX, maxX),
    y: clamp(y, minY, maxY),
  }
}

const tooltipTransform = computed(() => {
  if (!preview.value)
    return ''

  const anchor = hoverPointer.value ?? preview.value.active
  const position = tooltipPosition(anchor)
  return `translate(${position.x},${position.y})`
})

function clientPointToSvg(clientX: number, clientY: number) {
  if (!svgElement.value)
    return null

  const matrix = svgElement.value.getScreenCTM()
  if (!matrix)
    return null

  const point = svgElement.value.createSVGPoint()
  point.x = clientX
  point.y = clientY
  return point.matrixTransform(matrix.inverse())
}

function updateHover(clientX: number, clientY: number) {
  if (!preview.value) return
  const local = clientPointToSvg(clientX, clientY)
  if (!local) return
  const plotWidth = width - inset.left - inset.right
  const ratio = Math.min(1, Math.max(0, (local.x - inset.left) / plotWidth))
  hoveredCount.value = Math.round(1 + ratio * (preview.value.maxCount - 1))
  hoverPointer.value = {
    x: clamp(local.x, inset.left, width - inset.right),
    y: clamp(local.y, inset.top, height - inset.bottom),
  }
}

function onPointerMove(event: PointerEvent) {
  updateHover(event.clientX, event.clientY)
}

function onPointerLeave() {
  hoveredCount.value = null
  hoverPointer.value = null
}

function onKeydown(event: KeyboardEvent) {
  if (!preview.value || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return
  event.preventDefault()
  const current = hoveredCount.value ?? 1
  if (event.key === 'Home') hoveredCount.value = 1
  else if (event.key === 'End') hoveredCount.value = preview.value.maxCount
  else hoveredCount.value = Math.min(
    preview.value.maxCount,
    Math.max(1, current + (event.key === 'ArrowRight' ? 1 : -1)),
  )
}
</script>

<template>
  <figure v-if="preview" class="col-span-full border border-border bg-muted/20 px-4 py-3">
    <figcaption class="mb-2 flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground">
      <span>{{ $t('分值衰减曲线预览') }} · {{ $t(preview.modeLabel) }}</span>
      <span class="font-mono tabular-nums">
        {{ formatInteger(preview.initialPoints) }} → {{ formatInteger(preview.minimumPoints) }} pts
      </span>
    </figcaption>
    <svg
      ref="svgElement"
      class="h-72 w-full touch-none overflow-visible outline-none focus-visible:ring-2 focus-visible:ring-primary"
      :viewBox="`0 0 ${width} ${height}`"
      role="img"
      tabindex="0"
      :aria-label="$t('分值随解题队伍数量增加而衰减；使用左右方向键查看每个整数点')"
      preserveAspectRatio="xMidYMid meet"
      @pointermove="onPointerMove"
      @pointerleave="onPointerLeave"
      @focus="hoveredCount ??= 1"
      @keydown="onKeydown"
    >
      <g aria-hidden="true">
        <line
          v-for="(tick, index) in preview.yTicks"
          :key="`y-${tick.score}`"
          :x1="inset.left"
          :x2="width - inset.right"
          :y1="tick.y"
          :y2="tick.y"
          :class="index === 0 || index === preview.yTicks.length - 1 ? 'stroke-muted-foreground/70' : 'stroke-border'"
          stroke-dasharray="3 4"
        />
        <line
          v-for="tick in preview.xTicks"
          :key="`x-${tick.count}`"
          :x1="tick.x"
          :x2="tick.x"
          :y1="inset.top"
          :y2="height - inset.bottom"
          class="stroke-border/80"
          stroke-dasharray="2 5"
        />
        <line :x1="inset.left" :x2="inset.left" :y1="inset.top" :y2="height - inset.bottom" class="stroke-muted-foreground" />
        <line :x1="inset.left" :x2="width - inset.right" :y1="height - inset.bottom" :y2="height - inset.bottom" class="stroke-muted-foreground" />
        <text
          v-for="tick in preview.yTicks"
          :key="`yl-${tick.score}`"
          :x="inset.left - 10"
          :y="tick.y + 4"
          text-anchor="end"
          class="fill-muted-foreground text-[12px] font-mono tabular-nums"
        >{{ formatInteger(tick.score) }}</text>
        <text
          v-for="tick in preview.xTicks"
          :key="`xl-${tick.count}`"
          :x="tick.x"
          :y="height - inset.bottom + 20"
          text-anchor="middle"
          class="fill-muted-foreground text-[12px] font-mono tabular-nums"
        >{{ tick.count }}</text>
        <text :x="inset.left" :y="inset.top - 13" class="fill-muted-foreground text-[12px] font-medium">{{ $t('取整分值') }} (pts)</text>
        <text :x="width - inset.right" :y="height - 13" text-anchor="end" class="fill-muted-foreground text-[12px] font-medium">{{ $t('解题队伍数') }}</text>
      </g>
      <polyline
        :points="preview.points"
        fill="none"
        class="stroke-primary"
        stroke-width="3"
        stroke-linecap="round"
        stroke-linejoin="round"
        vector-effect="non-scaling-stroke"
      />
      <line
        :x1="preview.active.x"
        :x2="preview.active.x"
        :y1="inset.top"
        :y2="height - inset.bottom"
        class="stroke-primary/60"
        stroke-dasharray="4 4"
        vector-effect="non-scaling-stroke"
      />
      <circle :cx="preview.active.x" :cy="preview.active.y" r="5" class="fill-background stroke-primary" stroke-width="3" />
      <g :transform="tooltipTransform">
        <rect :width="tooltipBox.width" :height="tooltipBox.height" rx="4" class="fill-popover stroke-border" />
        <text x="11" y="19" class="fill-muted-foreground text-[11px]">{{ $t('第 {count} 支解题队伍', { count: preview.active.count }) }}</text>
        <text x="11" y="38" class="fill-popover-foreground text-[14px] font-mono font-semibold tabular-nums">{{ formatInteger(preview.active.score) }} pts</text>
      </g>
    </svg>
    <p class="mt-1 text-xs text-muted-foreground">
      {{ $t('将鼠标移到曲线上查看整数解题队伍数对应的取整分值；键盘可使用左右方向键。') }}
    </p>
  </figure>
  <div v-else-if="curve.decayMode === ScoreDecayMode.Custom" class="border border-dashed border-border bg-muted/20 px-4 py-3 text-xs text-muted-foreground">
    {{ $t('自定义公式由后端逐点校验；保存成功后才会参与计分。') }}
  </div>
</template>
