import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

test('draft competition overviews load the uploaded poster without browser caching', async () => {
  const overview = await sourceFile('app/features/competitions/useCompetitionOverview.ts').text()
  const poster = await sourceFile('app/features/competitions/useCompetitionPoster.ts').text()
  const endpoint = await sourceFile('../Endpoints/Competitions/GetCompetitionPosterEndpoint.cs').text()

  expect(overview).toContain('useCompetitionPoster(competitionId)')
  const posterSetup = overview.slice(
    overview.indexOf('useCompetitionPoster(competitionId)'),
    overview.indexOf('async function refreshCompetition'),
  )
  expect(posterSetup).not.toContain('managementOnly')
  expect(poster).toContain("cache: 'no-store'")
  expect(poster).toContain('URL.revokeObjectURL')
  expect(endpoint).toContain('Response.Headers.CacheControl = "no-store"')
})

test('competition administration previews replaces and removes the current poster', async () => {
  const controller = await sourceFile('app/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdIndexPage.ts').text()
  const view = await sourceFile('app/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdIndexPageView.vue').text()
  const mock = await sourceFile('mock/api.ts').text()

  expect(controller).toContain('adminCompetitionPosterReplace')
  expect(controller).toContain('adminCompetitionPosterClear')
  expect(controller).toContain('await refreshPoster()')
  expect(view).toContain(':src="posterUrl"')
  expect(view).toContain('@change="selectPoster"')
  expect(view).toContain("$t('ui.removeCompetitionPoster')")
  expect(mock).toContain("request.method === 'DELETE'")
})
