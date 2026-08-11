import { describe, expect, test } from 'bun:test'

const page = await Bun.file(
  new URL('../app/pages/competitions/[id]/leaderboard.vue', import.meta.url),
).text()

describe('leaderboard progressive display', () => {
  test('keeps the full snapshot and progressively reveals stable entries', () => {
    expect(page).toContain('const entryBatchSize = 50')
    expect(page).toContain('entries.value.slice(0, visibleEntryCount.value)')
    expect(page).toContain('v-for="entry in visibleEntries"')
    expect(page).toContain('v-if="hasMoreEntries"')
    expect(page).toContain('@click="showMoreEntries"')
  })

  test('does not recreate numbered client-side pagination', () => {
    expect(page).not.toContain('const pageSize =')
    expect(page).not.toContain('const totalPages =')
    expect(page).not.toContain('@click="page--"')
    expect(page).not.toContain('@click="page++"')
  })
})
