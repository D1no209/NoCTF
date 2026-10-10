import { describe, expect, test } from 'bun:test'
import { useCursorPagePagination } from '../app/composables/useCursorPagePagination'

describe('bounded cursor page navigation', () => {
  test('replaces each page, retains cursor positions and permits previous-page reads', async () => {
    const calls: any[] = []
    const pager = useCursorPagePagination(async (cursor, limit) => {
      calls.push({ cursor, limit })
      return cursor === null ? { items: ['a', 'b'], nextCursor: 'second' } : { items: ['c'], nextCursor: null }
    }, 2)
    await pager.loadPage(1); await pager.loadPage(2)
    expect(pager.items.value).toEqual(['c'])
    expect(pager.hasNext.value).toBeFalse()
    await pager.loadPage(1)
    expect(pager.items.value).toEqual(['a', 'b'])
    expect(calls.map(c => c.cursor)).toEqual([null, 'second', null])
  })

  test('does not advance a failed page and retries its same cursor', async () => {
    let fail = false
    const pager = useCursorPagePagination(async cursor => {
      if (fail) { fail = false; throw { status: 503 } }
      return { items: [cursor ?? 'first'], nextCursor: cursor ? null : 'next' }
    })
    await pager.loadPage(); fail = true; await pager.loadPage(2)
    expect(pager.page.value).toBe(1)
    expect(pager.items.value).toEqual(['first'])
    expect(pager.error.value).not.toBeNull()
    await pager.loadPage(2)
    expect(pager.items.value).toEqual(['next'])
  })

  test('rejects stale pages after reset and coalesces same-generation reads', async () => {
    let release!: (value: any) => void
    let calls = 0
    const pager = useCursorPagePagination(async () => {
      calls += 1
      if (calls === 1) return new Promise<any>(resolve => { release = resolve })
      return { items: ['new'], nextCursor: null }
    })
    const old = pager.loadPage(); await pager.loadPage()
    expect(calls).toBe(1)
    pager.reset(); await pager.loadPage()
    release({ items: ['old'], nextCursor: 'stale' }); await old
    expect(pager.items.value).toEqual(['new'])
    expect(pager.hasNext.value).toBeFalse()
  })
})
