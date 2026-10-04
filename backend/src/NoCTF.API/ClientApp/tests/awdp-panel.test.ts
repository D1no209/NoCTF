import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

describe('AWDP participant panel', () => {
  test('separates team attack runtime from one-shot fix verification', async () => {
    const source = await sourceFile(
      new URL('../app/features/challenges/panels/AwdpPanel.vue', import.meta.url),
    ).text()
    const types = await sourceFile(
      new URL('../app/api/types.gen.ts', import.meta.url),
    ).text()

    expect(source).toContain("challenges.label.attackTargetBreakEnvironment")
    expect(source).toContain("challenges.label.defenseFix")
    expect(source).toContain("<component :is=\"RuntimeCard\"")
    expect(source).toContain(":controls=\"state?.breakActivation ? 'readonly' : 'full'\"")
    expect(source).toContain("<component :is=\"FlagSubmit\"")
    expect(source).toContain("<component :is=\"FixSubmit\"")
    expect(source).toContain('state?.maximumFixAttempts !== null')
    expect(source).toContain('state.remainingFixAttempts ?? 0')
    expect(source).toContain("$t('common.label.submissionsRemaining'")
    expect(types).toContain('maximumFixAttempts?: number | null')
    expect(types).toContain('acceptedFixAttempts?: number')
    expect(types).toContain('remainingFixAttempts?: number | null')
    expect(source).not.toContain("common.description.createIsolatedAttackInstance")
    expect(source).not.toContain("common.description.defenseIndependentBreakPlatform")
    expect(source).not.toContain('Fix 历史')
  })

  test('models defense as an explicit one-shot target with one upload', async () => {
    const source = await sourceFile(
      new URL('../app/features/challenges/FixSubmit.vue', import.meta.url),
    ).text()

    expect(source).toContain('requestAwdpDefenseTargetEndpoint')
    expect(source).toContain('uploadPatchEndpoint')
    expect(source).toContain("props.defense?.runtimeState === 'Queued'")
    expect(source).toContain("props.defense?.runtimeState === 'Provisioning'")
    expect(source).toContain("props.defense?.runtimeState === 'Running'")
    expect(source).toContain('!hasGameplayFact.value')
    expect(source).toContain("challenges.label.defenseEnvironment")
    expect(source).toContain("challenges.label.uploadFixPackage")
    expect(source).toContain("challenges.fixSubmit.description.defenseEnvironmentStartingUpload")
    expect(source).toContain("challenges.fixSubmit.description.fixUploadedVerificationStart")
    expect(source).toContain('patchWaitingForTarget')
    expect(source).toContain("challenges.label.verifyingFix")
    expect(source).toContain("challenges.fixSubmit.label.fixVerificationComplete")
    expect(source).not.toContain("common.label.provisionCleanVerificationTarget")
    expect(source).not.toContain("common.label.applyPatch")
    expect(source).not.toContain("common.label.runCheckerOnce")
    expect(source).not.toContain('submitFixEndpoint')
  })

  test('does not expose AWD service-state or hardening language', async () => {
    const source = await sourceFile(
      new URL('../app/features/challenges/panels/AwdpPanel.vue', import.meta.url),
    ).text()

    expect(source).not.toContain('硬化')
    expect(source).not.toContain('Hardening')
    expect(source).not.toContain('服务 Up')
    expect(source).not.toContain('服务 Down')
    expect(source).not.toContain("challenges.challengeTemplate.validation.awdRuntimesFormat.challengeTemplateError")
    expect(source).not.toContain('AwdRotation')
  })

  test('keeps participant-state failures visible and retryable', async () => {
    const source = await sourceFile(
      new URL('../app/features/challenges/panels/AwdpPanel.vue', import.meta.url),
    ).text()

    expect(source).toContain('stateError.value = parseApiError(error')
    expect(source).toContain("challenges.awdpPanel.error.loadAwdpChallengeFailed")
    expect(source).toContain('statePollingTimedOut')
    expect(source).toContain('@click="refreshAndPoll"')
  })

  test('locks scoring changes but keeps read-only Break validation after first success', async () => {
    const source = await sourceFile(
      new URL('../app/features/challenges/panels/AwdpPanel.vue', import.meta.url),
    ).text()

    expect(source).toContain("<component :is=\"FlagSubmit\"")
    expect(source).toContain(":title=\"state?.breakActivation ? $t('challenges.label.checkFlag') : $t('challenges.label.submitFlag')\"")
    expect(source).toContain(':read-only-judgement="!!state?.breakActivation"')
    expect(source).not.toContain("common.description.breakLockedFurtherFlags")
    expect(source).not.toContain("common.description.breakLockedFurtherFlags")
    expect(source).toContain('<p v-if="state?.fixActivation"')
    expect(source).toContain('role="status"')
    expect(source).toContain("<component :is=\"FixSubmit\"")
    expect(source).toContain("challenges.awdpPanel.validation.defenseLockedRequired")
    expect(source).toContain("challenges.awdpPanel.validation.defenseLockedRequired")
  })

  test('surfaces stable success-lock failures from generated SDK contracts', async () => {
    const flag = await sourceFile(
      new URL('../app/features/challenges/FlagSubmit.vue', import.meta.url),
    ).text()
    const fix = await sourceFile(
      new URL('../app/features/challenges/FixSubmit.vue', import.meta.url),
    ).text()

    expect(flag).toContain('NoCtfapiEndpointsGameplayFactsGameplayFactAdmissionFailureCodeProtocol')
    expect(flag).toContain('judgeAwdpBreakFlag')
    expect(flag).toContain('readOnlyJudgement')
    expect(flag).toContain("code === 'AchievementAlreadySucceeded'")
    expect(flag).toContain("challenges.flagSubmit.description.breakAlreadySucceededVerification")
    expect(flag).toContain("challenges.flagSubmit.description.flagCorrectCheckCreates")
    expect(fix).toContain('DefenseAlreadySucceeded')
    expect(fix).toContain("common.fixSubmit.description.fixAlreadySucceededFurther")
  })

  test('keeps challenge details in the workspace and removes the duplicate Fix history surface', async () => {
    const challengePage = sourceFile(
      new URL('../app/pages/competitions/[id]/challenges/[[ccId]].vue', import.meta.url),
    )
    const historyPage = sourceFile(
      new URL('../app/pages/competitions/[id]/challenges/[ccId]/fix-history.vue', import.meta.url),
    )
    const historyComponent = sourceFile(
      new URL('../app/components/challenges/AwdpFixHistory.vue', import.meta.url),
    )
    const panel = await sourceFile(
      new URL('../app/features/challenges/panels/AwdpPanel.vue', import.meta.url),
    ).text()
    const challengeDetail = await sourceFile(
      new URL('../app/features/challenges/CompetitionChallengeDetail.vue', import.meta.url),
    ).text()
    const obsoleteNestedParent = sourceFile(
      new URL('../app/pages/competitions/[id]/challenges/[ccId].vue', import.meta.url),
    )

    expect(await challengePage.exists()).toBe(true)
    expect(await historyPage.exists()).toBe(false)
    expect(await historyComponent.exists()).toBe(false)
    expect(await obsoleteNestedParent.exists()).toBe(false)
    expect(panel).not.toContain('<AwdpFixHistory')
    expect(panel).not.toContain('fixHistory')
    expect(challengeDetail).toContain("<component :is=\"ChallengeSubmissionHistory\"")
  })

  test('labels the AWDP checker as a one-shot Fix verifier', async () => {
    const source = await sourceFile(
      new URL('../app/features/admin/DefinitionCheckerSection.vue', import.meta.url),
    ).text()
    const awdpBranch = source.slice(source.indexOf("mode === 'Awdp' || mode === 'Ctf'"))

    expect(awdpBranch).toContain("administration.definitionChecker.label.oneShotFixVerification")
    expect(awdpBranch).toContain("administration.definitionChecker.description.enableOneShotFix.checkerSectionView")
    expect(awdpBranch).not.toContain("administration.label.enablePeriodicServiceChecks")
    expect(awdpBranch).not.toContain("administration.label.checkerServiceHealthCheck")
  })

  test('shows safe defense outcomes in the unified challenge history', async () => {
    const panel = await sourceFile(
      new URL('../app/features/challenges/panels/AwdpPanel.vue', import.meta.url),
    ).text()
    const history = await sourceFile(
      new URL('../app/features/challenges/ChallengeSubmissionHistory.vue', import.meta.url),
    ).text()

    expect(panel).toContain("challenges.error.defenseExploitSucceededFailed")
    expect(panel).toContain("challenges.error.defenseServiceAbnormalFailed")
    expect(panel).not.toContain('防御未通过')
    expect(panel).not.toContain("common.error.reasonFailed")
    expect(history).toContain('listGameplayFactsEndpoint')
    expect(history).toContain('competitionChallengeId')
    expect(history).not.toContain('kind:')
    expect(history).not.toContain('/api/v1/')
  })
})
