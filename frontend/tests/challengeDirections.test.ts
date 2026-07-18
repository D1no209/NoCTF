import { describe, expect, it } from 'bun:test'
import { challengeDirectionsForType, normalizeDirection } from '../src/lib/challengeDirections'

describe('challenge directions', () => {
  it('keeps the runtime type separate from mode-specific directions', () => {
    expect(challengeDirectionsForType('Awd')).toEqual(['WEB', 'PWN'])
    expect(challengeDirectionsForType('Awdp')).toEqual(['WEB', 'PWN', 'AI'])
    expect(challengeDirectionsForType('Ctf')).toContain('MISC')
  })

  it('normalizes missing legacy values without pretending they are MISC', () => {
    expect(normalizeDirection()).toBe('UNCATEGORIZED')
    expect(normalizeDirection(' pwn ')).toBe('PWN')
  })
})
