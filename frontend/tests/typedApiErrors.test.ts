import { describe, expect, test } from 'bun:test'
import {
  ApiError,
  readChallengeTemplateConflict,
  readCompetitionPermissionsConflict,
  readPlatformRoleAssignmentBlockers,
} from '../src/api/noctf'

describe('typed API conflicts', () => {
  test('decodes platform role blockers and normalizes resource IDs', () => {
    const error = new ApiError('blocked', 409, {
      code: 'ActiveOwnerOrManagerAssignments',
      competitionIds: ['competition-b', 'competition-a', 'competition-a'],
      challengeIds: ['challenge-a'],
    })

    expect(readPlatformRoleAssignmentBlockers(error)).toEqual({
      competitionIds: ['competition-a', 'competition-b'],
      challengeIds: ['challenge-a'],
    })
    expect(readPlatformRoleAssignmentBlockers(new ApiError('bad request', 400, {}))).toBeNull()
    expect(
      readPlatformRoleAssignmentBlockers(
        new ApiError('different conflict', 409, { code: 'OtherConflict' }),
      ),
    ).toBeNull()
  })

  test('decodes challenge permission conflicts without accepting unknown codes', () => {
    expect(
      readChallengeTemplateConflict(
        new ApiError('conflict', 409, {
          code: 'RevisionConflict',
          userIds: ['user-b', 'user-a', 'user-a'],
        }),
      ),
    ).toEqual({
      code: 'RevisionConflict',
      userIds: ['user-a', 'user-b'],
    })
    expect(
      readChallengeTemplateConflict(
        new ApiError('conflict', 409, {
          code: 'UnknownConflict',
        }),
      ),
    ).toBeNull()
    expect(readChallengeTemplateConflict(new Error('network'))).toBeNull()
  })

  test('decodes only generated competition permission conflict codes from HTTP 409', () => {
    const codes = [
      'RevisionConflict',
      'EmailNotVerified',
      'RolesOverlap',
      'OwnerIncluded',
      'UserNotFound',
      'RoleNotEligible',
    ] as const

    for (const code of codes) {
      expect(
        readCompetitionPermissionsConflict(
          new ApiError('conflict', 409, {
            code,
            userIds: ['user-b', 'user-a', 'user-a'],
          }),
        ),
      ).toEqual({
        code,
        userIds: ['user-a', 'user-b'],
      })
    }

    expect(
      readCompetitionPermissionsConflict(
        new ApiError('conflict', 409, { code: 'UnknownConflict' }),
      ),
    ).toBeUndefined()
    expect(
      readCompetitionPermissionsConflict(
        new ApiError('RevisionConflict', 409, 'RevisionConflict'),
      ),
    ).toBeUndefined()
    expect(
      readCompetitionPermissionsConflict(
        new ApiError('conflict', 400, { code: 'RevisionConflict' }),
      ),
    ).toBeUndefined()
    expect(readCompetitionPermissionsConflict(new Error('RevisionConflict'))).toBeUndefined()
  })
})
