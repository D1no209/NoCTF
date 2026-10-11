import { describe, expect, test } from 'bun:test'
import { effectScope, nextTick, reactive, ref, toRefs, watch } from 'vue'
import * as echarts from 'echarts'
import { scoreTrendTimeRange } from '../app/features/leaderboard/score-trend-range'
import { scoreTrendValueAt } from '../app/features/leaderboard/score-trend-tooltip'

const source = await Bun.file(new URL('../app/features/leaderboard/useScoreTrendChart.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
  .replace(/export function /g, 'function ')

describe('score trend pointer identity', () => {
  test('after rank reorder, the actual ECharts hit ID still selects the same team and score', async () => {
    const runtime = echarts.init(null, undefined, { renderer: 'svg', ssr: true, width: 900, height: 320 })
    const handlers = new Map<string, (...args: any[]) => void>()
    const frames: Array<() => void> = []
    const mounts: Array<() => void> = []
    let unmount = () => {}
    let action: any
    let at = Date.parse('2026-10-10T12:30:00Z')
    const chart = {
      setOption: (option: any, options: any) => runtime.setOption(option, { ...options, lazyUpdate: false }),
      getOption: () => runtime.getOption(),
      convertFromPixel: () => at,
      dispatchAction: (payload: any) => { action = payload },
      on: (name: string, ...args: any[]) => handlers.set(name, args.at(-1)),
      off: () => {}, dispose: () => runtime.dispose(),
      getZr: () => ({ on: (name: string, handler: () => void) => handlers.set(name, handler) }),
    }
    const globals = {
      ref, toRefs, watch, nextTick,
      useLocale: () => ({ locale: ref('en-US') }), useTheme: () => ({ isDark: ref(false) }),
      onMounted: (callback: () => void) => mounts.push(callback), onUnmounted: (callback: () => void) => { unmount = callback },
      requestAnimationFrame: (callback: () => void) => { frames.push(callback); return frames.length }, cancelAnimationFrame: () => {},
      ResizeObserver: class { observe() {} disconnect() {} },
      echarts: { ...echarts, init: () => chart },
      scoreTrendTimeRange, scoreTrendValueAt,
      chartTooltipTheme: () => ({}), trendChartPalette: () => ['red', 'blue'], themeColor: () => 'gray',
      translate: (key: string) => key.includes('pts') ? 'pts' : key,
    }
    const make = new Function(...Object.keys(globals), `${compiled}; return useScoreTrendChart`)(...Object.values(globals))
    const team = (id: string, score: number) => ({ teamId: id, teamName: `Team ${id}`, points: [
      { at: '2026-10-10T12:00:00Z', score }, { at: '2026-10-10T13:00:00Z', score: score + 10 },
    ] })
    const props = reactive({ title: '', height: '320px', rangeStart: '2026-10-10T11:00:00Z', rangeEnd: '2026-10-10T14:00:00Z', revision: 1, series: [team('A', 100), team('B', 200)] })
    const scope = effectScope()
    try {
      scope.run(() => make(props))
      mounts.forEach(mount => mount())
      while (frames.length) frames.shift()!()
      props.series = [team('B', 200), team('A', 300)]
      await nextTick()
      while (frames.length) frames.shift()!()
      const series = runtime.getOption().series as Array<{ id: string }>
      expect(series.map(item => item.id)).toEqual(['A', 'B'])
      handlers.get('mousemove')!({ seriesIndex: 0, event: { offsetX: 400, offsetY: 100 } })
      expect(action.type).toBe('showTip')
      expect(action.x).toBe(400)
      expect(action.y).toBe(100)
      expect(action.tooltip.formatter()).toContain('Team A')
      expect(action.tooltip.formatter()).toContain('300 pts')
      expect(action.tooltip.formatter()).not.toContain('Team B')
      at = Date.parse('2026-10-10T13:30:00Z')
      handlers.get('mousemove')!({ seriesIndex: 1, event: { offsetX: 600, offsetY: 130 } })
      expect(action.tooltip.formatter()).toContain('Team B')
      expect(action.tooltip.formatter()).toContain('210 pts')
      handlers.get('mouseout')!()
      expect(action.type).toBe('hideTip')
      handlers.get('globalout')!()
      expect(action.type).toBe('hideTip')
    }
    finally { unmount(); scope.stop() }
  })
  test('step scores do not jump early or leak future scores before the first event', () => {
    const points = [[10, 100], [20, 200], [30, 150]] as const
    expect(scoreTrendValueAt(points, 9)).toBe(0)
    expect(scoreTrendValueAt(points, 19)).toBe(100)
    expect(scoreTrendValueAt(points, 20)).toBe(200)
    expect(scoreTrendValueAt(points, 40)).toBe(150)
    expect(scoreTrendValueAt([], 40)).toBe(0)
  })
})
