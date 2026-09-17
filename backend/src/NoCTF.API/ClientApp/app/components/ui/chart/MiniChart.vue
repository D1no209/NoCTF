<script setup lang="ts">
import { chartPalette, chartTooltipTheme, echarts } from '~/utils/echarts'

const props = withDefaults(
  defineProps<{
    option: echarts.EChartsCoreOption
    height?: string
  }>(),
  { height: '260px' },
)

const el = ref<HTMLElement | null>(null)
const { isDark } = useTheme()
let chart: echarts.ECharts | null = null
let resizeFrame = 0
let observedWidth = -1
let observedHeight = -1

function render() {
  if (!chart || !el.value) return
  const foreground = getComputedStyle(el.value).color
  chart.setOption(
    {
      backgroundColor: 'transparent',
      color: chartPalette(el.value),
      textStyle: { color: foreground },
      ...props.option,
      tooltip: Array.isArray(props.option.tooltip)
        ? props.option.tooltip.map(option => ({ ...option, ...chartTooltipTheme(el.value) }))
        : { ...(props.option.tooltip ?? {}), ...chartTooltipTheme(el.value) },
    },
    { notMerge: true },
  )
}

onMounted(() => {
  chart = echarts.init(el.value!)
  render()
})

watch(() => props.option, render, { deep: true })
watch(isDark, () => void nextTick(render))

let observer: ResizeObserver | null = null
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
  if (resizeFrame) cancelAnimationFrame(resizeFrame)
  chart?.dispose()
  chart = null
})
</script>

<template>
  <div ref="el" class="w-full text-foreground" :style="{ height }" />
</template>
