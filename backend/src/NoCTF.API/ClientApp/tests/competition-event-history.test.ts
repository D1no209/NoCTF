import { describe, expect, test } from 'bun:test'
import { competitionEventHistoryRange } from '../app/lib/competition-event-history'

describe('competition event history scope', () => {
  const now = Date.parse('2026-08-11T12:00:00.000Z')

  test('omits the date range for staff permanent-history navigation', () => {
    expect(competitionEventHistoryRange(true, '2024-01-01T00:00:00.000Z', now))
      .toEqual({})
  })

  test('bounds participant history to the most recent 30 days', () => {
    expect(competitionEventHistoryRange(false, '2024-01-01T00:00:00.000Z', now))
      .toEqual({
        from: '2026-07-12T12:00:00.000Z',
        to: '2026-08-11T12:00:00.000Z',
      })
  })

  test('does not request events from before a recent competition began', () => {
    expect(competitionEventHistoryRange(false, '2026-08-10T09:30:00.000Z', now))
      .toEqual({
        from: '2026-08-10T09:30:00.000Z',
        to: '2026-08-11T12:00:00.000Z',
      })
  })

  test('uses an empty current range before the competition starts', () => {
    expect(competitionEventHistoryRange(false, '2026-08-12T09:30:00.000Z', now))
      .toEqual({
        from: '2026-08-11T12:00:00.000Z',
        to: '2026-08-11T12:00:00.000Z',
      })
  })

  test('wires staff detection through the generated administration SDK', async () => {
    const source = await Bun.file(
      new URL('../app/pages/competitions/[id]/events.vue', import.meta.url),
    ).text()

    expect(source).toContain('adminGetCompetition({')
    expect(source).toMatch(/competitionEventHistoryRange\(\s*hasStaffHistory\.value,/)
    expect(source).toContain("$t(hasStaffHistory ? '完整历史' : '最近 30 天')")
  })
})
