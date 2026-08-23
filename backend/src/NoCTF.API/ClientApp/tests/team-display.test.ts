import { describe, expect, it } from 'vitest'
import { buildTeamDisplayNames } from '../app/utils/team-display'

describe('team display names', () => {
  it('adds a short id only when names are duplicated case-insensitively', () => {
    const labels = buildTeamDisplayNames([
      { id: '019ff000-1111-2222-3333-444444444444', name: 'Alpha' },
      { id: '019ff001-1111-2222-3333-444444444444', name: 'alpha' },
      { id: '019ff002-1111-2222-3333-444444444444', name: 'Beta' },
    ])

    expect(labels.get('019ff000-1111-2222-3333-444444444444')).toBe('Alpha · 019ff000')
    expect(labels.get('019ff001-1111-2222-3333-444444444444')).toBe('alpha · 019ff001')
    expect(labels.get('019ff002-1111-2222-3333-444444444444')).toBe('Beta')
  })
})
