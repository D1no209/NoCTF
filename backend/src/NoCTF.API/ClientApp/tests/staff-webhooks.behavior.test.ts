import { expect, test } from 'bun:test'
import { computed, reactive, ref, shallowRef } from 'vue'

const source = new Bun.Transpiler({ loader: 'ts' }).transformSync(await Bun.file(new URL('../app/features/competitions/staff-webhooks/useStaffWebhooks.ts', import.meta.url)).text())
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')

function harness(canManage = true) {
  const mounted: Array<() => void> = []; const disposed: Array<() => void> = []; const calls: any[] = []
  let probe: () => Promise<boolean> = async () => false
  const makePage = (fetch: any) => {
    const page = { page: ref(1), limit: ref(20), pageCount: ref(1), total: ref(0), items: ref<any[]>([]), loading: ref(false), error: ref(null),
      reset: () => {}, setPageSize: async () => {}, loadPage: async (_: number) => {
        const result = await fetch({ offset: 0, limit: 20, desc: true }); page.items.value = result.items; page.total.value = result.total
      } }
    return page
  }
  const target = { id: 'subscription-1', name: 'Staff', endpointUrl: canManage ? 'https://bot.example/staff/key' : null, categories: ['Consultation'], enabled: true }
  const dependencies = { computed, reactive, ref, shallowRef,
    useCompetitionAdmin: () => ({ competitionId: 'competition-1', canWrite: ref(canManage) }), useOffsetPagination: makePage,
    onMounted: (fn: () => void) => mounted.push(fn), onBeforeUnmount: (fn: () => void) => disposed.push(fn),
    message: (key: string) => ({ key }), toast: { success: () => {}, error: () => {} }, parseApiError: () => ({ displayMessage: 'error' }),
    usePolling: (fn: () => Promise<boolean>) => { probe = fn; return { start: () => {}, stop: () => {}, timedOut: ref(false) } },
    adminListStaffWebhooks: async () => ({ data: { canManage, items: [target], total: 1 } }),
    adminCreateStaffWebhook: async (request: any) => { calls.push(request); return { data: { target, signingSecret: 'TEST_SECRET_ONCE' } } },
    adminUpdateStaffWebhook: async (request: any) => { calls.push(request); return { data: { target } } },
    adminRotateStaffWebhookSecret: async (request: any) => { calls.push(request); return { data: { target, signingSecret: 'ROTATED_TEST_SECRET' } } },
    adminDeleteStaffWebhook: async (request: any) => { calls.push(request); return {} },
    adminTestStaffWebhook: async () => ({ data: { deliveryId: 'delivery-1' } }),
    adminGetStaffWebhookTest: async () => ({ data: { state: 2 } }),
    adminListStaffWebhookDeliveries: async () => ({ data: { items: [], total: 0 } }),
  }
  const state = new Function('dependencies', `const { ${Object.keys(dependencies).join(', ')} } = dependencies; ${source}; return useStaffWebhooks();`)(dependencies)
  return { state, target, calls, mount: async () => { for (const fn of mounted) fn(); await state.reload() }, dispose: () => disposed.forEach(fn => fn()), probe: () => probe() }
}

test('creation defaults to all categories and only exposes the returned secret until closed', async () => {
  const app = harness(); await app.mount(); app.state.create()
  expect(app.state.form.categories).toEqual(['CheatIncident', 'Consultation', 'BanAppeal'])
  app.state.form.name = ' Staff '; app.state.form.endpointUrl = ' https://bot.example/staff/key '
  await app.state.save()
  expect(app.calls[0].body.name).toBe('Staff')
  expect(app.calls[0].body.endpointUrl).toBe('https://bot.example/staff/key')
  expect(app.state.signingSecret.value).toBe('TEST_SECRET_ONCE')
  app.state.setSecretOpen(false); expect(app.state.signingSecret.value).toBeNull()
})

test('empty categories cannot submit and toggling a category cannot duplicate it', async () => {
  const app = harness(); await app.mount(); app.state.create()
  for (const kind of app.state.categories) app.state.toggleCategory(kind, false)
  await app.state.save(); expect(app.calls).toHaveLength(0)
  app.state.toggleCategory('Consultation', true); app.state.toggleCategory('Consultation', true)
  expect(app.state.form.categories).toEqual(['Consultation'])
})

test('read-only staff cannot mutate or reveal a signing secret', async () => {
  const app = harness(false); await app.mount()
  expect(app.state.mayManage.value).toBe(false); expect(app.state.page.items.value[0].endpointUrl).toBeNull()
  await app.state.save(); await app.state.rotate(app.target)
  app.state.requestDelete(app.target); await app.state.remove()
  expect(app.calls).toHaveLength(0); expect(app.state.signingSecret.value).toBeNull()
})

test('edit preserves categories, tests poll the generated SDK status endpoint, and unmount clears secrets', async () => {
  const app = harness(); await app.mount(); app.state.edit(app.target)
  expect(app.state.form.categories).toEqual(['Consultation'])
  await app.state.save(); expect(app.calls[0].path.targetId).toBe('subscription-1')
  await app.state.test(app.target); expect(await app.probe()).toBe(true); expect(app.state.testState.value).toBe(2)
  await app.state.rotate(app.target); app.dispose(); expect(app.state.signingSecret.value).toBeNull()
})
