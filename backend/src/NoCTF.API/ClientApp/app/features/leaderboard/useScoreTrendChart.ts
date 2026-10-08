import { toRefs } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import type { TooltipComponentOption } from 'echarts/components'
import { chartTooltipTheme, echarts, trendChartPalette } from '../../utils/echarts'
import { themeColor } from '../../utils/theme-color'
import type { TrendSeries } from './types'
import { scoreTrendTimeRange } from './score-trend-range'

export interface ScoreTrendChartProps {
  title: string
  series: TrendSeries[]
  revision: string | number | null
  /** 时间轴范围(ISO);没有数据的队伍画一条 0 分平线。 */
  rangeStart: string | null
  rangeEnd: string | null
  height: string
}

/** Owns state, effects and commands for ScoreTrendChart. */
export function useScoreTrendChart(props: Readonly<ScoreTrendChartProps>) {
  const el = ref<HTMLElement | null>(null)

  const { locale } = useLocale()

  const { isDark } = useTheme()

  let chart: echarts.ECharts | null = null

  let timeRange = scoreTrendTimeRange(props.series, props.rangeStart, props.rangeEnd)

  let selectedWindow: { start: number, end: number } | null = null

  let selectedScoreRange: { start: number, end: number } | null = null

  let focusedTeamName: string | null = null

  let hoveredLine: { seriesIndex: number, at: number } | null = null

  let applyingOption = false

  let renderFrame = 0

  let lastSeriesSignature = ''

  function zoomValue(value: unknown, percent: unknown, edge: 'start' | 'end'): number {
    const numeric = typeof value === 'number' ? value : Number.NaN
    if (Number.isFinite(numeric)) return numeric
    const ratio = typeof percent === 'number' && Number.isFinite(percent)
      ? Math.min(100, Math.max(0, percent)) / 100
      : edge === 'start' ? 0 : 1
    return timeRange.axisMin + (timeRange.axisMax - timeRange.axisMin) * ratio
  }

  function rememberZoom() {
    if (!chart || applyingOption) return
    // Read the resolved windows: wheel batches and toolbox selections use different payloads.
    const zooms = chart.getOption().dataZoom as Array<{
      id: string, start: number, end: number, startValue?: number, endValue?: number,
    }>
    const timeZoom = zooms.find(zoom => zoom.id === 'score-trend-time-slider')
    if (timeZoom) {
      const start = zoomValue(timeZoom.startValue, timeZoom.start, 'start')
      const end = zoomValue(timeZoom.endValue, timeZoom.end, 'end')
      selectedWindow = timeZoom.start <= 0 && timeZoom.end >= 100 || start >= end
        ? null
        : { start, end }
    }
    const scoreZoom = zooms.find(zoom => zoom.id === 'score-trend-score-inside')
    if (scoreZoom) {
      const { start, end } = scoreZoom
      selectedScoreRange = start <= 0 && end >= 100 || start >= end
        ? null
        : { start, end }
    }
  }

  function restoreChartView() {
    if (!applyingOption) {
      selectedWindow = null
      selectedScoreRange = null
      focusedTeamName = null
      scheduleRender()
    }
  }

  function toggleTeamFocus(...args: unknown[]) {
    if (applyingOption) return
    const name = (args[0] as { name?: unknown } | undefined)?.name
    if (typeof name !== 'string' || !name) return
    focusedTeamName = focusedTeamName === name ? null : name
    scheduleRender()
  }

  function releaseWheelToScrollSurface(event: WheelEvent) {
    if (!event.ctrlKey) event.stopImmediatePropagation()
  }

  function rememberHoveredLine(event: echarts.ECElementEvent) {
    if (!event.event || event.seriesIndex === undefined) {
      hoveredLine = null
      return
    }
    const at = chart?.convertFromPixel({ xAxisIndex: 0 }, event.event.offsetX)
    hoveredLine = typeof at === 'number' && Number.isFinite(at)
      ? { seriesIndex: event.seriesIndex, at }
      : null
    // Axis tooltips otherwise reuse cached content while moving between sparse step lines.
    chart?.dispatchAction({ type: 'hideTip' })
  }

  function clearHoveredLine() { hoveredLine = null }

  function buildOption(): echarts.EChartsCoreOption {
    timeRange = scoreTrendTimeRange(
      props.series,
      props.rangeStart,
      props.rangeEnd,
    )
    const zoomWindow = selectedWindow
      ? {
          startValue: Math.max(timeRange.axisMin, selectedWindow.start),
          endValue: Math.min(timeRange.axisMax, selectedWindow.end),
        }
      : { start: 0, end: 100 }
    const scoreZoom = selectedScoreRange ?? { start: 0, end: 100 }
    const foreground = el.value ? getComputedStyle(el.value).color : undefined
    const fontFamily = el.value ? getComputedStyle(el.value).fontFamily : undefined
    const palette = trendChartPalette(el.value)
    const border = themeColor('--border', el.value ?? undefined)
    const primary = themeColor('--primary', el.value ?? undefined)
    const muted = themeColor('--muted-foreground', el.value ?? undefined)
    const teamNames = props.series.map(team => team.teamName ?? translate('common.label.team'))
    if (focusedTeamName && !teamNames.includes(focusedTeamName))
      focusedTeamName = null
    const selectedTeams = Object.fromEntries(teamNames.map(name => [
      name,
      focusedTeamName === null || focusedTeamName === name,
    ]))
    const tooltipPoints: [number, number][][] = []
    const tooltipFormatter: TooltipComponentOption['formatter'] = (params) => {
      const entries = Array.isArray(params) ? params : [params]
      const point = entries[0]?.value
      const at = hoveredLine?.at ?? (Array.isArray(point) ? Number(point[0]) : Number.NaN)
      const heading = Number.isFinite(at) ? echarts.format.encodeHTML(new Date(at).toLocaleString(locale.value)) : ''
      const scoreText = (score: unknown) => echarts.format.encodeHTML(`${score} ${translate('common.label.pts.scoreTrendChart')}`)
      if (hoveredLine) {
        const { seriesIndex } = hoveredLine
        const points = tooltipPoints[seriesIndex] ?? []
        let low = 0
        let high = points.length
        while (low < high) {
          const middle = Math.floor((low + high) / 2)
          if (points[middle]![0] <= at) low = middle + 1
          else high = middle
        }
        const marker = echarts.format.getTooltipMarker(palette[seriesIndex % Math.max(1, palette.length)] ?? '')
        const name = echarts.format.encodeHTML(teamNames[seriesIndex] ?? '')
        return `${heading}<br/>${marker}${name} <strong>${scoreText(points[Math.max(0, low - 1)]?.[1] ?? 0)}</strong>`
      }
      return [heading, ...entries.map(entry => {
        const marker = typeof entry.marker === 'string' ? entry.marker : ''
        const name = echarts.format.encodeHTML(entry.seriesName ?? '')
        const score = Array.isArray(entry.value) ? entry.value[1] : entry.value
        return `${marker}${name} <strong>${scoreText(score)}</strong>`
      })].join('<br/>')
    }
    return {
      backgroundColor: 'transparent',
      textStyle: { fontFamily },
      color: palette,
      title: props.title
        ? {
            text: props.title,
            left: 'center',
            textStyle: { fontSize: 15, fontWeight: 600, color: foreground },
          }
        : undefined,
      grid: { left: 64, right: 32, top: props.title ? 44 : 24, bottom: 88 },
      tooltip: {
        ...chartTooltipTheme(el.value),
        trigger: 'axis',
        appendTo: 'body',
        confine: true,
        className: 'noctf-chart-tooltip',
        formatter: tooltipFormatter,
        axisPointer: {
          type: 'line',
          lineStyle: { color: primary, width: 1, type: 'dashed', opacity: 0.7 },
        },
      },
      legend: {
        bottom: 36,
        type: 'scroll',
        itemGap: 16,
        hoverLink: true,
        selectedMode: 'multiple',
        selected: selectedTeams,
        inactiveColor: muted,
        textStyle: { color: foreground },
      },
      toolbox: {
        top: 4,
        right: 32,
        itemGap: 14,
        iconStyle: { borderColor: foreground },
        emphasis: { iconStyle: { borderColor: primary } },
        feature: {
          saveAsImage: { title: translate("leaderboard.label.downloadImage") },
          dataZoom: { title: { zoom: translate("leaderboard.label.areaZoom"), back: translate("leaderboard.label.zoomRestore") }, xAxisIndex: 0, yAxisIndex: 0, filterMode: 'none' },
          restore: { title: translate("leaderboard.label.restore") },
        },
      },
      xAxis: {
        type: 'time',
        min: timeRange.axisMin,
        max: timeRange.axisMax,
        axisPointer: { snap: false, triggerEmphasis: false },
        axisLine: { lineStyle: { color: border } },
        axisTick: { lineStyle: { color: border } },
        axisLabel: { hideOverlap: true, color: muted },
      },
      yAxis: {
        type: 'value',
        name: translate("leaderboard.label.score.scoreTrendChart"),
        nameTextStyle: { color: muted },
        axisLine: { lineStyle: { color: border } },
        axisTick: { lineStyle: { color: border } },
        axisLabel: { color: muted },
        splitLine: { lineStyle: { color: border, opacity: 0.45, type: 'dashed' } },
      },
      dataZoom: [
        {
          id: 'score-trend-time-slider',
          type: 'slider',
          xAxisIndex: 0,
          bottom: 4,
          height: 24,
          filterMode: 'none',
          ...zoomWindow,
        },
        {
          id: 'score-trend-time-inside',
          type: 'inside',
          xAxisIndex: 0,
          filterMode: 'none',
          zoomOnMouseWheel: 'ctrl',
          moveOnMouseWheel: false,
          moveOnMouseMove: false,
          ...zoomWindow,
        },
        {
          id: 'score-trend-score-inside',
          type: 'inside',
          yAxisIndex: 0,
          filterMode: 'none',
          zoomOnMouseWheel: 'ctrl',
          moveOnMouseWheel: false,
          moveOnMouseMove: false,
          ...scoreZoom,
        },
      ],
      series: props.series.map((team, index) => {
        const raw = [...new Map((team.points ?? [])
          .filter((p) => p.at)
          .map((p) => [new Date(p.at!).getTime(), p.score ?? 0] as [number, number])
          .filter(([at]) => Number.isFinite(at))).entries()]
          .sort((left, right) => left[0] - right[0])
        const data: [number, number][] = []
        if (!raw.length) {
          data.push([timeRange.start, 0], [timeRange.end, 0])
        }
        else {
          if (raw[0]![0] > timeRange.start)
            data.push([timeRange.start, 0])
          data.push(...raw)
          if (raw[raw.length - 1]![0] < timeRange.end)
            data.push([timeRange.end, raw[raw.length - 1]![1]])
        }
        tooltipPoints.push(data)
        const lineColor = palette[index % Math.max(1, palette.length)]
        return {
          id: team.teamId || team.teamName || `team-${index}`,
          type: 'line' as const,
          colorBy: 'series' as const,
          name: team.teamName ?? translate("common.label.team"),
          step: 'end' as const,
          triggerEvent: 'line' as const,
          showSymbol: false,
          symbol: 'circle',
          symbolSize: 7,
          lineStyle: {
            color: lineColor,
            width: focusedTeamName ? 3.2 : 2.4,
            opacity: 1,
            shadowBlur: focusedTeamName ? 16 : 0,
            shadowColor: focusedTeamName ? lineColor : 'transparent',
          },
          itemStyle: { color: lineColor, borderColor: foreground, borderWidth: 1 },
          emphasis: {
            focus: 'series' as const,
            lineStyle: { width: 4, opacity: 1, shadowBlur: 18, shadowColor: lineColor },
            itemStyle: { opacity: 1 },
          },
          blur: { lineStyle: { opacity: 0.14 }, itemStyle: { opacity: 0.14 } },
          animationDurationUpdate: 420,
          data,
        }
      }),
    }
  }

  function applyRender() {
    if (!chart) return
    clearHoveredLine()
    const seriesSignature = props.series
      .map((team, index) => team.teamId || team.teamName || `team-${index}`)
      .join('\u0000')
    const replaceSeries = lastSeriesSignature !== '' && lastSeriesSignature !== seriesSignature
    lastSeriesSignature = seriesSignature
    applyingOption = true
    try {
      chart.setOption(buildOption(), {
        notMerge: false,
        lazyUpdate: true,
        ...(replaceSeries ? { replaceMerge: ['series'] } : {}),
      })
    }
    finally {
      applyingOption = false
    }
  }

  function scheduleRender() {
    if (!chart || renderFrame) return
    renderFrame = requestAnimationFrame(() => {
      renderFrame = 0
      applyRender()
    })
  }

  onMounted(() => {
    chart = echarts.init(el.value!)
    chart.on('datazoom', rememberZoom)
    chart.on('restore', restoreChartView)
    chart.on('legendselectchanged', toggleTeamFocus)
    chart.on('mousemove', 'series.line', rememberHoveredLine)
    chart.on('mouseout', 'series.line', clearHoveredLine)
    scheduleRender()
  })

  watch(
    () => [props.revision, props.rangeStart, props.rangeEnd, props.title, locale.value],
    scheduleRender,
  )

  watch(isDark, () => void nextTick(scheduleRender))

  let observer: ResizeObserver | null = null

  let resizeFrame = 0

  let observedWidth = -1

  let observedHeight = -1

  onMounted(() => {
    observer = new ResizeObserver((entries) => {
      const size = entries[0]?.contentRect
      if (!size) return
      const width = Math.ceil(size.width)
      const height = Math.ceil(size.height)
      if (width === observedWidth && height === observedHeight) return
      observedWidth = width
      observedHeight = height
      if (resizeFrame) cancelAnimationFrame(resizeFrame)
      resizeFrame = requestAnimationFrame(() => {
        resizeFrame = 0
        chart?.resize()
      })
    })
    if (el.value) observer.observe(el.value)
  })

  onUnmounted(() => {
    observer?.disconnect()
    if (renderFrame) cancelAnimationFrame(renderFrame)
    if (resizeFrame) cancelAnimationFrame(resizeFrame)
    chart?.off('datazoom', rememberZoom)
    chart?.off('restore', restoreChartView)
    chart?.off('legendselectchanged', toggleTeamFocus)
    chart?.off('mousemove', rememberHoveredLine)
    chart?.off('mouseout', clearHoveredLine)
    chart?.dispose()
    chart = null
  })

  function setElRef(element: Element | ComponentPublicInstance | null) { el.value = element as typeof el.value }

  return {
      ...toRefs(props),
      el,
      setElRef,
      releaseWheelToScrollSurface,
    }
}

export type ScoreTrendChartViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useScoreTrendChart>>>
