import { expect, test } from 'bun:test'
import { releaseStaleInteractionLock } from '../app/features/shell/interaction-lock-recovery'

function interactionDocument(pointerEvents: string, activeLayer: boolean) {
  const style = {
    pointerEvents,
    removeProperty(property: string) {
      if (property === 'pointer-events') this.pointerEvents = ''
      return pointerEvents
    },
  }

  return {
    documentRoot: {
      body: { style },
      querySelector: () => activeLayer ? ({}) : null,
    } as unknown as Document,
    style,
  }
}

test('releases a pointer lock after the final modal layer leaves', () => {
  const { documentRoot, style } = interactionDocument('none', false)

  expect(releaseStaleInteractionLock(documentRoot)).toBe(true)
  expect(style.pointerEvents).toBe('')
})

test('keeps the pointer lock while an interactive layer remains open', () => {
  const { documentRoot, style } = interactionDocument('none', true)

  expect(releaseStaleInteractionLock(documentRoot)).toBe(false)
  expect(style.pointerEvents).toBe('none')
})

test('does not change an unlocked page', () => {
  const { documentRoot, style } = interactionDocument('', false)

  expect(releaseStaleInteractionLock(documentRoot)).toBe(false)
  expect(style.pointerEvents).toBe('')
})
