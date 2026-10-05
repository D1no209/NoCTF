import { afterEach, beforeEach, describe, expect, test } from 'bun:test'
import { client } from '../app/api/client.gen'
import { useCompetitionsByIdTeamsIndexPage } from '../app/features/routes/competitions/[id]/teams/useCompetitionsByIdTeamsIndexPage'

const savedConfig = client.getConfig()
const globals = globalThis as unknown as Record<string, unknown>
const originalGlobals = new Map(['useRoute', 'onMounted', 'onBeforeUnmount']
  .map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)]))
let mount: (() => Promise<void>) | undefined
let unmount: (() => void) | undefined

beforeEach(() => {
  mount = undefined
  unmount = undefined
  globals.useRoute = () => ({ params: { id: 'competition-under-test' } })
  globals.onMounted = (callback: () => Promise<void>) => { mount = callback }
  globals.onBeforeUnmount = (callback: () => void) => { unmount = callback }
})

afterEach(() => {
  client.setConfig(savedConfig)
  for (const [key, descriptor] of originalGlobals) {
    if (descriptor) Object.defineProperty(globalThis, key, descriptor)
    else delete globals[key]
  }
})

describe('public competition team pagination', () => {
  test('pages the complete snapshot, retains cross-page duplicate names and avoids refetching', async () => {
    const teams = Array.from({ length: 23 }, (_, index) => ({
      id: `${(index + 1).toString(16).padStart(8, '0')}-0000-4000-8000-000000000001`,
      name: index === 0 || index === 15 ? 'Echo' : `Team ${index + 1}`,
    }))
    let requests = 0
    client.setConfig({ baseUrl: 'http://teams.test', fetch: async (request) => {
      expect(new URL(request.url).pathname).toBe('/api/v1/competitions/competition-under-test/teams')
      requests++
      return Response.json({ items: teams })
    } })
    const state = useCompetitionsByIdTeamsIndexPage()
    expect(state.loading.value).toBeTrue()
    await mount!()

    expect(state.total.value).toBe(23)
    expect(state.pageCount.value).toBe(3)
    expect(state.teams.value.map(team => team.id)).toEqual(teams.slice(0, 10).map(team => team.id))
    expect(state.teamDisplayNames.value.get(teams[0]!.id)).toBe('Echo · 00000001')
    expect(state.loading.value).toBeFalse()

    await state.loadPage(2)
    expect(state.teams.value.map(team => team.id)).toEqual(teams.slice(10, 20).map(team => team.id))
    expect(state.teamDisplayNames.value.get(teams[15]!.id)).toBe('Echo · 00000010')
    await state.loadPage(3)
    expect(state.teams.value).toHaveLength(3)
    await state.setPageSize(20)
    expect(state.page.value).toBe(1)
    expect(state.pageCount.value).toBe(2)
    expect(state.teams.value).toHaveLength(20)
    await state.loadPage(2)
    expect(state.teams.value.map(team => team.id)).toEqual(teams.slice(20).map(team => team.id))
    expect(requests).toBe(1)
  })

  test('completes empty and failed loads without getting stuck in loading', async () => {
    client.setConfig({ baseUrl: 'http://teams.test', fetch: async () => Response.json({ items: [] }) })
    const empty = useCompetitionsByIdTeamsIndexPage()
    await mount!()
    expect(empty.loading.value).toBeFalse()
    expect(empty.initialized.value).toBeTrue()
    expect(empty.total.value).toBe(0)
    expect(empty.teams.value).toEqual([])

    client.setConfig({ fetch: async () => Response.json({ status: 500, detail: '列表暂时不可用' }, { status: 500 }) })
    const failed = useCompetitionsByIdTeamsIndexPage()
    await mount!()
    expect(failed.loading.value).toBeFalse()
    expect(failed.initialized.value).toBeFalse()
    expect(failed.error.value).toBe('列表暂时不可用')
  })

  test('does not publish an outstanding load after the page unmounts', async () => {
    let resolve!: (response: Response) => void
    let started!: () => void
    const requestStarted = new Promise<void>((done) => { started = done })
    client.setConfig({ baseUrl: 'http://teams.test', fetch: () => new Promise<Response>((done) => {
      resolve = done
      started()
    }) })
    const state = useCompetitionsByIdTeamsIndexPage()
    const pending = mount!()
    await requestStarted
    unmount!()
    resolve(Response.json({ items: [{ id: 'late-team', name: 'Late' }] }))
    await pending
    expect(state.teams.value).toEqual([])
    expect(state.total.value).toBe(0)
    expect(state.initialized.value).toBeFalse()
  })
})
