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

/** 队伍配色盘(截图风格的高饱和区分色)。 */
export const CHART_PALETTE = [
  '#f87171', '#34d399', '#fbbf24', '#a78bfa', '#22d3ee',
  '#fb923c', '#a3e635', '#f472b6', '#818cf8', '#38bdf8',
]
