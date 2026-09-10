import { expect, test } from 'bun:test'

test('scrollbar enhancement preserves fixed/absolute positioning and restores original inline styles', async () => {
  const source = await Bun.file(new URL('../app/components/ui/scroll-area/scrollbars.ts', import.meta.url)).text()
  const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
    .replace(/^import[^\n]*\n/gm, '').replace('export function installScrollbars', 'function installScrollbars')
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
    isConnected = true
    constructor(public position: string, public children: Node[] = []) {}
    querySelectorAll() { return this.children }
    querySelector() { return null }
    matches() { return false }
    addEventListener() {}
    removeEventListener() {}
  }
  const dialog = new Node('fixed')
  const popover = new Node('absolute')
  popover.style.setProperty('position', 'absolute', 'important')
  const normal = new Node('static')
  const body = new Node('static', [dialog, popover, normal])
  const deps = {
    document: { body }, Element: Node, HTMLElement: Node,
    getComputedStyle: (node: Node) => ({ position: node.style.getPropertyValue('position') || (node.enhanced ? 'relative' : node.position) }),
    MutationObserver: class { observe() {} disconnect() {} },
    OverlayScrollbars: (value: Node | { target: Node }) => {
      const target = value instanceof Node ? value : value.target
      target.enhanced = true
      return { options() {}, destroy() { target.enhanced = false } }
    },
  }
  const dispose = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return installScrollbars();`)(deps)
  expect(deps.getComputedStyle(dialog).position).toBe('fixed')
  expect(deps.getComputedStyle(popover).position).toBe('absolute')
  expect(deps.getComputedStyle(normal).position).toBe('relative')
  dispose()
  expect(dialog.style.getPropertyValue('position')).toBe('')
  expect(popover.style.getPropertyValue('position')).toBe('absolute')
  expect(popover.style.getPropertyPriority('position')).toBe('important')
})
