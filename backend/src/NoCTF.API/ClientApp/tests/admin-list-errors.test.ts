import { describe, expect, test } from 'bun:test'

async function pageSource(path: string): Promise<string> {
  return Bun.file(new URL(`../app/pages/${path}`, import.meta.url)).text()
}

describe('admin list error presentation', () => {
  test.each([
    'admin/platform/audit.vue',
    'admin/platform/logs.vue',
    'admin/competitions/[id]/submissions.vue',
    'admin/competitions/[id]/runtimes.vue',
  ])('%s exposes cursor failures without replacing them with an empty state', async (path) => {
    const source = await pageSource(path)

    expect(source).toContain('error: listError')
    expect(source).toContain('<Alert v-if="listError" variant="destructive">')
    expect(source).toContain('!listError && initialized && items.length === 0')
    expect(source).toMatch(/(?:Card|template) v-else-if="items\.length > 0"/)
    expect(source).toContain('reset({ preserveItems: true })')
  })

  test('team appeals preserve the previous result and expose refresh failures', async () => {
    const source = await pageSource('admin/competitions/[id]/teams.vue')

    expect(source).toContain('appealsError.value = parseApiError(error).message')
    expect(source).toContain('appeals.value = data.items ?? []')
    expect(source).toContain('<Alert v-if="appealsError" variant="destructive">')
    expect(source).toContain('!appealsError && appeals.length === 0')
    expect(source).toContain('v-else-if="appeals.length > 0"')
  })

  test('competition exports preserve the previous result and expose refresh failures', async () => {
    const source = await pageSource('admin/competitions/[id]/exports.vue')

    expect(source).toContain('exportsError.value = parseApiError(error).message')
    expect(source).toContain('exports_.value = data.items ?? []')
    expect(source).toContain('<Alert v-if="exportsError" variant="destructive">')
    expect(source).toContain('!exportsError && exports_.length === 0')
    expect(source).toContain('v-else-if="exports_.length > 0"')
  })

  test('challenge flags and hints keep loaded rows visible beside load failures', async () => {
    const source = await pageSource('admin/competitions/[id]/challenges/[ccId].vue')

    for (const section of ['flags', 'hints']) {
      expect(source).toContain(`${section}LoadError.value = parseApiError(error).message`)
      expect(source).toContain(`<Alert v-if="${section}LoadError" variant="destructive">`)
    }
    expect(source).toContain('!flagsLoadError && staticFlags.length === 0 && systemFlags.length === 0')
    expect(source).toContain('!flagsLoading && staticFlags.length > 0')
    expect(source).toContain('!flagsLoading && systemFlags.length > 0')
    expect(source).toContain('!hintsLoadError && hints.length === 0')
    expect(source).toContain('v-else-if="hints.length > 0"')
  })

  test('platform audit exports do not turn an initial failure into no tasks', async () => {
    const source = await pageSource('admin/platform/audit.vue')

    expect(source).toContain('exportsError.value = parseApiError(error).message')
    expect(source).toContain('<Alert v-if="exportsError" variant="destructive"')
    expect(source).toContain('!exportsError && exports_.length === 0')
  })
})
