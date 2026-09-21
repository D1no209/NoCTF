import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

describe('participant copy density', () => {
  test('keeps challenge actions concise without repeating their behavior', async () => {
    const [detail, runtime, awd, awdp, koh] = await Promise.all([
      sourceFile(new URL('../app/features/challenges/CompetitionChallengeDetail.vue', import.meta.url)).text(),
      sourceFile(new URL('../app/features/challenges/RuntimeCard.vue', import.meta.url)).text(),
      sourceFile(new URL('../app/features/challenges/panels/AwdPanel.vue', import.meta.url)).text(),
      sourceFile(new URL('../app/features/challenges/panels/AwdpPanel.vue', import.meta.url)).text(),
      sourceFile(new URL('../app/features/challenges/panels/KohPanel.vue', import.meta.url)).text(),
    ])

    expect(detail).not.toContain("$t('ui.question')")
    expect(detail).not.toContain("$t('ui.thereAreNoAttachmentsForThisQuestion')")
    expect(runtime).not.toContain("ui.theEnvironmentHasNotBeenStartedYetClickStartEnvironment")
    expect(awd).not.toContain("ui.awdModeProtectYourOwnServiceFromBeingAttackedAnd")
    expect(awdp).not.toContain("ui.submitOneDynamicFlagObtainedFromTheCurrentAttackInstance")
    expect(koh).not.toContain("ui.kohModeCaptureTheHillAndMaintainControlToScore")
  })

  test('does not explain invitation-token implementation beside the action', async () => {
    const teamPage = await sourceFile(
      new URL('../app/pages/competitions/[id]/my/team.vue', import.meta.url),
    ).text()

    expect(teamPage).not.toContain("ui.theInvitationCodeIsGeneratedWhenCreatingATeamFor")
    expect(teamPage).toContain('getTeamInvitationEndpoint({')
    expect(teamPage).toContain('invitationToken.value = data.invitationToken')
    expect(teamPage).toContain('v-else-if="invitationToken"')
    expect(teamPage).toContain("$t('ui.rotateInvitationCode')")
    expect(teamPage).toContain('v-if="isCaptain && !team.isBanned"')
    expect(teamPage).toContain(':can-manage="isCaptain && !team.isBanned && canEditOrganization"')
    expect(teamPage).toContain('v-if="!team.isBanned"')
    expect(teamPage.match(/<Card>/g)).toHaveLength(1)
  })

  test('uses one continuous challenge work surface instead of nested floating cards', async () => {
    const [detail, runtime, flag, fix, awd, awdp, koh] = await Promise.all([
      sourceFile(new URL('../app/features/challenges/CompetitionChallengeDetail.vue', import.meta.url)).text(),
      sourceFile(new URL('../app/features/challenges/RuntimeCard.vue', import.meta.url)).text(),
      sourceFile(new URL('../app/features/challenges/FlagSubmit.vue', import.meta.url)).text(),
      sourceFile(new URL('../app/features/challenges/FixSubmit.vue', import.meta.url)).text(),
      sourceFile(new URL('../app/features/challenges/panels/AwdPanel.vue', import.meta.url)).text(),
      sourceFile(new URL('../app/features/challenges/panels/AwdpPanel.vue', import.meta.url)).text(),
      sourceFile(new URL('../app/features/challenges/panels/KohPanel.vue', import.meta.url)).text(),
    ])

    for (const source of [detail, runtime, flag, fix, awd, awdp, koh]) {
      expect(source).not.toContain('<Card')
    }
    expect(detail).toContain('class="border-b py-5"')
    expect(awdp).toContain('xl:border-l')
    expect(koh).toContain('md:divide-x')
  })
})
