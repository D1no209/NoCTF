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
  let renderer: Renderer
  class Renderer {
    domElement = new Element(); shadowMap = { enabled: false, autoUpdate: false }; pixelRatio = 0; disposed = false
    info = { memory: { geometries: 0, textures: 0 }, render: { calls: 0, triangles: 0 } }
    constructor() { renderer = this }
    setPixelRatio(value: number) { this.pixelRatio = value }
    setClearColor() {} setSize() {} render() { this.info.render.calls++ } dispose() { this.disposed = true }
  }
  const deps = { THREE: { ...Three, WebGLRenderer: Renderer }, themeColor: () => '#aabbcc', createSpectatorRibbon, createTerrain, terrainHeight, setFlagTransfer,
    document, devicePixelRatio: 3, ResizeObserver: class { observe() {} disconnect() {} },
    requestAnimationFrame: (callback: FrameRequestCallback) => { frames.set(++id, callback); return id }, cancelAnimationFrame: (key: number) => frames.delete(key) }
  const Scene = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return SpectatorScene;`)(deps)
  const scene = new Scene(element, () => { lost = true }) as SpectatorScene
  scene.update({ mode: 'Awd', nodes: [{ id: 'a', name: 'A', services: ['Healthy'] }, { id: 'b', name: 'B', services: ['Down'] }],
    focus: '', reduced: false, fps: 60, locked: true })
  return { scene, document, frames, element, renderer: renderer!, lost: () => lost,
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

test('DPR is bounded, context loss requests fallback, disposal releases event listeners and renderer', () => {
  const h = harness(); expect(h.renderer.pixelRatio).toBe(1.5)
  let prevented = false
  h.renderer.domElement.events.get('webglcontextlost')!({ preventDefault() { prevented = true } })
  expect(prevented && h.lost()).toBe(true)
  h.scene.dispose()
  expect(h.frames.size).toBe(0); expect(h.document.events.size).toBe(0)
  expect(h.element.events.size).toBe(0); expect(h.renderer.disposed).toBe(true)
})
