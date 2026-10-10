import { describe, expect, test } from 'bun:test'
import { computed, effectScope, nextTick, reactive, ref, shallowRef, watch } from 'vue'
import * as geometry from '../app/components/ui/pdf-preview/pdf-page-window'

const portrait = { width: 600, height: 800 }
const landscape = { width: 800, height: 450 }

describe('continuous PDF reading geometry', () => {
  test('a thousand-page document has a bounded reading window at the beginning, middle and end', () => {
    const pages = geometry.createPdfLayout(1000, new Map(), portrait, 832, 1)
    for (const number of [1, 500, 1000]) {
      const window = geometry.pdfReadingWindow(pages, pages[number - 1]!.top, 900)
      expect(window.length).toBeLessThanOrEqual(geometry.maximumPdfCanvases)
      expect(window.some(page => page.number === number)).toBeTrue()
    }
    expect(geometry.pdfReadingWindow(pages, 0, 900).map(page => page.number)).toEqual([1, 2])
  })

  test('zoom, resize and newly discovered landscape pages preserve the same reading point', () => {
    const old = geometry.createPdfLayout(100, new Map(), portrait, 832, 1)
    const top = old[49]!.top + old[49]!.height * 0.4 - geometry.pdfPageGap
    const anchor = geometry.capturePdfAnchor(old, top)
    expect(anchor.page).toBe(50)
    expect(anchor.ratio).toBeCloseTo(0.4)
    for (const [width, zoom] of [[832, 3], [420, 0.5]]) {
      const changed = geometry.createPdfLayout(100, new Map([[49, landscape], [50, landscape]]), portrait, width!, zoom!)
      const restored = geometry.capturePdfAnchor(changed, geometry.restorePdfAnchor(changed, anchor))
      expect(restored.page).toBe(50)
      expect(restored.ratio).toBeCloseTo(0.4)
    }
  })

  test('large pages at high zoom keep canvas memory and dimensions bounded', () => {
    for (const size of [{ width: 5000, height: 9000 }, { width: 80, height: 200_000 }]) {
      const pixels = geometry.pdfCanvasSize(size, 3)
      expect(pixels.width * pixels.height).toBeLessThanOrEqual(4_000_000)
      expect(Math.max(pixels.width, pixels.height)).toBeLessThanOrEqual(8192)
      expect(pixels.scale).toBeGreaterThan(0)
    }
  })
})

