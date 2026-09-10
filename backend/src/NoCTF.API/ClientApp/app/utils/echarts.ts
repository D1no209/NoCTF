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
import { themeColor } from './theme-color'

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

const trendChartColorProperties = [
  '--trend-chart-1', '--trend-chart-2', '--trend-chart-3', '--trend-chart-4', '--trend-chart-5',
  '--trend-chart-6', '--trend-chart-7', '--trend-chart-8', '--trend-chart-9', '--trend-chart-10',
]

/** 从当前主题读取队伍配色，确保 Canvas 图表随明暗模式同步。 */
export function chartPalette(element?: Element | null): string[] {
  if (typeof getComputedStyle === 'undefined') return []
  return chartColorProperties
    .map(property => themeColor(property, element ?? undefined))
}

/** High-chroma colors reserved for dense multi-team trend lines. */
export function trendChartPalette(element?: Element | null): string[] {
  if (typeof getComputedStyle === 'undefined') return []
  return trendChartColorProperties
    .map(property => themeColor(property, element ?? undefined))
}

/** All chart overlays use the same opaque surface, border and typography as UI popovers. */
export function chartTooltipTheme(element?: Element | null) {
  return {
    backgroundColor: themeColor('--popover', element ?? undefined),
    borderColor: themeColor('--border', element ?? undefined),
    textStyle: { color: themeColor('--popover-foreground', element ?? undefined), fontFamily: element ? getComputedStyle(element).fontFamily : undefined },
  }
}
