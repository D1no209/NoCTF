import { describe, expect, test } from 'bun:test'
import { applyInputModelModifiers } from '../src/components/ui/input/modelValue'

describe('input model modifiers', () => {
  test('converts edited numeric input values before emitting them', () => {
    expect(applyInputModelModifiers('587', { number: true })).toBe(587)
    expect(applyInputModelModifiers('0.25', { number: true })).toBe(0.25)
  })

  test('preserves empty and non-numeric values for native validation', () => {
    expect(applyInputModelModifiers('', { number: true })).toBe('')
    expect(applyInputModelModifiers('invalid', { number: true })).toBe('invalid')
    expect(applyInputModelModifiers('587')).toBe('587')
  })
})
