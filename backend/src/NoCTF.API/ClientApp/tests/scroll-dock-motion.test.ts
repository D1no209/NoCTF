import { expect, test } from 'bun:test'
import { advanceScrollDock, compactScrollDock, scrollDockOffset } from '../app/motion/useScrollDockMotion'

test('the dock moves with its intro then stops at the top without following extra scroll', () => {
  expect(scrollDockOffset(160, 0)).toBe(160)
  expect(scrollDockOffset(160, 80)).toBe(80)
  expect(scrollDockOffset(160, 160)).toBe(0)
  expect(scrollDockOffset(160, 1200)).toBe(0)
  expect(scrollDockOffset(160, -30)).toBe(160)
})

test('the compact row reserves countdown and action widths and truncates the title rather than overlapping controls', () => {
  const layout = compactScrollDock([
    { x: 0, y: 0, width: 400, height: 36, scale: 0.76, flexible: true },
    { x: 0, y: 60, width: 150, height: 32, scale: 0.82 },
    { x: 0, y: 110, width: 330, height: 40, scale: 0.86 },
  ], 684)
  expect(layout.positions.every(position => position.y === 12)).toBeTrue()
  expect(layout.positions[0]!.width).toBeLessThan(400 * 0.76)
  layout.positions.forEach((position, index) => {
    expect(position.x + position.width).toBeLessThanOrEqual(672)
    if (index > 0) expect(position.x).toBeGreaterThan(layout.positions[index - 1]!.x + layout.positions[index - 1]!.width)
  })
})

test('reverse progress remains eased for small scroll deltas and narrow screens wrap without horizontal overflow', () => {
  expect(advanceScrollDock(0.4, 0.45, 16, false, 0.001)).toBeGreaterThan(0.4)
  expect(advanceScrollDock(0.4, 0.45, 16, false, 0.001)).toBeLessThan(0.45)
  const layout = compactScrollDock([
    { x: 0, y: 0, width: 300, height: 40, scale: 0.8, flexible: true },
    { x: 0, y: 50, width: 150, height: 30, scale: 0.8 },
    { x: 0, y: 100, width: 330, height: 40, scale: 0.86 },
  ], 320)
  expect(layout.positions[2]!.y).toBeGreaterThan(12)
  expect(layout.positions.every(position => position.x + position.width <= 308)).toBeTrue()
})

test('downward scrolling follows directly and reverse scrolling settles monotonically without overshoot', () => {
  expect(advanceScrollDock(160, 80, 16)).toBe(80)
  expect(advanceScrollDock(80, 0, 16)).toBe(0)
  let position = 0
  for (let frame = 0; frame < 80; frame++) {
    const next = advanceScrollDock(position, 160, 16)
    expect(next).toBeGreaterThanOrEqual(position)
    expect(next).toBeLessThanOrEqual(160)
    position = next
  }
  expect(position).toBe(160)
  expect(advanceScrollDock(0, 160, 16, true)).toBe(160)
})
