import { describe, expect, test } from 'bun:test'
import { Bot, CircuitBoard, Cloud, Flag, Link2, ScanSearch, Search } from '@lucide/vue'
import { directionIcon } from '../app/utils/directions'

describe('challenge direction iconography', () => {
  test('maps common security directions to semantic icons', () => {
    expect(directionIcon('OSINT')).toBe(Search)
    expect(directionIcon('Open Source Intelligence')).toBe(Search)
    expect(directionIcon('AI')).toBe(Bot)
    expect(directionIcon('Machine Learning')).toBe(Bot)
    expect(directionIcon('Blockchain')).toBe(Link2)
    expect(directionIcon('Web3')).toBe(Link2)
    expect(directionIcon('Forensics')).toBe(ScanSearch)
    expect(directionIcon('Hardware')).toBe(CircuitBoard)
    expect(directionIcon('Cloud')).toBe(Cloud)
  })

  test('keeps unknown open-text directions on the explicit fallback', () => {
    expect(directionIcon('Custom Research')).toBe(Flag)
  })
})
