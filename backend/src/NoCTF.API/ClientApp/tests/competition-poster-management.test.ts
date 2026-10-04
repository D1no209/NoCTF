import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

test('public competition posters use versioned URLs while private posters use authenticated reads', async () => {
  const overview = await sourceFile('app/features/competitions/useCompetitionOverview.ts').text()
  const view = await sourceFile('app/components/views/competitions/CompetitionOverviewView.vue').text()
  const cover = await sourceFile('app/components/ui/media/CoverImage.vue').text()
  const contract = await sourceFile('../Endpoints/Competitions/GetCompetitionEndpoint.cs').text()
  const endpoint = await sourceFile('../Endpoints/Competitions/GetCompetitionPosterEndpoint.cs').text()

  expect(overview).toContain("competition.value.accessMode === 'StaffOnly'")
  expect(overview).toContain('? protectedPoster.posterUrl.value : competition.value.posterUrl ?? null')
  expect(overview).toContain('useCompetitionPoster(competitionId)')
  expect(overview).toContain('posterActorId !== user.value?.userId')
  expect(view).toContain('<CoverImage :src="posterUrl"')
  expect(view).not.toContain(':pending="posterLoading"')
  expect(cover.match(/pointer-events-none/g)?.length).toBeGreaterThanOrEqual(3)
  expect(contract).toContain('poster?revision={posterFileId:N}')
  expect(endpoint).toContain('public,max-age=31536000,immutable')
  expect(endpoint).toContain('requestedFileId == file.FileId')
})

test('competition administration previews replaces and removes the current poster', async () => {
  const controller = await sourceFile('app/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdIndexPage.ts').text()
  const createController = await sourceFile('app/features/competitions/useCreateCompetitionDialog.ts').text()
  const view = await sourceFile('app/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdIndexPageView.vue').text()
  const mock = await sourceFile('mock/api.ts').text()

  expect(controller).toContain('adminCompetitionPosterReplace')
  expect(controller).toContain('adminCompetitionPosterClear')
  expect(controller).toContain('await refreshPoster()')
  expect(view).toContain(':src="posterUrl"')
  expect(view).toContain('@change="selectPoster"')
  expect(view).toContain("$t('administration.label.removeCompetitionPoster')")
  expect(createController).toContain('posterUrl: uploadedPoster.url')
  expect(mock).toContain("request.method === 'DELETE'")
})
