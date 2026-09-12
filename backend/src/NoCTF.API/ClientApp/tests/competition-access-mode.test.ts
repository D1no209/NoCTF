import { expect, test } from 'bun:test'
import { resolveCompetitionBrowser } from '../app/features/competitions/competition-browser'

test('hidden competitions retain their lifecycle group for authorized viewers', () => {
  const hidden = {
    id: 'hidden',
    title: 'Hidden',
    status: 'Running' as const,
    accessMode: 'StaffOnly' as const,
    endTime: '2026-10-01',
  }

  const browser = resolveCompetitionBrowser([hidden], 'hidden', null)

  expect(browser.group).toBe('running')
  expect(browser.selected?.id).toBe('hidden')
})

test('create and configuration flows send the selected access mode', async () => {
  const create = await Bun.file(new URL(
    '../app/features/competitions/useCreateCompetitionDialog.ts',
    import.meta.url,
  )).text()
  const configuration = await Bun.file(new URL(
    '../app/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdConfigurationPage.ts',
    import.meta.url,
  )).text()

  expect(create).toContain("accessMode: staffOnly.value ? 'StaffOnly' : 'Public'")
  expect(configuration).toContain("accessMode: staffOnly.value ? 'StaffOnly' : 'Public'")
})

test('authorized views label hidden competitions and react to audience invalidation', async () => {
  const sidebar = await Bun.file(new URL(
    '../app/components/views/competitions/CompetitionSidebarView.vue',
    import.meta.url,
  )).text()
  const overview = await Bun.file(new URL(
    '../app/components/views/competitions/CompetitionOverviewView.vue',
    import.meta.url,
  )).text()
  const workspace = await Bun.file(new URL(
    '../app/features/routes/competitions/useCompetitionsByIdPage.ts',
    import.meta.url,
  )).text()

  expect(sidebar).toContain("accessMode === 'StaffOnly'")
  expect(overview).toContain("accessMode === 'StaffOnly'")
  expect(workspace).toContain("event.kind === 'CompetitionAudienceChanged'")
  expect(workspace).toContain("router.replace('/competitions')")
})
