import { describe, expect, test } from 'bun:test'

describe('challenge flag match kind editor', () => {
  test('uses the generated match-kind contract only in the challenge-bank editor', async () => {
    const templatePage = await Bun.file(
      new URL('../app/pages/admin/challenges/[id].vue', import.meta.url),
    ).text()
    const competitionPage = await Bun.file(
      new URL('../app/pages/admin/competitions/[id]/challenges/[ccId].vue', import.meta.url),
    ).text()

    expect(templatePage).toContain('NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagMatchKindProtocol')
    expect(templatePage).toContain('matchKind: flagForm')
    expect(templatePage).toContain('supportsRegularExpression')
    expect(templatePage).toContain('value="RegularExpression"')
    expect(templatePage).toContain('usesRuntimeFlagInjection')
    expect(templatePage).toContain('无需维护精确或正则 Flag')
    expect(templatePage).not.toContain('flagForm.teamId')
    expect(templatePage).not.toContain('flagForm.specificationKind')
    expect(templatePage).not.toContain('flagForm.specificationId')
    expect(templatePage).not.toContain('flagForm.validStart')
    expect(templatePage).not.toContain('flagForm.validUntil')

    expect(competitionPage).not.toContain('ChallengeFlagMatchKindProtocol')
    expect(competitionPage).not.toContain('flagForm')
    expect(competitionPage).not.toContain('TabsTrigger value="flags"')
  })

  test('uses generated typed flag failures and preserves the open form on failure', async () => {
    const templatePage = await Bun.file(
      new URL('../app/pages/admin/challenges/[id].vue', import.meta.url),
    ).text()
    const competitionPage = await Bun.file(
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
    expect(create.indexOf('flagCreateOpen.value = false')).toBeGreaterThan(create.indexOf('if (error)'))
  })
})

describe('attachment delivery editor', () => {
  test('uses the generated random-batch SDK and keeps selected input after failure', async () => {
    const page = await Bun.file(
      new URL('../app/pages/admin/challenges/[id].vue', import.meta.url),
    ).text()
    const upload = page.slice(
      page.indexOf('async function uploadRandomBatch'),
      page.indexOf('function randomAttachmentErrorMessage'),
    )

    expect(page).toContain('adminChallengeBankUploadRandomAttachmentBatch')
    expect(page).toContain('NoCtfapiEndpointsAdministrationChallengeBankRandomAttachmentBatchFailureResponse')
    expect(page).toContain('multiple class="hidden" @change="selectRandomFiles"')
    expect(page).toContain('file.name')
    expect(page).toContain('attachment.exactFlag')
    expect(page).toContain("$t('哈希')")
    expect(page).toContain("attachment.sha256?.slice(0, 8) ?? '—'")
    expect(page).toContain(':title="attachment.sha256"')
    expect(page).toContain('id="attachment-delivery-policy"')
    expect(page).toContain('requestAttachmentDeliveryPolicy')
    expect(page).toContain('value="All"')
    expect(page).toContain('value="RandomOnePerTeam"')
    expect(upload.indexOf('randomFiles.value = []')).toBeGreaterThan(upload.indexOf('if (error)'))
    expect(upload.indexOf('randomBatchOpen.value = false')).toBeGreaterThan(upload.indexOf('if (error)'))
  })

  test('renders one player download entry for random delivery and all entries otherwise', async () => {
    const page = await Bun.file(
      new URL('../app/components/challenges/CompetitionChallengeDetail.vue', import.meta.url),
    ).text()

    expect(page).toContain("attachmentDeliveryPolicy === 'RandomOnePerTeam'")
    expect(page).toContain('downloadRandomChallengeAttachmentEndpoint')
    expect(page).toContain('v-for="attachment in attachments"')
    expect(page).not.toContain('首次下载会为本队随机分配一个附件')
  })
})
