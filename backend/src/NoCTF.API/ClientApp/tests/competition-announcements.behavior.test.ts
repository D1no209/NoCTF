import { describe, expect, test } from 'bun:test'
import { computed, effectScope, nextTick, onScopeDispose, ref, watch } from 'vue'
import { useOffsetPagination } from '../app/composables/useOffsetPagination'
import { ApiError } from '../app/utils/api-error'
import { createTrailingRefresh } from '../app/lib/latest-page-refresh'
import { sourceFile } from './support/feature-source'

const source = await Bun.file(new URL('../app/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdAnnouncementsPage.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }

function harness(writable = true) {
  const calls: Array<{ operation: string; options: any }> = []
  let failing = false
  const query = async (options: any) => { calls.push({ operation:'list', options }); return { data: { items:[], total:0 } } }
  const mutate = (operation: string) => async (options: any) => {
    calls.push({ operation, options })
    return failing ? { error: new ApiError('rejected') } : { data: { id:'announcement' } }
  }
  const deps = { computed, ref, watch, onScopeDispose, onMounted: (callback: () => void) => callback(),
    useCompetitionAdmin: () => ({ competitionId:'competition', canJudge: ref(writable) }), useOffsetPagination,
    adminListCompetitionAnnouncements: query, adminCreateCompetitionAnnouncement: mutate('create'),
    adminUpdateCompetitionAnnouncement: mutate('edit'), adminDeleteCompetitionAnnouncement: mutate('delete'),
    watchNotifications: () => () => {}, createTrailingRefresh, translate: (key: string) => key,
    parseApiError: (value: unknown) => value instanceof ApiError ? value : new ApiError('failed'),
    adminUserPath: () => '', adminFormatDateTime: () => '', toast:{success:()=>{}}, document:{getElementById:()=>null} }
  const factory = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useAdminCompetitionsByIdAnnouncementsPage;`)(deps)
  const scope = effectScope()
  const state = scope.run(() => factory())!
  return { state, calls, fail: (value: boolean) => { failing=value }, stop: () => scope.stop() }
}

describe('competition notification management', () => {
  test('publishes from its own page and retains the draft on failure', async () => {
    const app = harness()
    app.state.title.value=' Title '
    app.state.body.value=' Body '
    app.fail(true)
    await app.state.save()
    expect(app.state.title.value).toBe(' Title ')
    expect(app.state.formError.value).toBe('rejected')
    app.fail(false)
    await app.state.save()
    const created = app.calls.filter(call=>call.operation==='create').at(-1)!
    expect(created.options.body).toEqual({ title:'Title',body:'Body',audience:'Participants' })
    expect(app.state.title.value).toBe('')
    app.stop()
  })
  test('edits existing content without changing its audience and withdraws after confirmation', async () => {
    const app = harness()
    const target={id:'notice',state:'Published',title:'Old',body:'Original',audience:'Collaborators'}
    app.state.edit(target)
    app.state.title.value='New'
    await app.state.save()
    const edit=app.calls.find(call=>call.operation==='edit')!
    expect(edit.options.path).toEqual({competitionId:'competition',announcementId:'notice'})
    expect(edit.options.body).toEqual({title:'New',body:'Original'})
    app.state.requestDelete(target)
    app.fail(true)
    await app.state.remove()
    expect(app.state.deleteTarget.value).toEqual(target)
    app.fail(false)
    await app.state.remove()
    expect(app.state.deleteTarget.value).toBeNull()
    app.state.includeWithdrawn.value=true
    await drain()
    expect(app.calls.filter(call=>call.operation==='list').at(-1)!.options.query.includeWithdrawn).toBe(true)
    app.stop()
  })
  test('observers and withdrawn rows never invoke mutation endpoints', async () => {
    const app=harness(false)
    app.state.title.value='Title';app.state.body.value='Body'
    await app.state.save()
    app.state.edit({id:'notice',state:'Published'})
    app.state.requestDelete({id:'notice',state:'Published'})
    await app.state.remove()
    expect(app.calls.filter(call=>call.operation!=='list')).toHaveLength(0)
    app.stop()
    const manager=harness()
    manager.state.edit({id:'notice',state:'Withdrawn'})
    expect(manager.state.editingId.value).toBeNull()
    manager.stop()
  })
})

test('announcements own the composer and Runtime Flags remain inside detail sheets', async () => {
  const shell=await sourceFile(new URL('../app/pages/admin/competitions/[id].vue',import.meta.url)).text()
  expect(shell).toContain('`${base}/announcements`')
  expect(shell).not.toContain('announcementOpen')
  const notice=await sourceFile(new URL('../app/pages/admin/competitions/[id]/announcements.vue',import.meta.url)).text()
  expect(notice).toContain('adminListCompetitionAnnouncements')
  expect(notice).toContain('adminUpdateCompetitionAnnouncement')
  expect(notice).toContain('adminDeleteCompetitionAnnouncement')
  for (const path of ['../app/pages/admin/platform/runtimes.vue','../app/pages/admin/competitions/[id]/runtimes.vue']) {
    const runtime=await sourceFile(new URL(path,import.meta.url)).text()
    expect(runtime).not.toContain('openFlagQuery')
    expect(runtime).toContain(':is="RuntimeFlagsPanel"')
  }
})