const source = await Bun.file(new URL('../app/components/ui/pdf-preview/usePdfPreview.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
  .replace(/export function /g, 'function ')
  .replace(/import\(["']pdfjs-dist["']\)/, 'Promise.resolve(testLibrary)')
  .replace('import.meta.client', 'true')

class TestCanvas {
  width = 0
  height = 0
  getContext() { return {} }
}

function harness(options: { slow?: boolean; failPage?: number } = {}) {
  const calls: Array<{ source: string; number: number }> = []
  const cleanup: number[] = []
  const destroyed: string[] = []
  const canvases = new Map<number, TestCanvas>()
  const cancellations: number[] = []
  const pending = new Set<() => void>()
  const frames = new Map<number, () => void>()
  let frame = 0
  let active = 0
  let maximumActive = 0
  let cleanupWhileRendering = false
  let unmount!: () => void
  let failPage = options.failPage
  const scroll = { scrollTop: 0, scrollLeft: 0, clientWidth: 832, clientHeight: 900 }
  let resize!: () => void
  const props = reactive({ source: '/first.pdf', loading: false })
  const library = {
    GlobalWorkerOptions: {},
    getDocument: ({ url }: { url: string }) => {
      const document = {
        numPages: 1000,
        async getPage(number: number) {
          calls.push({ source: url, number })
          if (number === failPage) { failPage = undefined; throw new Error('Temporary page failure') }
          let rendering = false
          const size = number === 150 ? landscape : portrait
          return {
            getViewport: ({ scale }: { scale: number }) => ({ width: size.width * scale, height: size.height * scale }),
            getTextContent: async () => ({ items: [{ str: `Page ${number}` }] }),
            cleanup: () => { cleanup.push(number); cleanupWhileRendering ||= rendering },
            render: () => {
              active++
              maximumActive = Math.max(maximumActive, active)
              rendering = true
              let done = false
              let resolve!: () => void
              let reject!: (error: Error) => void
              const promise = new Promise<void>((yes, no) => { resolve = yes; reject = no })
              const finish = () => {
                if (done) return
                done = true
                active--
                rendering = false
                pending.delete(finish)
                resolve()
              }
              pending.add(finish)
              if (!options.slow) queueMicrotask(finish)
              return { promise, cancel: () => {
                if (done) return
                done = true
                active--
                rendering = false
                cancellations.push(number)
                pending.delete(finish)
                const error = new Error('Cancelled')
                error.name = 'RenderingCancelledException'
                reject(error)
              } }
            },
          }
        },
      }
      return { promise: Promise.resolve(document), destroy: async () => { destroyed.push(url) } }
    },
  }
  const deps = { ...geometry, computed, nextTick, ref, shallowRef, watch, testLibrary: library, pdfWorkerUrl: '/worker',
    onBeforeUnmount: (callback: () => void) => { unmount = callback },
    window: { devicePixelRatio: 2 }, HTMLCanvasElement: TestCanvas,
    ResizeObserver: class { constructor(callback: () => void) { resize = callback } observe() {} disconnect() {} },
    requestAnimationFrame: (callback: () => void) => { frames.set(++frame, callback); return frame },
    cancelAnimationFrame: (id: number) => { frames.delete(id) },
  }
  const factory = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return usePdfPreview;`)(deps)
  const scope = effectScope()
  const state = scope.run(() => factory(props))!
  state.viewportHost.value = { querySelector: () => scroll }
  const drain = async () => {
    for (let cycle = 0; cycle < 25; cycle++) {
      await nextTick()
      for (const [id, callback] of frames) { frames.delete(id); callback() }
      const visible = new Set(state.visiblePages.value.map((page: { number: number }) => page.number))
      for (const number of canvases.keys()) if (!visible.has(number)) canvases.delete(number)
      for (const number of visible as Set<number>) {
        let canvas = canvases.get(number)
        if (!canvas) { canvas = new TestCanvas(); canvases.set(number, canvas) }
        state.setCanvas(number, canvas)
      }
      await Promise.resolve()
    }
  }
  const stop = async () => { unmount(); scope.stop(); await drain() }
  return { state, props, calls, cleanup, destroyed, canvases, scroll, drain, stop,
    resize: () => resize(), cancellations, finish: () => { for (const finish of pending) finish() },
    maximumActive: () => maximumActive, cleanupWhileRendering: () => cleanupWhileRendering }
}

describe('PDF rendering lifecycle', () => {
  test('loads only nearby pages, recycles old canvases, and returns to earlier pages', async () => {
    const app = harness()
    try {
      await app.drain()
      expect([...new Set(app.calls.map(call => call.number))]).toEqual([1, 2])
      expect(app.state.pageStates.value.get(1).done).toBeTrue()
      const oldCanvases = [...app.canvases.values()]
      const initial = geometry.createPdfLayout(1000, new Map(), portrait, 832, 1)
      app.scroll.scrollTop = initial[149]!.top
      app.state.onScroll()
      await app.drain()
      expect(app.state.pageNumber.value).toBe(150)
      expect(app.canvases.size).toBeLessThanOrEqual(geometry.maximumPdfCanvases)
      expect(oldCanvases.every(canvas => canvas.width === 0 && canvas.height === 0)).toBeTrue()
      expect(app.calls.length).toBeLessThan(12)
      expect(app.state.pageStates.value.get(150).done).toBeTrue()
      app.scroll.scrollTop = 0
      app.state.onScroll()
      await app.drain()
      expect(app.state.pageNumber.value).toBe(1)
      expect(app.state.pageStates.value.get(1).done).toBeTrue()
      expect(app.maximumActive()).toBe(1)
      expect(app.cleanupWhileRendering()).toBeFalse()
    }
    finally { await app.stop() }
  })

  test('keeps the reading point through zoom and viewport resize, and page navigation scrolls across boundaries', async () => {
    const app = harness()
    try {
      await app.drain()
      app.state.nextPage()
      await app.drain()
      expect(app.state.pageNumber.value).toBe(2)
      app.scroll.scrollTop += 200
      app.state.onScroll()
      await app.drain()
      const before = app.scroll.scrollTop
      app.state.zoomIn()
      await app.drain()
      expect(app.state.zoomPercent.value).toBe(125)
      expect(app.state.pageNumber.value).toBe(2)
      expect(app.scroll.scrollTop).toBeCloseTo((before - 16) * 1.25 + 16)
      app.scroll.clientWidth = 432
      app.resize()
      await app.drain()
      expect(app.state.pageNumber.value).toBe(2)
      expect(app.state.pageStates.value.get(2).done).toBeTrue()
      app.state.previousPage()
      await app.drain()
      expect(app.scroll.scrollTop).toBe(0)
    }
    finally { await app.stop() }
  })

  test('cancels an obsolete render on source replacement and unmount, without overlapping canvas renders', async () => {
    const app = harness({ slow: true })
    await app.drain()
    app.props.source = '/second.pdf'
    await app.drain()
    expect(app.cancellations).toContain(1)
    expect(app.destroyed).toContain('/first.pdf')
    expect(app.maximumActive()).toBe(1)
    const liveCanvases = [...app.canvases.values()]
    await app.stop()
    expect(app.destroyed).toContain('/second.pdf')
    expect(liveCanvases.every(canvas => canvas.width === 0)).toBeTrue()
    expect(app.cleanupWhileRendering()).toBeFalse()
  })

  test('a failed page can be retried while the document and adjacent pages stay available', async () => {
    const app = harness({ failPage: 2 })
    try {
      await app.drain()
      expect(app.state.pageStates.value.get(1).done).toBeTrue()
      expect(app.state.pageStates.value.get(2).failed).toBeTrue()
      expect(app.state.internalError.value).toBeFalse()
      app.state.retryPage(2)
      await app.drain()
      expect(app.state.pageStates.value.get(2).done).toBeTrue()
    }
    finally { await app.stop() }
  })
})
