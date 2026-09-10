import { expect, test } from 'bun:test'
import { typewriterFrames } from '../app/motion/useTypewriterMotion'

test('terminal reveal preserves Chinese and supplementary Unicode characters', () => {
  const frames = typewriterFrames(['命令🛰', '状态'], 3)
  expect(frames.map(frame => frame.visible)).toEqual(['命令🛰', ''])
})

test('terminal cursor advances across fields and disappears when the output completes', () => {
  expect(typewriterFrames(['', 'live', '3'], 2).map(frame => frame.active)).toEqual([false, true, false])
  const completed = typewriterFrames(['live', '3'], 5)
  expect(completed.map(frame => frame.visible)).toEqual(['live', '3'])
  expect(completed.some(frame => frame.active)).toBe(false)
})
