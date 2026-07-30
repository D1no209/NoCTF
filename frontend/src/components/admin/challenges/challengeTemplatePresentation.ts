import type {
  NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse,
  NoCtfDomainChallengesChallengeVisibility,
  NoCtfDomainCompetitionsGameMode,
} from '@/api/generated/types.gen'

type ChallengeTemplateLifecycle = Pick<
  NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse,
  'activeCompetitionReferenceCount' | 'deletedAt'
>

export const CHALLENGE_MODE = {
  ctf: 0,
  awd: 1,
  awdp: 2,
  koh: 3,
} as const satisfies Record<string, NoCtfDomainCompetitionsGameMode>

export const CHALLENGE_VISIBILITY = {
  private: 0,
  shared: 1,
} as const satisfies Record<string, NoCtfDomainChallengesChallengeVisibility>

export function challengeModeLabelKey(mode?: NoCtfDomainCompetitionsGameMode | null) {
  switch (mode) {
    case CHALLENGE_MODE.ctf:
      return 'admin.challenges.modeCtf'
    case CHALLENGE_MODE.awd:
      return 'admin.challenges.modeAwd'
    case CHALLENGE_MODE.awdp:
      return 'admin.challenges.modeAwdp'
    case CHALLENGE_MODE.koh:
      return 'admin.challenges.modeKoh'
    default:
      return 'admin.challenges.modeUnknown'
  }
}

export function challengeVisibilityLabelKey(
  visibility?: NoCtfDomainChallengesChallengeVisibility | null,
) {
  switch (visibility) {
    case CHALLENGE_VISIBILITY.private:
      return 'admin.challenges.visibilityPrivate'
    case CHALLENGE_VISIBILITY.shared:
      return 'admin.challenges.visibilityShared'
    default:
      return 'admin.challenges.visibilityUnknown'
  }
}

export function isDeletedTemplate(template: ChallengeTemplateLifecycle) {
  return template.deletedAt != null
}

export function canDeleteTemplate(template: ChallengeTemplateLifecycle) {
  return !isDeletedTemplate(template) && template.activeCompetitionReferenceCount === 0
}

export function canRestoreTemplate(template: ChallengeTemplateLifecycle) {
  return isDeletedTemplate(template)
}
