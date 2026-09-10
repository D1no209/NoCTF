import { expect, test } from 'bun:test'
import { waveDisplacement } from '../app/motion/useWaveMotion'

test('the wave peaks at the pointer and falls smoothly across neighboring rows', () => {
  const peak = waveDisplacement(0, false)
  const near = waveDisplacement(80, false)
  const far = waveDisplacement(200, false)
  expect(peak.offset).toBe(30)
  expect(peak.offset).toBeGreaterThan(near.offset)
  expect(near.offset).toBeGreaterThan(far.offset)
  expect(peak.scale).toBeLessThan(1.02)
  expect(waveDisplacement(-80, false)).toEqual(near)
})

test('selection remains raised after leaving and reduced motion disables pointer waves', () => {
  expect(waveDisplacement(Infinity, true)).toEqual({ offset: 14, scale: 1 })
  expect(waveDisplacement(Infinity, false)).toEqual({ offset: 0, scale: 1 })
  expect(waveDisplacement(0, false, true)).toEqual({ offset: 0, scale: 1 })
  expect(waveDisplacement(0, true, true)).toEqual({ offset: 14, scale: 1 })
})
