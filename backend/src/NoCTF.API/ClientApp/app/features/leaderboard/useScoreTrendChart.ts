import { toRefs } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { chartPalette, echarts } from '../../utils/echarts'
import type { TrendSeries } from './types'

/** Owns state, effects and commands for ScoreTrendChart. */
export function useScoreTrendChart(props: Readonly<Omit<{
    title?: string
    series: TrendSeries[]
    /** 时间轴范围(ISO);没有数据的队伍画一条 0 分平线。 */
    rangeStart?: string | null
    rangeEnd?: string | null
    height?: string
  }, "title" | "rangeStart" | "rangeEnd" | "height"> & Required<Pick<{
    title?: string
    series: TrendSeries[]
    /** 时间轴范围(ISO);没有数据的队伍画一条 0 分平线。 */
    rangeStart?: string | null
    rangeEnd?: string | null
    height?: string
  }, "title" | "rangeStart" | "rangeEnd" | "height">>>) {
  const el = ref<HTMLElement | null>(null)

  const { locale } = useLocale()

  const { isDark } = useTheme()

  let chart: echarts.ECharts | null = null

  function buildOption(): echarts.EChartsCoreOption {
    const end = props.rangeEnd ?? new Date().toISOString()
    const start = props.rangeStart ?? end
    const foreground = el.value ? getComputedStyle(el.value).color : undefined
    return {
      backgroundColor: 'transparent',
      color: chartPalette(el.value),
      title: props.title
        ? {
            text: props.title,
            left: 'center',
            textStyle: { fontSize: 15, fontWeight: 600, color: foreground },
          }
        : undefined,
      grid: { left: 64, right: 32, top: props.title ? 44 : 24, bottom: 88 },
      tooltip: {
        trigger: 'axis',
        valueFormatter: (value: number | string) => `${value} pts`,
      },
      legend: {
        bottom: 36,
        type: 'scroll',
        itemGap: 16,
        hoverLink: false,
        selectedMode: false,
        textStyle: { color: foreground },
      },
      toolbox: {
        right: 16,
        feature: {
          saveAsImage: { title: translate("ui.downloadAsImage") },
          dataZoom: { title: { zoom: translate("ui.areaZoom"), back: translate("ui.zoomRestore") }, yAxisIndex: 'none' },
          restore: { title: translate("ui.restore") },
        },
      },
      xAxis: {
        type: 'time',
        axisLabel: { hideOverlap: true, color: foreground },
      },
      yAxis: {
        type: 'value',
        name: translate("ui.score"),
        nameTextStyle: { color: foreground },
        axisLabel: { color: foreground },
        splitLine: { lineStyle: { opacity: 0.35 } },
      },
      dataZoom: [
        { type: 'slider', bottom: 4, height: 24 },
        { type: 'inside' },
      ],
      series: props.series.map((team) => {
        const raw = (team.points ?? [])
          .filter((p) => p.at)
          .map((p) => [new Date(p.at!).getTime(), p.score ?? 0] as [number, number])
        const data: [number, number][] = raw.length
          ? [[new Date(start).getTime(), 0], ...raw, [new Date(end).getTime(), raw[raw.length - 1]![1]]]
          : [[new Date(start).getTime(), 0], [new Date(end).getTime(), 0]]
        return {
          type: 'line' as const,
          colorBy: 'series' as const,
          name: team.teamName ?? translate("ui.team"),
          step: 'end' as const,
          showSymbol: false,
          emphasis: { disabled: true },
          blur: { lineStyle: { opacity: 1 }, itemStyle: { opacity: 1 } },
          lineStyle: { width: 2 },
          data,
        }
      }),
    }
  }

  function render() {
    if (!chart) return
    chart.setOption(buildOption(), { notMerge: true })
  }

  onMounted(() => {
    chart = echarts.init(el.value!)
    render()
  })

  watch(() => [props.series, props.rangeStart, props.rangeEnd, props.title, locale.value], render, { deep: true })

  watch(isDark, () => void nextTick(render))

  const onResize = () => chart?.resize()

  let observer: ResizeObserver | null = null

  onMounted(() => {
    observer = new ResizeObserver(onResize)
    if (el.value) observer.observe(el.value)
  })

  onUnmounted(() => {
    observer?.disconnect()
    chart?.dispose()
    chart = null
  })

  function setElRef(element: Element | ComponentPublicInstance | null) { el.value = element as typeof el.value }

  return {
      ...toRefs(props),
      el,
      setElRef
    }
}

export type ScoreTrendChartViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useScoreTrendChart>>>
