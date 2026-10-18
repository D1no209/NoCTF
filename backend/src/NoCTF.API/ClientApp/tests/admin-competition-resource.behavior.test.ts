import { describe, expect, test } from 'bun:test'
import { createAdminCompetitionResource } from '../app/features/admin/admin-competition-resource'

describe('shared management competition reads', () => {
  test('guard, shell and children share one full response and one in-flight request', async () => {
    let reads = 0, finish!: (value: any) => void
    const resource = createAdminCompetitionResource(() => { reads++; return new Promise(resolve => { finish = resolve }) })
    const guard = resource.read('actor', 'competition')
    const shell = resource.read('actor', 'competition')
    expect(shell).toBe(guard)
    finish({ data: { competition: { id: 'competition' }, modeConfiguration: { mode: 'Ctf' } } })
    await guard
    expect((await resource.read('actor', 'competition')).data?.modeConfiguration?.mode).toBe('Ctf')
    expect(reads).toBe(1)
  })

  test('transient failure preserves the snapshot but closes write capabilities until recovery', async () => {
    let fail = false
    const resource = createAdminCompetitionResource(async () => fail
      ? { error: {}, response: new Response(null, { status: 503 }) }
      : { data: { competition: { id: 'competition' } } })
    await resource.read('actor', 'competition')
    fail = true
    await resource.read('actor', 'competition', true)
    expect(resource.data.value?.competition?.id).toBe('competition')
    expect(resource.fresh.value).toBe(false)
    fail = false
    await resource.read('actor', 'competition')
    expect(resource.fresh.value).toBe(true)
  })

  test.each([401, 403, 404])('%s clears the authorized snapshot', async status => {
    let fail = false
    const resource = createAdminCompetitionResource(async () => fail
      ? { error: {}, response: new Response(null, { status }) }
      : { data: { competition: { id: 'competition' } } })
    await resource.read('actor', 'competition')
    fail = true
    await resource.read('actor', 'competition', true)
    expect(resource.data.value).toBeNull()
    expect(resource.fresh.value).toBe(false)
  })

  test('late reads and unmounting an older workspace cannot restore or clear another identity', async () => {
    let finish!: (value: any) => void
    const resource = createAdminCompetitionResource(id => id === 'old'
      ? new Promise(resolve => { finish = resolve }) : Promise.resolve({ data: { competition: { id } } }))
    const old = resource.read('actor', 'old')
    await resource.read('other', 'new')
    finish({ data: { competition: { id: 'old' } } })
    await old
    resource.clear('actor', 'old')
    resource.accept({ competition: { id: 'old' } }, 'actor', 'old')
    expect(resource.data.value?.competition?.id).toBe('new')
  })

  test('accepted write sections update the shared snapshot without another GET', async () => {
    let reads = 0
    const resource = createAdminCompetitionResource(async () => { reads++; return { data: { competition: { id: 'competition' } } } })
    await resource.read('actor', 'competition')
    resource.accept({ modeConfiguration: { mode: 'Awd' } })
    expect((await resource.read('actor', 'competition')).data?.modeConfiguration?.mode).toBe('Awd')
    expect(reads).toBe(1)
    resource.clear('actor', 'competition')
    await resource.read('actor', 'competition')
    expect(reads).toBe(2)
  })
})
