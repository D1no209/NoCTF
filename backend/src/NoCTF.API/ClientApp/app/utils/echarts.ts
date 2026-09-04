import * as echarts from 'echarts/core'
import { LineChart, PieChart, RadarChart } from 'echarts/charts'
import {
  DataZoomComponent,
  GridComponent,
  LegendComponent,
  TitleComponent,
  ToolboxComponent,
  TooltipComponent,
} from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'

echarts.use([
  LineChart,
  PieChart,
  RadarChart,
  GridComponent,
  LegendComponent,
  TitleComponent,
  ToolboxComponent,
  TooltipComponent,
  DataZoomComponent,
  CanvasRenderer,
])

export { echarts }

const chartColorProperties = [
  '--chart-1', '--chart-2', '--chart-3', '--chart-4', '--chart-5',
  '--chart-6', '--chart-7', '--chart-8', '--chart-9', '--chart-10',
]

/** 从当前主题读取队伍配色，确保 Canvas 图表随明暗模式同步。 */
export function chartPalette(element?: Element | null): string[] {
  if (typeof getComputedStyle === 'undefined') return []
  const styles = getComputedStyle(element ?? document.documentElement)
  return chartColorProperties
    .map(property => styles.getPropertyValue(property).trim())
    .filter(Boolean)
}
