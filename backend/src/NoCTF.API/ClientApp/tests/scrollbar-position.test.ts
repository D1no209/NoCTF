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
