<script setup lang="ts">
import { echarts, CHART_PALETTE } from '~/utils/echarts'

const props = withDefaults(
  defineProps<{
    option: echarts.EChartsCoreOption
    height?: string
  }>(),
  { height: '260px' },
)

const el = ref<HTMLElement | null>(null)
let chart: echarts.ECharts | null = null

function render() {
  if (!chart || !el.value) return
  const foreground = getComputedStyle(el.value).color
  chart.setOption(
    {
      backgroundColor: 'transparent',
      color: CHART_PALETTE,
      textStyle: { color: foreground },
      ...props.option,
    },
    { notMerge: true },
  )
}

onMounted(() => {
  chart = echarts.init(el.value!)
  render()
})

watch(() => props.option, render, { deep: true })

let observer: ResizeObserver | null = null
onMounted(() => {
  observer = new ResizeObserver(() => chart?.resize())
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
