import { describe, expect, test } from 'bun:test'
import { areMocksEnabled } from '../src/mocks/runtime'

describe('mock runtime activation', () => {
  test('keeps mocks disabled by default in development', () => {
    expect(areMocksEnabled(true)).toBe(false)
    expect(areMocksEnabled(true, 'false')).toBe(false)
  })

  test('requires both development mode and explicit opt-in', () => {
    expect(areMocksEnabled(true, 'true')).toBe(true)
    expect(areMocksEnabled(false, 'true')).toBe(false)
  })
})
