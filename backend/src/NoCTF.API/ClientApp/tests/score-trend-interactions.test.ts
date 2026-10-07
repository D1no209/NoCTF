import { afterEach, beforeEach, describe, expect, spyOn, test } from 'bun:test'
import { effectScope, nextTick, reactive, ref, watch } from 'vue'
import { SVGRenderer } from 'echarts/renderers'
import { echarts } from '../app/utils/echarts'
import { useScoreTrendChart } from '../app/features/leaderboard/useScoreTrendChart'

echarts.use(SVGRenderer)

const globalNames = ['ref', 'watch', 'nextTick', 'useLocale', 'useTheme', 'translate', 'onMounted', 'onUnmounted', 'requestAnimationFrame', 'cancelAnimationFrame', 'ResizeObserver']
const originalGlobals = new Map(globalNames.map(name => [name, Object.getOwnPropertyDescriptor(globalThis, name)]))
const globals = globalThis as unknown as Record<string, unknown>
const timestamp = (hour: number) => Date.UTC(2026, 9, 8, hour)
const mounts: Array<() => void> = []
const unmounts: Array<() => void> = []
const frames = new Map<number, (time: number) => void>()
let frameId = 0
let chart: echarts.ECharts
let initSpy: ReturnType<typeof spyOn>
let renderSpy: ReturnType<typeof spyOn>
let scope: ReturnType<typeof effectScope>

beforeEach(() => {
  mounts.length = unmounts.length = 0
  frames.clear()
  globals.ref = ref
  globals.watch = watch
  globals.nextTick = nextTick
  globals.useLocale = () => ({ locale: ref('en') })
  globals.useTheme = () => ({ isDark: ref(false) })
  globals.translate = (key: string) => key
  globals.onMounted = (callback: () => void) => { mounts.push(callback) }
  globals.onUnmounted = (callback: () => void) => { unmounts.push(callback) }
  globals.requestAnimationFrame = (callback: (time: number) => void) => {
    frames.set(++frameId, callback)
    return frameId
  }
  globals.cancelAnimationFrame = (id: number) => { frames.delete(id) }
  globals.ResizeObserver = class { observe() {} disconnect() {} }
  chart = echarts.init(null, undefined, { renderer: 'svg', ssr: true, width: 800, height: 320 })
  chart.setOption({ animation: false })
  initSpy = spyOn(echarts, 'init').mockReturnValue(chart)
  const setOption = chart.setOption.bind(chart)
  renderSpy = spyOn(chart, 'setOption').mockImplementation((option, settings) => {
    setOption(option, { ...(typeof settings === 'object' ? settings : {}), lazyUpdate: false })
  })
  scope = effectScope()
})

afterEach(() => {
  for (const unmount of unmounts) unmount()
  scope.stop()
  renderSpy.mockRestore()
  initSpy.mockRestore()
  if (!chart.isDisposed()) chart.dispose()
  for (const [key, descriptor] of originalGlobals) {
    if (descriptor) Object.defineProperty(globalThis, key, descriptor)
    else delete globals[key]
  }
})

function flushFrames() {
  for (const [id, callback] of frames) {
    frames.delete(id)
    callback(0)
  }
}

function mountChart() {
  const props = reactive({
    title: '', revision: 1, height: '320px',
    rangeStart: new Date(timestamp(10)).toISOString(),
    rangeEnd: new Date(timestamp(14)).toISOString(),
    series: [
      { teamId: 'a', teamName: 'Alpha', points: [
        { at: new Date(timestamp(10)).toISOString(), score: 10 },
        { at: new Date(timestamp(14)).toISOString(), score: 100 },
      ] },
      { teamId: 'b', teamName: 'Beta', points: [
        { at: new Date(timestamp(12)).toISOString(), score: 70 },
        { at: new Date(timestamp(14)).toISOString(), score: 150 },
      ] },
    ],
  })
  scope.run(() => useScoreTrendChart(props))
  for (const mount of mounts) mount()
  flushFrames()
  return props
}

function zoomWindows() {
  return chart.getOption().dataZoom as Array<{
    id: string, type: string, start: number, end: number, startValue: number, endValue: number, zoomOnMouseWheel?: boolean | string,
  }>
}

