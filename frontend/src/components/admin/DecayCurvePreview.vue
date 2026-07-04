<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Badge } from '@/components/ui/badge'

interface DecayCurveConfig {
  initialPoints?: number | string | null
  minimumPoints?: number | string | null
  decayFactor?: number | string | null
  decayFunction?: string | null
  difficultyCoefficient?: number | string | null
}

interface NormalizedCurveConfig {
  initial: number
  minimum: number
  decayFactor: number
  decayFunction: string
  difficultyCoefficient: number
}

interface CurvePoint {
  solves: number
  score: number
  x: number
  y: number
}

interface HoverCurvePoint extends CurvePoint {
  tooltipX: number
  tooltipY: number
}

const props = defineProps<{
  config: DecayCurveConfig
}>()

const { t } = useI18n()
const hoverPoint = ref<HoverCurvePoint | null>(null)

const curveViewBox = {
  width: 320,
  height: 158,
} as const

const curvePlot = {
  left: 42,
  top: 18,
  right: 292,
  bottom: 116,
} as const

const curvePlotWidth = curvePlot.right - curvePlot.left
const curvePlotHeight = curvePlot.bottom - curvePlot.top

function numberOrDefault(value: number | string | undefined | null, fallback: number) {
  if (value === undefined || value === null || value === '')
    return fallback
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

function clamp(value: number, minimum: number, maximum: number) {
  return Math.min(maximum, Math.max(minimum, value))
}

function normalizeConfig(config: DecayCurveConfig): NormalizedCurveConfig {
  const initialValue = numberOrDefault(config.initialPoints, 500)
  const minimumValue = numberOrDefault(config.minimumPoints, 100)
  const initial = Math.max(initialValue, minimumValue)
  const minimum = Math.min(minimumValue, initial)

  return {
    initial,
    minimum,
    decayFactor: Math.max(1, numberOrDefault(config.decayFactor, 450)),
    decayFunction: config.decayFunction || 'sigmoid',
    difficultyCoefficient: Math.max(0.1, numberOrDefault(config.difficultyCoefficient, 1)),
  }
}

function normalizeDecay(progress: number, decayFunction: string) {
  const key = (decayFunction || 'sigmoid').toLowerCase()
  if (key === 'linear')
    return Math.min(1, progress)
  if (key === 'quadratic')
    return Math.min(1, progress * progress)
  if (key === 'logarithmic')
    return progress >= 1 ? 1 : Math.log(1 + progress * 9) / Math.log(10)
  if (progress >= 1)
    return 1
  const logistic = (value: number) => 1 / (1 + Math.exp(-10 * (value - 0.5)))
  const start = logistic(0)
  const end = logistic(1)
  return Math.min(1, Math.max(0, (logistic(progress) - start) / (end - start)))
}

function scoreForSolves(solves: number, config: NormalizedCurveConfig) {
  const range = config.initial - config.minimum
  if (solves <= 1 || range <= 0)
    return config.initial
  const effectiveDecay = Math.max(1, config.decayFactor * config.difficultyCoefficient)
  const progress = Math.max(0, solves / effectiveDecay)
  const normalized = normalizeDecay(progress, config.decayFunction)
  return Math.max(config.minimum, Math.min(config.initial, Math.floor(config.initial - range * normalized)))
}

function xForSolves(solves: number, maxSolves: number) {
  return curvePlot.left + (solves / Math.max(1, maxSolves)) * curvePlotWidth
}

function yForScore(score: number, config: NormalizedCurveConfig) {
  return curvePlot.bottom - ((score - config.minimum) / Math.max(1, config.initial - config.minimum)) * curvePlotHeight
}

function pointForSolves(solves: number, maxSolves: number, config: NormalizedCurveConfig): CurvePoint {
  const score = scoreForSolves(solves, config)
  return {
    solves,
    score,
    x: xForSolves(solves, maxSolves),
    y: yForScore(score, config),
  }
}

function integerTicks(maxValue: number, steps = 4) {
  const values = new Set<number>()
  for (let index = 0; index <= steps; index++)
    values.add(Math.round((maxValue * index) / steps))
  values.add(maxValue)
  return Array.from(values).sort((first, second) => first - second)
}

function scoreTicks(config: NormalizedCurveConfig, steps = 4) {
  const values = new Set<number>()
  for (let index = 0; index <= steps; index++) {
    const value = Math.round(config.initial - ((config.initial - config.minimum) * index) / steps)
    values.add(value)
  }
  return Array.from(values).sort((first, second) => second - first)
}

function findMinimumAt(maxSolves: number, config: NormalizedCurveConfig) {
  if (scoreForSolves(0, config) <= config.minimum)
    return 0

  let left = 0
  let right = maxSolves
  let result: number | null = null

  while (left <= right) {
    const mid = Math.floor((left + right) / 2)
    if (scoreForSolves(mid, config) <= config.minimum) {
      result = mid
      right = mid - 1
    }
    else {
      left = mid + 1
    }
  }

  return result
}

function buildCurve(config: DecayCurveConfig) {
  const normalized = normalizeConfig(config)
  const effectiveDecay = Math.max(1, normalized.decayFactor * normalized.difficultyCoefficient)
  const maxSolves = Math.max(10, Math.ceil(effectiveDecay))
  const sampleCount = Math.min(maxSolves, 96)
  const sampledSolves = Array.from({ length: sampleCount + 1 }, (_, index) =>
    Math.round((maxSolves * index) / sampleCount))
  const points = Array.from(new Set(sampledSolves)).map(solves =>
    pointForSolves(solves, maxSolves, normalized))
  const path = points.map((point, index) =>
    `${index === 0 ? 'M' : 'L'} ${point.x.toFixed(1)} ${point.y.toFixed(1)}`).join(' ')

  return {
    config: normalized,
    maxSolves,
    path,
    minimumAt: findMinimumAt(maxSolves, normalized),
    xTicks: integerTicks(maxSolves).map(value => ({
      value,
      x: xForSolves(value, maxSolves),
    })),
    yTicks: scoreTicks(normalized).map(value => ({
      value,
      y: yForScore(value, normalized),
    })),
  }
}

const curve = computed(() => buildCurve(props.config))

function withTooltipPosition(point: CurvePoint): HoverCurvePoint {
  const tooltipWidth = 92
  const tooltipHeight = 36
  const preferredX = point.x > curveViewBox.width - tooltipWidth - 16
    ? point.x - tooltipWidth - 10
    : point.x + 10
  const preferredY = point.y > curvePlot.top + tooltipHeight + 8
    ? point.y - tooltipHeight - 8
    : point.y + 12

  return {
    ...point,
    tooltipX: clamp(preferredX, 4, curveViewBox.width - tooltipWidth - 4),
    tooltipY: clamp(preferredY, 4, curveViewBox.height - tooltipHeight - 4),
  }
}

function updateHoverPoint(event: MouseEvent) {
  const target = event.currentTarget as SVGSVGElement
  const rect = target.getBoundingClientRect()
  const svgX = ((event.clientX - rect.left) / rect.width) * curveViewBox.width
  const boundedX = clamp(svgX, curvePlot.left, curvePlot.right)
  const solves = clamp(
    Math.round(((boundedX - curvePlot.left) / curvePlotWidth) * curve.value.maxSolves),
    0,
    curve.value.maxSolves,
  )
  hoverPoint.value = withTooltipPosition(pointForSolves(solves, curve.value.maxSolves, curve.value.config))
}
</script>

<template>
  <div class="rounded-lg border bg-muted/30 p-3">
    <div class="mb-2 flex items-center justify-between">
      <div>
        <div class="text-sm font-medium">
          {{ t('admin.competitionDetail.decayPreview') }}
        </div>
        <div class="text-xs text-muted-foreground">
          {{ curve.minimumAt == null ? t('admin.competitionDetail.minimumNotReached') : t('admin.competitionDetail.minimumReachedAt', { count: curve.minimumAt }) }}
        </div>
      </div>
      <Badge variant="outline">
        {{ curve.maxSolves }} {{ t('scoreboard.solves') }}
      </Badge>
    </div>

    <svg
      :viewBox="`0 0 ${curveViewBox.width} ${curveViewBox.height}`"
      class="h-40 w-full select-none"
      role="img"
      @mousemove="updateHoverPoint"
      @mouseleave="hoverPoint = null"
    >
      <g>
        <line
          v-for="tick in curve.yTicks"
          :key="`y-${tick.value}`"
          :x1="curvePlot.left"
          :x2="curvePlot.right"
          :y1="tick.y"
          :y2="tick.y"
          class="stroke-border/80"
          stroke-width="1"
          stroke-dasharray="3 4"
        />
        <line
          v-for="tick in curve.xTicks"
          :key="`x-${tick.value}`"
          :x1="tick.x"
          :x2="tick.x"
          :y1="curvePlot.top"
          :y2="curvePlot.bottom"
          class="stroke-border/60"
          stroke-width="1"
          stroke-dasharray="3 4"
        />
        <line :x1="curvePlot.left" :x2="curvePlot.right" :y1="curvePlot.bottom" :y2="curvePlot.bottom" class="stroke-border" stroke-width="1.25" />
        <line :x1="curvePlot.left" :x2="curvePlot.left" :y1="curvePlot.top" :y2="curvePlot.bottom" class="stroke-border" stroke-width="1.25" />
        <text
          v-for="tick in curve.yTicks"
          :key="`yl-${tick.value}`"
          :x="curvePlot.left - 8"
          :y="tick.y + 3"
          text-anchor="end"
          class="fill-muted-foreground text-[10px]"
        >
          {{ tick.value }}
        </text>
        <text
          v-for="tick in curve.xTicks"
          :key="`xl-${tick.value}`"
          :x="tick.x"
          :y="curvePlot.bottom + 16"
          text-anchor="middle"
          class="fill-muted-foreground text-[10px]"
        >
          {{ tick.value }}
        </text>
        <text :x="curvePlot.left" y="10" class="fill-muted-foreground text-[10px]">
          {{ t('admin.competitionDetail.pointsAxis') }}
        </text>
        <text :x="curvePlot.right" :y="curvePlot.bottom + 34" text-anchor="end" class="fill-muted-foreground text-[10px]">
          {{ t('admin.competitionDetail.solvesAxis') }}
        </text>
      </g>

      <path :d="curve.path" fill="none" stroke="oklch(0.56 0.23 262)" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" />

      <g v-if="hoverPoint">
        <line :x1="hoverPoint.x" :x2="hoverPoint.x" :y1="curvePlot.top" :y2="curvePlot.bottom" class="stroke-primary/60" stroke-width="1.25" />
        <line :x1="curvePlot.left" :x2="curvePlot.right" :y1="hoverPoint.y" :y2="hoverPoint.y" class="stroke-primary/40" stroke-width="1.25" />
        <circle :cx="hoverPoint.x" :cy="hoverPoint.y" r="4" class="fill-primary stroke-background" stroke-width="2" />
        <g :transform="`translate(${hoverPoint.tooltipX} ${hoverPoint.tooltipY})`">
          <rect width="92" height="36" rx="6" class="fill-background stroke-border" stroke-width="1" />
          <text x="8" y="14" class="fill-foreground text-[10px] font-medium">
            {{ t('admin.competitionDetail.decayTooltipSolves', { count: hoverPoint.solves }) }}
          </text>
          <text x="8" y="28" class="fill-muted-foreground text-[10px]">
            {{ t('admin.competitionDetail.decayTooltipPoints', { points: hoverPoint.score }) }}
          </text>
        </g>
      </g>
    </svg>
  </div>
</template>
