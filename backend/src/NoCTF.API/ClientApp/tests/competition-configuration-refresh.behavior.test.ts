import { expect, test } from 'bun:test'
import { computed, effectScope, markRaw, nextTick, ref, watch } from 'vue'
import { testAdminResource } from './support/admin-competition-resource'

async function compile(path: string, name: string, dependencies: Record<string, unknown>) {
  const source = await Bun.file(new URL(`../app/${path}`, import.meta.url)).text()
  let code = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
    .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
  code = code.replace(/export function /g, 'function ') + `; return ${name};`
  for (const match of source.matchAll(/import (\w+) from '[^']+\.vue'/g)) dependencies[match[1]!] = {}
  return new Function('deps', `const { ${Object.keys(dependencies).join(', ')} } = deps; ${code}`)(dependencies)
}

test('a realtime competition refresh keeps the description being edited and still updates untouched fields', async () => {
  let reads = 0
  const resource = testAdminResource(async ({ path }) => {
    reads++
    return {
      data: {
        competition: {
          id: path.competitionId,
          title: reads === 1 ? 'Server title' : 'Updated title',
          description: 'Server description',
          accessMode: 'Public',
        },
        modeConfiguration: { competitionId: path.competitionId, mode: 'Ctf' },
      },
    }
  })
  const mounted: Array<() => unknown> = []
  const context = {
    competitionId: 'contest',
    competition: resource.competition,
    canWrite: ref(true),
    refresh: resource.refresh,
    accept: resource.accept,
    data: resource.data,
    read: resource.read,
  }
  const deps: Record<string, unknown> = {
    computed, ref, watch, markRaw,
    onMounted: (callback: () => unknown) => mounted.push(callback),
    onUnmounted: () => {},
    useCompetitionAdmin: () => context,
    describeMessage: (key: string) => key,
    toast: { success: () => {}, error: () => {} },
    isoToLocalInput: (value: unknown) => (value == null ? '' : String(value)),
    normalizeConfigValue: (value: unknown) => JSON.stringify(value ?? null),
    shouldHydrateMetaField: (previous: Record<string, unknown> | null, current: Record<string, unknown>, key: string, force = false) =>
      force || previous === null || current[key] === previous[key],
  }
  const scope = effectScope()
  try {
    const controller = await compile('features/routes/admin/competitions/[id]/useAdminCompetitionsByIdConfigurationPage.ts', 'useAdminCompetitionsByIdConfigurationPage', deps)
    const page = scope.run(() => controller())!
    for (const callback of mounted) await callback()
    await nextTick()
    expect(page.description.value).toBe('Server description')
    expect(page.title.value).toBe('Server title')

    // 用户正在编辑描述,标题保持服务器值。
    page.description.value = 'Typing in the editor'
    await context.refresh()
    await nextTick()

    expect(page.description.value).toBe('Typing in the editor')
    expect(page.title.value).toBe('Updated title')
    void reads
  }
  finally { scope.stop() }
})
