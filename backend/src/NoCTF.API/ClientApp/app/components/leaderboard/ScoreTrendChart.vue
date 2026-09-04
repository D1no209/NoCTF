<script setup lang="ts">
import { chartPalette, echarts } from '~/utils/echarts'
import type { TrendSeries } from './types'

const props = withDefaults(
  defineProps<{
    title?: string
    series: TrendSeries[]
    /** 时间轴范围(ISO);没有数据的队伍画一条 0 分平线。 */
    rangeStart?: string | null
    rangeEnd?: string | null
    height?: string
  }>(),
  { title: '', rangeStart: null, rangeEnd: null, height: '400px' },
)

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
    legend: { bottom: 36, type: 'scroll', itemGap: 16, textStyle: { color: foreground } },
    toolbox: {
      right: 16,
      feature: {
        saveAsImage: { title: translate("下载为图片") },
        dataZoom: { title: { zoom: translate("区域缩放"), back: translate("缩放还原") }, yAxisIndex: 'none' },
        restore: { title: translate("还原") },
      },
    },
    xAxis: {
      type: 'time',
      axisLabel: { hideOverlap: true, color: foreground },
    },
    yAxis: {
      type: 'value',
      name: translate("分数"),
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
        name: team.teamName ?? translate('队伍'),
        step: 'end' as const,
        showSymbol: false,
        emphasis: { focus: 'series' as const },
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
</script>

<template>
  <div ref="el" class="w-full text-foreground" :style="{ height }" />
</template>
