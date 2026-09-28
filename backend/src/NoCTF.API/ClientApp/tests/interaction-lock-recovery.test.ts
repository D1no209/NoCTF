import { expect, test } from 'bun:test'
import { releaseStaleInteractionLock } from '../app/features/shell/interaction-lock-recovery'

interface FakeStyle {
  display: string
  opacity: string
  pointerEvents: string
  visibility: string
  priority: string
  removeProperty: (property: string) => string
  setProperty: (property: string, value: string, priority?: string) => void
}

function fakeStyle(pointerEvents = 'auto'): FakeStyle {
  return {
    display: 'block',
    opacity: '1',
    pointerEvents,
    visibility: 'visible',
    priority: '',
    removeProperty(property: string) {
      if (property === 'pointer-events') this.pointerEvents = ''
      return pointerEvents
    },
    setProperty(property: string, value: string, priority = '') {
      if (property === 'pointer-events') this.pointerEvents = value
      this.priority = priority
    },
  }
}

function fakeLayer({ visible = true, pointerEvents = 'auto', recovered = false } = {}) {
  const attributes = new Map<string, string>()
  if (recovered) attributes.set('data-interaction-lock-recovered', '')
  const style = fakeStyle(pointerEvents)

  return {
    isConnected: true,
    style,
    computedStyle: style,
    getClientRects: () => visible ? [{}] : [],
    getAttribute: (name: string) => attributes.get(name) ?? null,
    hasAttribute: (name: string) => attributes.has(name),
    removeAttribute: (name: string) => attributes.delete(name),
    setAttribute: (name: string, value: string) => attributes.set(name, value),
  }
}

function interactionDocument(
  pointerEvents: string,
  { openLayers = [], overlays = [] }: {
    openLayers?: ReturnType<typeof fakeLayer>[]
    overlays?: ReturnType<typeof fakeLayer>[]
  } = {},
) {
  const bodyStyle = fakeStyle(pointerEvents)
  const documentRoot = {
    body: { style: bodyStyle },
    defaultView: {
      getComputedStyle: (layer: ReturnType<typeof fakeLayer>) => layer.computedStyle,
    },
    querySelectorAll: (selector: string) => {
      if (selector === '[data-interaction-lock-recovered]')
        return overlays.filter(layer => layer.hasAttribute('data-interaction-lock-recovered'))
      if (selector.includes('data-dismissable-layer')) return openLayers
      return overlays
    },
  } as unknown as Document

  return { bodyStyle, documentRoot }
}

test('releases a pointer lock after the final modal layer leaves', () => {
  const { documentRoot, bodyStyle } = interactionDocument('none')

  expect(releaseStaleInteractionLock(documentRoot)).toBe(true)
  expect(bodyStyle.pointerEvents).toBe('')
})

test('keeps the pointer lock while a visible interaction layer remains open', () => {
  const { documentRoot, bodyStyle } = interactionDocument('none', {
    openLayers: [fakeLayer()],
  })

  expect(releaseStaleInteractionLock(documentRoot)).toBe(false)
  expect(bodyStyle.pointerEvents).toBe('none')
})

test('does not let an invisible stale layer preserve the body lock', () => {
  const { documentRoot, bodyStyle } = interactionDocument('none', {
    openLayers: [fakeLayer({ visible: false })],
  })

  expect(releaseStaleInteractionLock(documentRoot)).toBe(true)
  expect(bodyStyle.pointerEvents).toBe('')
})

test('makes an orphaned full-screen overlay inert', () => {
  const overlay = fakeLayer()
  const { documentRoot, bodyStyle } = interactionDocument('none', { overlays: [overlay] })

  expect(releaseStaleInteractionLock(documentRoot)).toBe(true)
  expect(bodyStyle.pointerEvents).toBe('')
  expect(overlay.style.pointerEvents).toBe('none')
  expect(overlay.style.priority).toBe('important')
  expect(overlay.hasAttribute('data-interaction-lock-recovered')).toBe(true)
})

test('restores a recovered overlay when a real layer opens again', () => {
  const overlay = fakeLayer({ pointerEvents: 'none', recovered: true })
  const { documentRoot } = interactionDocument('none', {
    openLayers: [fakeLayer()],
    overlays: [overlay],
  })

  expect(releaseStaleInteractionLock(documentRoot)).toBe(false)
  expect(overlay.style.pointerEvents).toBe('')
  expect(overlay.hasAttribute('data-interaction-lock-recovered')).toBe(false)
})

test('does not change an unlocked page without a modal overlay', () => {
  const { documentRoot, bodyStyle } = interactionDocument('')

  expect(releaseStaleInteractionLock(documentRoot)).toBe(false)
  expect(bodyStyle.pointerEvents).toBe('')
})
