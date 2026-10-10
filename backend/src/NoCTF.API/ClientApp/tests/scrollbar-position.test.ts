import { expect, test } from 'bun:test'

test('owned scrollbar enhancement preserves positioning and disposes its listeners', async () => {
  const source = await Bun.file(new URL('../app/components/ui/scroll-area/scrollbars.ts', import.meta.url)).text()
  const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
    .replace(/^import[^\n]*\n/gm, '')
    .replace(/^export /gm, '')
  class Style {
    values = new Map<string, string>()
    priorities = new Map<string, string>()
    getPropertyValue(key: string) { return this.values.get(key) ?? '' }
    getPropertyPriority(key: string) { return this.priorities.get(key) ?? '' }
    setProperty(key: string, value: string, priority = '') { this.values.set(key, value); this.priorities.set(key, priority) }
    removeProperty(key: string) { this.values.delete(key); this.priorities.delete(key) }
  }
  class Node {
    style = new Style()
    dataset = { scrollAxis: 'y' }
    enhanced = false
    listeners = new Set<string>()
    constructor(public position: string) {}
    querySelector() { return null }
    matches() { return false }
    addEventListener(name: string) { this.listeners.add(name) }
    removeEventListener(name: string) { this.listeners.delete(name) }
  }
  const options: unknown[] = []
  const deps = {
    getComputedStyle: (node: Node) => ({ position: node.style.getPropertyValue('position') || (node.enhanced ? 'relative' : node.position) }),
    OverlayScrollbars: (value: { target: Node }) => {
      const target = value.target
      target.enhanced = true
      return {
        options(value: unknown) { options.push(value) },
        destroy() { target.enhanced = false },
      }
    },
  }
  const create = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return createScrollbars;`)(deps)

  const dialog = new Node('fixed')
  const binding = create(dialog, 'y')
  expect(deps.getComputedStyle(dialog).position).toBe('fixed')
  expect(dialog.listeners).toEqual(new Set(['focusin', 'focusout']))
  binding.update('x')
  expect(options.at(-1)).toEqual({ overflow: { x: 'scroll', y: 'hidden' } })
  binding.dispose()
  expect(dialog.listeners.size).toBe(0)
  expect(dialog.style.getPropertyValue('position')).toBe('')
  expect(dialog.enhanced).toBe(false)
  expect(source).not.toContain('MutationObserver')
  expect(source).not.toContain('document.body')
})

test('pinned control stays outside vertical scrolling content and releases its host', async () => {
  const source = await Bun.file(new URL('../app/components/ui/scroll-area/scrollbars.ts', import.meta.url)).text()
  const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
    .replace(/^import[^\n]*\n/gm, '')
    .replace(/^export /gm, '')
  const style = () => {
    const values = new Map<string, string>()
    const writes: string[] = []
    return {
      writes,
      getPropertyValue: (key: string) => values.get(key) ?? '',
      getPropertyPriority: () => '',
      setProperty(key: string, value: string) { writes.push(key); values.set(key, value) },
      removeProperty(key: string) { values.delete(key) },
    }
  }
  const element = (rect: { top: number, right: number, bottom: number, left: number }) => {
    const listeners = new Map<string, (event?: { propertyName: string }) => void>()
    return {
      rect, listeners, style: style(), scrollTop: 0, scrollLeft: 0, geometryReads: 0,
      getBoundingClientRect() { this.geometryReads++; return { ...this.rect } },
      matches: () => false,
      querySelector: () => null,
      addEventListener(name: string, listener: (event?: { propertyName: string }) => void) { listeners.set(name, listener) },
      removeEventListener(name: string) { listeners.delete(name) },
    }
  }
  const host = element({ top: 180, right: 1920, bottom: 1050, left: 20 })
  const root = { ...element({ top: 290, right: 1900, bottom: 1010, left: 40 }), parentElement: host }
  const content = { ...element({ top: 290, right: 1900, bottom: 1800, left: 40 }), parentElement: root }
  const table = { ...element({ top: 420, right: 1880, bottom: 1600, left: 60 }), parentElement: content }
  let hidden = false
  let visibilityWrites = 0
  const bar = {
    style: style(), offsetHeight: 8, classList: { add() {} },
    get hidden() { return hidden },
    set hidden(value: boolean) { visibilityWrites++; hidden = value },
  }
  const frames = new Map<number, () => void>()
  const observed: unknown[] = []
  let resize: () => void = () => {}
  let destroyed = false
  let disconnected = 0
  let slot: unknown
  let frameId = 0
  const windowListeners = new Map<string, () => void>()
  const deps = {
    getComputedStyle: (node: typeof host) => ({ position: node.style.getPropertyValue('position') || 'static' }),
    OverlayScrollbars: (input: { scrollbars: { slot: unknown } }) => {
      slot = input.scrollbars.slot
      return {
        options() {},
        elements: () => ({ scrollbarHorizontal: { scrollbar: bar } }),
        destroy() { destroyed = true },
      }
    },
    requestAnimationFrame(callback: () => void) { frames.set(++frameId, callback); return frameId },
    cancelAnimationFrame(id: number) { frames.delete(id) },
    window: {
      addEventListener(name: string, callback: () => void) { windowListeners.set(name, callback) },
      removeEventListener(name: string) { windowListeners.delete(name) },
    },
    ResizeObserver: class {
      constructor(callback: () => void) { resize = callback }
      observe(node: unknown) { observed.push(node) }
      disconnect() { disconnected++ }
    },
    IntersectionObserver: class {
      observe() {}
      disconnect() { disconnected++ }
    },
  }
  const flush = () => {
    const pending = [...frames.values()]
    frames.clear()
    pending.forEach(callback => callback())
  }
  const create = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return createScrollbars;`)(deps)
  const binding = create(table, 'x', { pinHorizontalTo: root })
  expect(slot).toBe(host)
  expect(host.style.getPropertyValue('position')).toBe('relative')
  expect(observed).toEqual([table, root, host, content])
  flush()
  expect(bar.style.getPropertyValue('top')).toBe('822px')
  const initialWrites = bar.style.writes.length
  const geometryReads = () => table.geometryReads + root.geometryReads + host.geometryReads
  expect(geometryReads()).toBe(3)

  // The table moves but the control stays at the same viewport coordinate.
  root.scrollTop = 300
  table.rect = { ...table.rect, top: 120, bottom: 1300 }
  root.listeners.get('scroll')!()
  root.listeners.get('scroll')!()
  expect(frames.size).toBe(1)
  flush()
  expect(bar.style.getPropertyValue('top')).toBe('822px')
  expect(bar.style.writes).toHaveLength(initialWrites)
  expect(geometryReads()).toBe(3)
  expect(visibilityWrites).toBe(0)

  // At the end of the table it follows the table edge, not the load-more area.
  root.scrollTop = 650
  table.rect.top = -230
  table.rect.bottom = 950
  root.listeners.get('scroll')!()
  flush()
  expect(bar.style.getPropertyValue('top')).toBe('762px')
  expect(geometryReads()).toBe(3)
  table.rect.top -= 4
  table.rect.bottom = 946
  root.listeners.get('transitionend')!({ propertyName: 'transform' })
  flush()
  expect(bar.style.getPropertyValue('top')).toBe('758px')
  expect(geometryReads()).toBe(6)

  root.rect.right = 1300
  resize()
  flush()
  expect(bar.style.getPropertyValue('width')).toBe('1240px')
  expect(geometryReads()).toBe(9)
  root.scrollTop = 1600
  root.listeners.get('scroll')!()
  flush()
  expect(bar.hidden).toBe(true)
  root.scrollTop = 650
  root.listeners.get('scroll')!()
  flush()
  expect(bar.hidden).toBe(false)
  expect(visibilityWrites).toBe(2)
  expect(geometryReads()).toBe(9)

  // A chart above the table can grow without changing the table's own size.
  table.rect.top += 80
  table.rect.bottom += 80
  content.rect.bottom += 80
  resize()
  flush()
  expect(bar.style.getPropertyValue('top')).toBe('822px')
  expect(geometryReads()).toBe(12)

  resize()
  binding.dispose()
  expect(frames.size).toBe(0)
  expect(root.listeners.size).toBe(0)
  expect(windowListeners.size).toBe(0)
  expect(table.listeners.size).toBe(0)
  expect(disconnected).toBe(2)
  expect(destroyed).toBe(true)
  expect(host.style.getPropertyValue('position')).toBe('')
})
