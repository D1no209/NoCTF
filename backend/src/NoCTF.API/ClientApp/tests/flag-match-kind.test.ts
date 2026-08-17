import { describe, expect, test } from 'bun:test'

describe('challenge flag match kind editor', () => {
  test('uses the generated match-kind contract in template and competition editors', async () => {
    const templatePage = await Bun.file(
      new URL('../app/pages/admin/challenges/[id].vue', import.meta.url),
    ).text()
    const competitionPage = await Bun.file(
      new URL('../app/pages/admin/competitions/[id]/challenges/[ccId].vue', import.meta.url),
    ).text()

    for (const page of [templatePage, competitionPage]) {
      expect(page).toContain('NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagMatchKindProtocol')
      expect(page).toContain('matchKind: flagForm')
      expect(page).toContain('supportsRegularExpression')
      expect(page).toContain('value="RegularExpression"')
      expect(page).toContain('usesRuntimeFlagInjection')
      expect(page).toContain('无需维护精确或正则 Flag')
      expect(page).toContain('systemManaged')
      expect(page).toContain('系统生成的动态 Flag')
      expect(page).not.toContain('flagForm.teamId')
      expect(page).not.toContain('flagForm.specificationKind')
      expect(page).not.toContain('flagForm.specificationId')
      expect(page).not.toContain('flagForm.validStart')
      expect(page).not.toContain('flagForm.validUntil')
    }
  })

  test('uses generated typed flag failures and preserves the open form on failure', async () => {
    const templatePage = await Bun.file(
      new URL('../app/pages/admin/challenges/[id].vue', import.meta.url),
    ).text()
    const competitionPage = await Bun.file(
      new URL('../app/pages/admin/competitions/[id]/challenges/[ccId].vue', import.meta.url),
    ).text()

    for (const page of [templatePage, competitionPage]) {
      expect(page).toContain('NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagFailureResponse')
      expect(page).toContain('challengeFlagErrorMessage')
    }
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
      new URL('../app/pages/competitions/[id]/challenges/[ccId].vue', import.meta.url),
    ).text()

    expect(page).toContain("attachmentDeliveryPolicy === 'RandomOnePerTeam'")
    expect(page).toContain('downloadRandomChallengeAttachmentEndpoint')
    expect(page).toContain('v-for="attachment in attachments"')
    expect(page).toContain('首次下载会为本队随机分配一个附件')
  })
})
