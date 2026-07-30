import type { ChallengeTemplate, PlatformUser } from '@/api/noctf'
import { isBot, PLATFORM_USER_ROLE } from '@/components/admin/users/platformUserPresentation'

export interface ChallengeGitOpsAccessUpdate {
  challengeId: string
  managerIds: string[]
  expectedRevision: number
}

export function existingChallengeManagerIds(template: ChallengeTemplate) {
  return [
    ...new Set((template.managerIds ?? []).filter(id => Boolean(id) && id !== template.ownerId)),
  ].sort()
}

export function gitOpsBotCandidates(users: PlatformUser[], template: ChallengeTemplate) {
  if (!template.ownerId || !Array.isArray(template.managerIds))
    return []

  const existingManagers = new Set(existingChallengeManagerIds(template))

  return users
    .filter(
      user =>
        typeof user.id === 'string'
        && user.id.length > 0
        && isBot(user)
        && user.role === PLATFORM_USER_ROLE.organizer
        && user.id !== template.ownerId
        && !existingManagers.has(user.id),
    )
    .sort((left, right) =>
      (left.userName || left.id || '').localeCompare(right.userName || right.id || ''),
    )
}

export function buildChallengeGitOpsAccessUpdate(
  template: ChallengeTemplate,
  botId: string,
): ChallengeGitOpsAccessUpdate | null {
  if (
    !template.id
    || !template.ownerId
    || !Array.isArray(template.managerIds)
    || typeof template.revision !== 'number'
    || !botId
    || botId === template.ownerId
  ) {
    return null
  }

  return {
    challengeId: template.id,
    managerIds: [...new Set([...existingChallengeManagerIds(template), botId])].sort(),
    expectedRevision: template.revision,
  }
}
