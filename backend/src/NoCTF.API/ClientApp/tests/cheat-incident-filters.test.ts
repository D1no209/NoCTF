import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import {
  defaultCheatIncidentQueryRange,
  resolveCheatIncidentQueryRange,
} from '../app/lib/cheat-incident-query-range'

const now = new Date('2026-08-10T00:00:00.000Z')

describe('cheat incident query range', () => {
  test('defaults empty filters to one stable 31 day window', () => {
    const range = defaultCheatIncidentQueryRange(now)
    const resolved = resolveCheatIncidentQueryRange('', '', now)

    expect(resolved).toEqual({ range, error: null })
    expect(range.to).toBe(now.toISOString())
    expect(Date.parse(range.to) - Date.parse(range.from)).toBe(31 * 24 * 60 * 60 * 1000)
  })

  test('requires both explicit date inputs', () => {
    expect(resolveCheatIncidentQueryRange('2026-08-01T00:00:00Z', '', now)).toEqual({
      range: null,
      error: '请同时填写起始和结束时间，或全部留空查询最近 31 天。',
    })
    expect(resolveCheatIncidentQueryRange('', '2026-08-10T00:00:00Z', now)).toEqual({
      range: null,
      error: '请同时填写起始和结束时间，或全部留空查询最近 31 天。',
    })
  })

  test('rejects invalid, reversed, and overlong explicit ranges', () => {
    expect(resolveCheatIncidentQueryRange('invalid', '2026-08-10T00:00:00Z', now).error)
      .toBe('请输入有效的起始和结束时间。')
    expect(resolveCheatIncidentQueryRange('2026-08-10T00:00:00Z', '2026-08-09T00:00:00Z', now).error)
      .toBe('起始时间不能晚于结束时间。')
    expect(resolveCheatIncidentQueryRange('2026-07-09T23:59:59Z', '2026-08-10T00:00:00Z', now).error)
      .toBe('查询时间范围不能超过 31 天。')
  })

  test('accepts an explicit range of exactly 31 days', () => {
    expect(resolveCheatIncidentQueryRange('2026-07-10T00:00:00Z', '2026-08-10T00:00:00Z', now))
      .toEqual({
        range: {
          from: '2026-07-10T00:00:00.000Z',
          to: '2026-08-10T00:00:00.000Z',
        },
        error: null,
      })
  })
})

describe('cheat incident filter wiring', () => {
  test('defaults status to all and uses frozen applied filters for the SDK query', async () => {
    const page = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/cheats.vue', import.meta.url),
    ).text()

    expect(page).toContain("const filterStatus = ref<CheatIncidentStatusFilter>('All')")
    expect(page).toContain("<SelectItem value=\"All\">{{ $t('administration.label.platformLogs') }}</SelectItem>")
    expect(page).toContain("status: appliedStatus.value === 'All' ? null : appliedStatus.value")
    expect(page).toContain('from: appliedRange.value.from')
    expect(page).toContain('to: appliedRange.value.to')
    expect(page).not.toContain("'1970-01-01T00:00:00Z'")
    expect(page).not.toContain("'2999-12-31T23:59:59Z'")
  })
})
