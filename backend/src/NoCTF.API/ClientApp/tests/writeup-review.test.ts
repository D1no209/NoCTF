import { expect, test } from 'bun:test'

const sourceFile = (path: string) => Bun.file(new URL(path, import.meta.url))

test('participant WriteUp flow uses generated PDF upload preview and download operations', async () => {
  const controller = await sourceFile(
    '../app/features/routes/competitions/[id]/my/useCompetitionsByIdMyWriteUpPage.ts',
  ).text()
  const view = await sourceFile(
    '../app/components/views/page/competitions/[id]/my/CompetitionsByIdMyWriteUpPageView.vue',
  ).text()
  const navigation = await sourceFile(
    '../app/features/routes/competitions/useCompetitionsByIdPage.ts',
  ).text()

  expect(controller).toContain('replaceMyTeamWriteUp')
  expect(controller).toContain('downloadMyTeamWriteUp')
  expect(controller).toContain("!== '%PDF-'")
  expect(view).toContain('accept="application/pdf,.pdf"')
  expect(view).toContain('<PdfPreview')
  expect(navigation).toContain('/my/writeup')
})

test('staff review combines sandboxed PDF preview with authoritative challenge score actions', async () => {
  const controller = await sourceFile(
    '../app/features/routes/competitions/[id]/useCompetitionsByIdWriteUpsPage.ts',
  ).text()
  const view = await sourceFile(
    '../app/components/views/page/competitions/[id]/CompetitionsByIdWriteUpsPageView.vue',
  ).text()
  const preview = await sourceFile(
    '../app/components/ui/pdf-preview/PdfPreview.vue',
  ).text()

  expect(controller).toContain('listTeamWriteUps')
  expect(controller).toContain('adminCreateManualAdjustment')
  expect(controller).toContain('await adjustScore(-points)')
  expect(view).toContain('selected.challengeScores')
  expect(view).toContain('openDeduction(score)')
  expect(preview).toContain(":sandbox=\"''\"")
})

test('WriteUp reviewers can start a team consultation and continue in the existing workspace', async () => {
  const controller = await sourceFile(
    '../app/features/routes/competitions/[id]/useCompetitionsByIdWriteUpsPage.ts',
  ).text()

  expect(controller).toContain('createTeamWriteUpConsultation')
  expect(controller).toContain("path: `/competitions/${competitionId}/questions`")
  expect(controller).toContain('query: { question: data.threadRootId }')
})
