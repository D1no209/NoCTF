import { expect, test } from 'bun:test'
import * as Three from 'three'
import { createSpectatorRibbon } from '../app/motion/spectator-ribbon'
import { createTerrain, terrainHeight } from '../app/lib/spectator-terrain'
import { setFlagTransfer } from '../app/motion/spectator-flag'
import type { SpectatorScene } from '../app/lib/spectator-scene'

const source = await Bun.file(new URL('../app/lib/spectator-scene.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export class /g, 'class ')
class Element {
  clientWidth = 1200; clientHeight = 600; dataset: Record<string, unknown> = {}; style = {}; hidden = false
  events = new Map<string, (event: unknown) => void>()
  addEventListener(key: string, fn: (event: unknown) => void) { this.events.set(key, fn) }
  removeEventListener(key: string) { this.events.delete(key) }
  append() {} remove() {} replaceChildren() {} setPointerCapture() {}
}
function harness() {
  const element = new Element(), document = Object.assign(new Element(), { hidden: false, createElement: () => new Element() })
  const frames = new Map<number, FrameRequestCallback>(); let id = 0, lost = false
  const queries: Element[] = []
  let resized = () => {}
  const window = { devicePixelRatio: 3, matchMedia: () => { const query = new Element(); queries.push(query); return query } }
  let renderer: Renderer
  class Renderer {
    domElement = new Element(); shadowMap = { enabled: false, autoUpdate: false }; pixelRatio = 0; disposed = false
    capabilities = { maxTextureSize: 8192 }
    width = 0; height = 0
    info = { memory: { geometries: 0, textures: 0 }, render: { calls: 0, triangles: 0 } }
    constructor() { renderer = this }
    setPixelRatio(value: number) { this.pixelRatio = value }
    getPixelRatio() { return this.pixelRatio }
    setClearColor() {} setSize(w: number, h: number) { this.width = Math.floor(w * this.pixelRatio); this.height = Math.floor(h * this.pixelRatio) }
    render() { this.info.render.calls++ } dispose() { this.disposed = true }
  }
  const deps = { THREE: { ...Three, WebGLRenderer: Renderer }, themeColor: () => '#aabbcc', createSpectatorRibbon, createTerrain, terrainHeight, setFlagTransfer,
    document, window, ResizeObserver: class { constructor(callback: () => void) { resized = callback } observe() {} disconnect() {} },
    requestAnimationFrame: (callback: FrameRequestCallback) => { frames.set(++id, callback); return id }, cancelAnimationFrame: (key: number) => frames.delete(key) }
  const Scene = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return SpectatorScene;`)(deps)
  const scene = new Scene(element, () => { lost = true }) as SpectatorScene
  scene.update({ mode: 'Awd', nodes: [{ id: 'a', name: 'A', services: ['Healthy'] }, { id: 'b', name: 'B', services: ['Down'] }],
    focus: '', reduced: false, fps: 60, locked: true })
  return { scene, document, window, queries, frames, element, renderer: renderer!, lost: () => lost,
    resize(w: number, h: number) { element.clientWidth = w; element.clientHeight = h; resized() },
    density(ratio: number) { window.devicePixelRatio = ratio; queries.at(-1)!.events.get('change')!({}) },
    pump(time: number) { const work = [...frames.values()]; frames.clear(); work.forEach(callback => callback(time)) } }
}

test('idle rendering stops and browser visibility clears animations before resuming without replay', () => {
  const h = harness(); h.pump(17)
  expect(h.frames.size).toBe(0)
  h.scene.enqueue(Array.from({ length: 60 }, (_, i) => ({ id: String(i), source: 'a', target: 'b', service: 0 })))
  h.pump(34)
  expect(h.scene.metrics().active).toBe(12); expect(h.scene.metrics().queued).toBe(18)
  const rendered = h.scene.metrics().frames
  h.document.hidden = true; h.document.events.get('visibilitychange')!({})
  expect(h.frames.size).toBe(0); expect(h.scene.metrics().active).toBe(0); expect(h.scene.metrics().queued).toBe(0)
  h.pump(60000); expect(h.scene.metrics().frames).toBe(rendered)
  h.document.hidden = false; h.document.events.get('visibilitychange')!({}); h.pump(60017)
  expect(h.scene.metrics().frames).toBe(rendered + 1); expect(h.frames.size).toBe(0)
  h.scene.dispose()
})

test('native density survives 4K resize and monitor changes without exceeding pixel or GPU limits', () => {
  const h = harness(); expect(h.renderer.pixelRatio).toBe(3)
  h.density(1); h.resize(2750, 1395)
  expect([h.renderer.width, h.renderer.height]).toEqual([2750, 1395])
  h.density(2)
  expect([h.renderer.width, h.renderer.height]).toEqual([5500, 2790])
  expect(h.queries.at(-2)!.events.size).toBe(0)
  h.resize(7680, 4320)
  expect(h.renderer.width * h.renderer.height).toBeLessThanOrEqual(16_777_216)
  h.resize(10000, 100)
  expect(h.renderer.width).toBeLessThanOrEqual(8192)
  h.resize(1200, 600)
  expect(h.renderer.pixelRatio).toBe(2)
  h.scene.dispose()
  expect(h.queries.every(query => query.events.size === 0)).toBe(true)
})

test('context loss requests fallback, disposal releases event listeners and renderer', () => {
  const h = harness()
  let prevented = false
  h.renderer.domElement.events.get('webglcontextlost')!({ preventDefault() { prevented = true } })
  expect(prevented && h.lost()).toBe(true)
  h.scene.dispose()
  expect(h.frames.size).toBe(0); expect(h.document.events.size).toBe(0)
  expect(h.element.events.size).toBe(0); expect(h.renderer.disposed).toBe(true)
})
