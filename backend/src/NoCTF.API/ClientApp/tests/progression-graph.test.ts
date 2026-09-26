import { describe, expect, test } from 'bun:test'
import { canConnectProgression } from '../app/lib/progression-graph'

describe('progression editor connections', () => {
  test('allows a new edge and independent badge paths', () => {
    expect(canConnectProgression([{ source: 'challenge', target: 'badge-a' }],
      'challenge', 'badge-b')).toBe(true)
  })

  test('rejects a self edge, duplicate edge and a transitive cycle', () => {
    const edges = [
      { source: 'a', target: 'b' },
      { source: 'b', target: 'c' },
    ]
    expect(canConnectProgression(edges, 'a', 'a')).toBe(false)
    expect(canConnectProgression(edges, 'a', 'b')).toBe(false)
    expect(canConnectProgression(edges, 'c', 'a')).toBe(false)
    expect(canConnectProgression(edges, 'c', 'd')).toBe(true)
  })
})
