import { describe, expect, test } from 'bun:test'
import { createMockApi } from '../mock/api'
import { id } from '../mock/schema'

describe('competition runtime administration pagination', () => {
  test('uses numbered offset pages and resets to the first page when filters are applied', async () => {
    const controller = await Bun.file(new URL('../app/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdRuntimesPage.ts', import.meta.url)).text().then(source => source.replace(/\r\n/g, '\n'))
    const view = await Bun.file(new URL('../app/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdRuntimesPageView.vue', import.meta.url)).text().then(source => source.replace(/\r\n/g, '\n'))

    expect(controller).toContain('useOffsetPagination<NoCTFAPIEndpointsAdministrationRuntimeAdminRuntimeResponse>')
    expect(controller).toContain('offset,\n        limit,\n        desc,')
    expect(controller).toContain('return { items: data.items ?? [], total: data.total ?? 0 }')
    expect(controller).toContain('initialPageSize: 10')
    expect(controller).toContain('pagination.reset()\n    await pagination.loadPage(1)')
    expect(view).toContain('<OffsetPagination v-if="initialized"')
    expect(view).not.toContain('loadMore')
  })

  test('the existing administrator API returns distinct bounded pages and the same total', async () => {
    const api = createMockApi()
    const base = 'http://127.0.0.1:5081'
    const login = await api.handle(new Request(`${base}/api/v1/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ login: 'admin', password: 'Mock123!' }),
    }))
    const { accessToken } = await login.json()
    const path = `/api/v1/admin/competitions/${id(2)}/runtimes`
    const requestPage = async (offset: number) => {
      const response = await api.handle(new Request(`${base}${path}?offset=${offset}&limit=5&desc=true`, {
        headers: { Authorization: `Bearer ${accessToken}` },
      }))
      expect(response.status).toBe(200)
      return response.json()
    }

    const first = await requestPage(0)
    const second = await requestPage(5)
    expect(first.total).toBeGreaterThan(5)
    expect(second.total).toBe(first.total)
    expect(first.items).toHaveLength(5)
    expect(second.items.length).toBeGreaterThan(0)
    expect(second.items.every((item: { id: string }) => first.items.every((previous: { id: string }) => previous.id !== item.id))).toBe(true)
  })
})