describe('score trend interactions', () => {
  test('zooms both axes with Ctrl and retains both windows after a data refresh', async () => {
    const props = mountChart()
    expect(zoomWindows().find(zoom => zoom.id === 'score-trend-time-inside')?.zoomOnMouseWheel).toBe('ctrl')
    const originalTime = chart.convertFromPixel({ xAxisIndex: 0 }, 64)
    chart.getZr().handler.dispatch('mousewheel', {
      zrX: 400, zrY: 110, zrDelta: 1, ctrlKey: true, preventDefault() {}, stopPropagation() {},
    })
    expect(zoomWindows().find(zoom => zoom.id === 'score-trend-time-slider')?.start).toBeGreaterThan(0)
    expect(zoomWindows().find(zoom => zoom.id === 'score-trend-score-inside')?.start).toBeGreaterThan(0)
    chart.dispatchAction({ type: 'dataZoom', batch: [
      { dataZoomId: 'score-trend-time-inside', start: 25, end: 75 },
      { dataZoomId: 'score-trend-score-inside', start: 20, end: 80 },
    ] })
    expect(chart.convertFromPixel({ xAxisIndex: 0 }, 64)).toBeGreaterThan(originalTime)
    props.revision++
    await nextTick()
    flushFrames()
    expect(zoomWindows().find(zoom => zoom.id === 'score-trend-time-slider')?.start).toBeCloseTo(25)
    expect(zoomWindows().find(zoom => zoom.id === 'score-trend-time-slider')?.end).toBeCloseTo(75)
    expect(zoomWindows().find(zoom => zoom.id === 'score-trend-score-inside')?.start).toBeCloseTo(20)
    expect(zoomWindows().find(zoom => zoom.id === 'score-trend-score-inside')?.end).toBeCloseTo(80)
  })

  test('retains absolute toolbox time selection while focusing a team and restores the full chart', () => {
    mountChart()
    const selection = chart.getModel().findComponents({ mainType: 'dataZoom' }).find(component => component.subType === 'select')!
    chart.dispatchAction({ type: 'dataZoom', dataZoomId: selection.id, startValue: timestamp(11), endValue: timestamp(13) })
    chart.dispatchAction({ type: 'legendToggleSelect', name: 'Alpha' })
    flushFrames()
    expect(zoomWindows().find(zoom => zoom.id === 'score-trend-time-slider')?.startValue).toBe(timestamp(11))
    expect(zoomWindows().find(zoom => zoom.id === 'score-trend-time-slider')?.endValue).toBe(timestamp(13))
    chart.dispatchAction({ type: 'restore' })
    flushFrames()
    expect(zoomWindows().find(zoom => zoom.id === 'score-trend-time-slider')?.start).toBe(0)
    expect(zoomWindows().find(zoom => zoom.id === 'score-trend-time-slider')?.end).toBe(100)
    expect((chart.getOption().legend as Array<{ selected: Record<string, boolean> }>)[0]?.selected).toEqual({ Alpha: true, Beta: true })
  })

  test('does not highlight another team merely because its timestamp is closest to the hovered line', () => {
    mountChart()
    const highlights: unknown[] = []
    chart.on('highlight', event => { highlights.push(event) })
    const [x, y] = chart.convertToPixel({ seriesIndex: 0 }, [timestamp(12) + 30 * 60_000, 10])
    chart.dispatchAction({ type: 'updateAxisPointer', x, y })
    expect(highlights).toHaveLength(0)
    const pointer = (chart.getOption().xAxis as Array<{ axisPointer: { value: number } }>)[0]!.axisPointer
    expect(pointer.value).toBeCloseTo(timestamp(12) + 30 * 60_000, 0)
    const target = chart.getZr().handler.findHover(x, y).target
    expect(target?.type).toBe('ec-polyline')
    chart.getZr().handler.dispatch('mousemove', { zrX: x, zrY: y })
    expect(target?.hoverState).toBe(2)
    const tooltip = (chart.getOption().tooltip as Array<{ formatter: (params: unknown) => string }>)[0]!
    const nearestOtherTeam = chart.getModel().getSeriesByIndex(1).getDataParams(0)
    const content = tooltip.formatter(nearestOtherTeam)
    expect(content).toContain('Alpha')
    expect(content).toContain('10 common.label.pts.scoreTrendChart')
    expect(content).not.toContain('Beta')
    chart.getZr().handler.dispatch('mousemove', { zrX: x, zrY: y - 12 })
    expect(tooltip.formatter(nearestOtherTeam)).toContain('Beta')
    nearestOtherTeam.seriesName = '<b>Beta</b>'
    expect(tooltip.formatter(nearestOtherTeam)).toContain('&lt;b&gt;Beta&lt;/b&gt;')
  })
})
