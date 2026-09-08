import { toRefs } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import type { PointsCurveValue } from '../../utils/game-config'
import { ctfPointsAtSolveCount, SCORE_DECAY_MODES, ScoreDecayMode } from '../../utils/game-config'

/** Owns state, effects and commands for PointsDecayCurve. */
export function usePointsDecayCurve(props: Readonly<{ curve: PointsCurveValue }>) {
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

  function setSvgElementRef(element: Element | ComponentPublicInstance | null) { svgElement.value = element as typeof svgElement.value }

  return {
      ...toRefs(props),
      ScoreDecayMode,
      width,
      height,
      inset,
      tooltipBox,
      hoveredCount,
      svgElement,
      preview,
      formatInteger,
      tooltipTransform,
      onPointerMove,
      onPointerLeave,
      onKeydown,
      setSvgElementRef
    }
}

export type PointsDecayCurveViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof usePointsDecayCurve>>>
