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

test('staff review combines Edge-compatible PDF preview with authoritative challenge score actions', async () => {
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
  expect(view).toContain('v-if="loadError && !review"')
  expect(view).toContain('v-else-if="review"')
  expect(preview).not.toContain('sandbox')
  expect(preview).toContain('referrerpolicy="no-referrer"')
})

test('WriteUp reviewers can start a team consultation and continue in the existing workspace', async () => {
  const controller = await sourceFile(
    '../app/features/routes/competitions/[id]/useCompetitionsByIdWriteUpsPage.ts',
  ).text()

  expect(controller).toContain('createTeamWriteUpConsultation')
  expect(controller).toContain("path: `/competitions/${competitionId}/questions`")
  expect(controller).toContain('query: { question: data.threadRootId }')
})

test('competition configuration separates required WriteUps from submission availability', async () => {
  const configuration = await sourceFile(
    '../app/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdConfigurationPage.ts',
  ).text()
  const configurationView = await sourceFile(
    '../app/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdConfigurationPageView.vue',
  ).text()
  const participant = await sourceFile(
    '../app/features/routes/competitions/[id]/my/useCompetitionsByIdMyWriteUpPage.ts',
  ).text()
  const participantView = await sourceFile(
    '../app/components/views/page/competitions/[id]/my/CompetitionsByIdMyWriteUpPageView.vue',
  ).text()
  const navigation = await sourceFile(
    '../app/features/routes/competitions/useCompetitionsByIdPage.ts',
  ).text()

  expect(configuration).toContain('writeUpSubmissionDeadlineHours: writeUpSubmissionDeadlineHours.value')
  expect(configurationView).toContain('id="c-writeup-required"')
  expect(configurationView).toContain('id="c-writeup-deadline-hours"')
  expect(configurationView).not.toContain('<Field v-if="writeUpSubmissionRequired">')
  expect(participant).toContain('submissionClosed')
  expect(participant).not.toContain('!submissionRequired.value || submissionClosed.value')
  expect(participantView).toContain('v-if="!submissionClosed"')
  expect(participantView).toContain("$t('writeUp.notRequiredForCompetition')")
  expect(navigation).not.toContain('writeUpSubmissionRequired')
})
