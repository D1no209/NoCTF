import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

describe('challenge flag match kind editor', () => {
  test('uses the generated match-kind contract only in the challenge-bank editor', async () => {
    const templatePage = await sourceFile(
      new URL('../app/pages/admin/challenges/[id].vue', import.meta.url),
    ).text()
    const competitionPage = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/challenges/[ccId].vue', import.meta.url),
    ).text()

    expect(templatePage).toContain('NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagMatchKindProtocol')
    expect(templatePage).toContain('matchKind: flagForm')
    expect(templatePage).toContain('supportsRegularExpression')
    expect(templatePage).toContain("template.value?.mode === 'Ctf'")
    expect(templatePage).toContain('!usesRuntimeFlagInjection.value')
    expect(templatePage).toContain('value="RegularExpression"')
    expect(templatePage).not.toContain("common.description.regularExpressionMatchesEntire")
    expect(templatePage).toContain('usesRuntimeFlagInjection')
    expect(templatePage).toContain("administration.challengesBy.description.challengeUsesRuntimeManaged")
    expect(templatePage).not.toContain('flagForm.teamId')
    expect(templatePage).not.toContain('flagForm.specificationKind')
    expect(templatePage).not.toContain('flagForm.specificationId')
    expect(templatePage).not.toContain('flagForm.validStart')
    expect(templatePage).not.toContain('flagForm.validUntil')

    expect(competitionPage).not.toContain('ChallengeFlagMatchKindProtocol')
    expect(competitionPage).not.toContain('flagForm')
    expect(competitionPage).not.toContain('TabsTrigger value="flags"')
  })

  test('uses generated typed flag failures and keeps creation inline', async () => {
    const templatePage = await sourceFile(
      new URL('../app/pages/admin/challenges/[id].vue', import.meta.url),
    ).text()
    const competitionPage = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/challenges/[ccId].vue', import.meta.url),
    ).text()

    expect(templatePage).toContain('NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagFailureResponse')
    expect(templatePage).toContain('challengeFlagErrorMessage')
    expect(competitionPage).not.toContain('ChallengeFlagFailureResponse')
    expect(competitionPage).not.toContain('challengeFlagErrorMessage')
    const create = templatePage.slice(
      templatePage.indexOf('async function createFlag'),
      templatePage.indexOf('async function confirmDeleteFlag'),
    )
    expect(templatePage).toContain('<UiForm validation="feature" @submit.prevent="createFlag">')
    expect(templatePage).not.toContain('<Dialog v-model:open="flagCreateOpen">')
    expect(create.indexOf("flagForm.flag = ''")).toBeGreaterThan(create.indexOf('if (error)'))
    const confirmation = templatePage.slice(
      templatePage.indexOf('<AlertDialog :open="!!deletingFlag"'),
      templatePage.indexOf('<AlertDialog v-model:open="transferOpen">'),
    )
    expect(confirmation).toContain('<Button variant="destructive"')
    expect(confirmation).not.toContain('<AlertDialogAction')
  })
})

describe('attachment delivery editor', () => {
  test('keeps the delete target alive until the asynchronous request succeeds', async () => {
    const page = await sourceFile(
      new URL('../app/pages/admin/challenges/[id].vue', import.meta.url),
    ).text()
    const confirmation = page.slice(
      page.indexOf('<AlertDialog :open="!!deletingAttachment"'),
      page.indexOf('<Dialog v-model:open="randomBatchOpen">'),
    )
    const deletion = page.slice(
      page.indexOf('async function confirmDeleteAttachment'),
      page.indexOf('async function restoreAttachment'),
    )

    expect(confirmation).toContain('<Button variant="destructive"')
    expect(confirmation).not.toContain('<AlertDialogAction')
    expect(deletion).toContain('adminChallengeBankDeleteAttachment({')
    expect(deletion.indexOf('deletingAttachment.value = null'))
      .toBeGreaterThan(deletion.indexOf('if (error)'))
  })

  test('uses the generated random-batch SDK and keeps selected input after failure', async () => {
    const page = await sourceFile(
      new URL('../app/pages/admin/challenges/[id].vue', import.meta.url),
    ).text()
    const upload = page.slice(
      page.indexOf('async function uploadRandomBatch'),
      page.indexOf('function randomAttachmentErrorMessage'),
    )

    expect(page).toContain('adminChallengeBankUploadAttachments')
    expect(page).toContain('NoCtfapiEndpointsAdministrationChallengeBankAttachmentBatchFailureResponse')
    expect(page).toContain('multiple class="hidden" @change="selectRandomFiles"')
    expect(page).toContain('file.name')
    expect(page).toContain('attachment.exactFlag')
    expect(page).toContain("$t('administration.label.hash')")
    expect(page).toContain("attachment.sha256?.slice(0, 8) ?? $t('common.label.symbol')")
    expect(page).toContain(':content="attachment.sha256"')
    expect(page).toContain('id="attachment-delivery-policy"')
    expect(page).toContain('requestAttachmentDeliveryPolicy')
    expect(page).toContain('value="All"')
    expect(page).toContain('value="RandomOnePerTeam"')
    expect(upload.indexOf('randomFiles.value = []')).toBeGreaterThan(upload.indexOf('if (error)'))
    expect(upload.indexOf('randomBatchOpen.value = false')).toBeGreaterThan(upload.indexOf('if (error)'))
  })

  test('renders one player download entry for random delivery and all entries otherwise', async () => {
    const page = await sourceFile(
      new URL('../app/features/challenges/CompetitionChallengeDetail.vue', import.meta.url),
    ).text()

    expect(page).toContain("attachmentDeliveryPolicy === 'RandomOnePerTeam'")
    expect(page).toContain('prepareRandomChallengeAttachmentDownloadEndpoint')
    expect(page).toContain('v-for="attachment in attachments"')
    expect(page).not.toContain("common.description.firstDownloadAssignsOne")
  })
})
