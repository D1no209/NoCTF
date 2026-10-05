import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import { emptyPlatformRuntimeFilters, platformRuntimeQuery } from '../app/utils/platform-runtime-filters'

describe('platform runtime administration', () => {
  test('adds an administrator-only global container inventory to the platform workspace', async () => {
    const shell = await sourceFile(
      new URL('../app/pages/admin/platform.vue', import.meta.url),
    ).text().then(source => source.replace(/\r\n/g, '\n'))
    const page = await sourceFile(
      new URL('../app/pages/admin/platform/runtimes.vue', import.meta.url),
    ).text().then(source => source.replace(/\r\n/g, '\n'))

    expect(shell).toContain("'/admin/platform/runtimes'")
    expect(page).toContain("definePageMeta({ middleware: 'platform-admin', path: '/admin/platform/runtimes/:runtimeId?'")
    expect(page).toContain('adminPlatformListActiveRuntimes')
    expect(page).toContain('adminTerminateRuntime')
    expect(page).toContain('adminCreateRuntimeForceTermination')
    expect(page).toContain('item.runtime?.competitionId')
    expect(page).toContain('v-else-if="detail.runtime?.challengeId"')
    expect(page).toContain('`/admin/challenges/${detail.runtime.challengeId}`')
    expect(page).toContain('useOffsetPagination<PlatformRuntime>')
    expect(page).toContain('setInterval(() => void refresh(), 10_000)')
    expect(page).not.toContain("fetch('/api")
  })

  test('snapshots applied server filters independently from form edits', () => {
    const filters = emptyPlatformRuntimeFilters()
    expect(platformRuntimeQuery(filters)).toEqual({ search: undefined, scope: undefined, state: undefined, runtimeKind: undefined, offset: 0, desc: true })
    Object.assign(filters, { search: '  soul  ', scope: 'Competition', state: 'Running', kind: 'Container' })
    const applied = platformRuntimeQuery(filters)
    filters.search = 'another team'
    filters.state = 'Stopping'
    expect(applied).toEqual({ search: 'soul', scope: 'Competition', state: 'Running', runtimeKind: 'Container', offset: 0, desc: true })
    expect(platformRuntimeQuery(filters).state).toBe('Stopping')
  })

  test('uses a compact filter form, wrapping cells and a detail sheet instead of an oversized card table', async () => {
    const page = await sourceFile(new URL('../app/pages/admin/platform/runtimes.vue', import.meta.url)).text().then(source => source.replace(/\r\n/g, '\n'))
    const workspace = await sourceFile(new URL('../app/features/app/AppWorkspaceNav.vue', import.meta.url)).text().then(source => source.replace(/\r\n/g, '\n'))
    expect(workspace).toContain('<ChoiceSidebar')
    expect(workspace).toContain('class="settings-workspace-layout"')
    expect(workspace).not.toContain('<SidebarInset')
    expect(page).toContain('<UiForm @submit.prevent="applyFilters">')
    expect(page).toContain('query: { ...appliedQuery.value, offset, limit, desc }')
    expect(page).toContain('appliedQuery.value = platformRuntimeQuery(filters)\n    pagination.reset()')
    expect(page).toContain('<OffsetPagination')
    expect(page).toContain('whitespace-normal break-words')
    expect(page).toContain('<SheetTitle>')
    expect(page).not.toContain('min-w-64')
    expect(page).not.toContain('<Card v-else-if="items.length > 0"')
  })

  test('keeps destructive runtime targets until the generated SDK request completes', async () => {
    const page = await sourceFile(
      new URL('../app/pages/admin/platform/runtimes.vue', import.meta.url),
    ).text().then(source => source.replace(/\r\n/g, '\n'))

    expect(page).toContain(':open="terminateTarget !== null"')
    expect(page).toContain('if (!open && !viewState.terminatePending) viewState.terminateTarget = null')
    expect(page).toContain(':open="forceTerminateTarget !== null"')
    expect(page).toContain('if (!open && !viewState.forceTerminatePending) viewState.forceTerminateTarget = null')
    expect(page).toContain('terminateTarget.value = null')
    expect(page).toContain('forceTerminateTarget.value = null')
    expect(page).toContain('terminationError.value = parseApiError(requestError).displayMessage')
    expect(page).toContain('forceTerminationError.value = parseApiError(requestError).displayMessage')
    expect(page).toContain('<Alert v-if="terminationError" variant="destructive">')
    expect(page).toContain('<Alert v-if="forceTerminationError" variant="destructive">')
  })
})
