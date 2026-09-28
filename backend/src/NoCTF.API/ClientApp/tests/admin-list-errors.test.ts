import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

async function pageSource(path: string): Promise<string> {
  return sourceFile(new URL(`../app/pages/${path}`, import.meta.url)).text()
}

describe('admin list error presentation', () => {
  test.each([
    'admin/platform/audit.vue',
    'admin/platform/logs.vue',
    'admin/competitions/[id]/submissions.vue',
  ])('%s exposes cursor failures without replacing them with an empty state', async (path) => {
    const source = await pageSource(path)

    expect(source).toContain('error: listError')
    expect(source).toContain('<Alert v-if="listError" variant="destructive">')
    expect(source).toContain('!listError && initialized && items.length === 0')
    expect(source).toMatch(/(?:Card|template) v-else-if="items\.length > 0"/)
    expect(source).toContain('reset({ preserveItems: true })')
  })

  test('competition runtimes keep the current offset page visible after a refresh failure', async () => {
    const source = await pageSource('admin/competitions/[id]/runtimes.vue')

    expect(source).toContain('error: listError')
    expect(source).toContain('<Alert v-if="listError" variant="destructive">')
    expect(source).toContain('!listError && initialized && items.length === 0')
    expect(source).toContain('v-else-if="items.length > 0"')
    expect(source).toContain('createTrailingRefresh(() => pagination.loadPage())')
    expect(source).toContain('<OffsetPagination v-if="initialized"')
  })

  test('team appeals preserve the previous result and expose refresh failures', async () => {
    const source = await pageSource('admin/competitions/[id]/teams.vue')

    expect(source).toContain('appealsError.value = parseApiError(error).message')
    expect(source).toContain('appeals.value = data.items ?? []')
    expect(source).toContain('<Alert v-if="appealsError" variant="destructive">')
    expect(source).toContain('!appealsError && appeals.length === 0')
    expect(source).toContain('v-else-if="appeals.length > 0"')
  })

  test('competition archives download directly and preserve the form on failure', async () => {
    const source = await pageSource('admin/competitions/[id]/exports.vue')

    expect(source).toContain('adminExportCompetitionArchive({')
    expect(source).toContain('if (exportingArchive.value) return')
    expect(source).toContain('exportReason.value = \'\'')
    expect(source).toContain('toast.error(parseApiError(e).message)')
    const catchBody = source.slice(source.indexOf('catch (e)'), source.indexOf('finally'))
    expect(catchBody).not.toContain('exportReason.value = \'\'')
  })

  test('competition challenge hints keep loaded rows visible beside load failures', async () => {
    const source = await pageSource('admin/competitions/[id]/challenges/[ccId].vue')

    expect(source).toContain('hintsLoadError.value = parseApiError(error).message')
    expect(source).toContain('<Alert v-if="hintsLoadError" variant="destructive">')
    expect(source).toContain('!hintsLoadError && hints.length === 0')
    expect(source).toContain('v-else-if="hints.length > 0"')
    expect(source).not.toContain('flagsLoadError')
  })

  test('platform audit archives download directly without an asynchronous task list', async () => {
    const source = await pageSource('admin/platform/audit.vue')

    expect(source).toContain('adminExportPlatformAuditArchive({')
    expect(source).toContain('if (exportingArchive.value) return')
    expect(source).toContain('toast.error(parseApiError(e).message)')
    expect(source).not.toContain('exports_.value')
  })
})
