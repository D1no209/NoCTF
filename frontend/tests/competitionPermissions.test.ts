import { describe, expect, test } from 'bun:test'
import {
  assignCompetitionPermission,
  buildCompetitionPermissionUpdateRequest,
  competitionPermissionMembers,
  eligibleCompetitionPermissionCandidates,
  hasCompetitionPermissionChanges,
  normalizeCompetitionPermissionCandidates,
  normalizeCompetitionPermissionSnapshot,
  removeCompetitionPermission,
} from '../src/components/admin/competition-detail/competitionPermissions'
import en from '../src/locales/en.json'
import zhCN from '../src/locales/zh-CN.json'

const competitionId = '00000000-0000-0000-0000-000000000010'
const ownerId = '00000000-0000-0000-0000-000000000001'
const managerId = '00000000-0000-0000-0000-000000000002'
const judgeId = '00000000-0000-0000-0000-000000000003'
const observerId = '00000000-0000-0000-0000-000000000004'
const extraId = '00000000-0000-0000-0000-000000000005'

const validSnapshot = {
  competitionId,
  ownerId,
  managerIds: [managerId],
  judgeIds: [judgeId],
  observerIds: [observerId],
  permissionRevision: 7,
}

describe('competition permissions', () => {
  test('keeps the permission workflow localized without legacy collaborator copy', () => {
    const keys = [
      'navPermissions',
      'permissionsTitle',
      'permissionsDescription',
      'permissionsLoading',
      'permissionsRestrictedTitle',
      'permissionsRestrictedDescription',
      'permissionsNotFoundTitle',
      'permissionsNotFoundDescription',
      'permissionsLoadErrorTitle',
      'permissionsLoadErrorDescription',
      'permissionsInvalidTitle',
      'permissionsInvalidDescription',
      'permissionsOwner',
      'permissionsOwnerDescription',
      'permissionsRoleManager',
      'permissionsRoleManagerDescription',
      'permissionsRoleJudge',
      'permissionsRoleJudgeDescription',
      'permissionsRoleObserver',
      'permissionsRoleObserverDescription',
      'permissionsAddMember',
      'permissionsSelectCandidate',
      'permissionsNoCandidates',
      'permissionsUnknownMember',
      'permissionsMoveTo',
      'permissionsRemove',
      'permissionsUpdateSuccess',
      'permissionsUpdateError',
      'permissionsConflictRevision',
      'permissionsConflictEmailNotVerified',
      'permissionsConflictRolesOverlap',
      'permissionsConflictOwnerIncluded',
      'permissionsConflictUserNotFound',
      'permissionsConflictRoleNotEligible',
    ] as const
    const english = en.admin.competitionDetail as Record<string, unknown>
    const chinese = zhCN.admin.competitionDetail as Record<string, unknown>

    for (const key of keys) {
      expect(typeof english[key]).toBe('string')
      expect(typeof chinese[key]).toBe('string')
    }

    expect('collaborators' in en.admin).toBe(false)
    expect('collaborators' in en.admin.nav).toBe(false)
    expect('collaborators' in zhCN.admin).toBe(false)
    expect('collaborators' in zhCN.admin.nav).toBe(false)
    expect(typeof en.admin.competitions.permissions).toBe('string')
    expect(typeof zhCN.admin.competitions.permissions).toBe('string')
  })

  test('normalizes UUIDs and deterministically de-duplicates each role', () => {
    expect(
      normalizeCompetitionPermissionSnapshot({
        competitionId: competitionId.toUpperCase(),
        ownerId: ownerId.toUpperCase(),
        managerIds: [` ${extraId.toUpperCase()} `, managerId, extraId],
        judgeIds: [judgeId],
        observerIds: [observerId],
        permissionRevision: 7,
      }),
    ).toEqual({
      competitionId,
      ownerId,
      managerIds: [managerId, extraId],
      judgeIds: [judgeId],
      observerIds: [observerId],
      permissionRevision: 7,
    })
  })

  test.each([
    [{ ...validSnapshot, competitionId: undefined }, 'missing competition'],
    [{ ...validSnapshot, competitionId: 'not-a-uuid' }, 'invalid competition'],
    [{ ...validSnapshot, ownerId: undefined }, 'missing owner'],
    [{ ...validSnapshot, managerIds: undefined }, 'missing managers'],
    [{ ...validSnapshot, judgeIds: undefined }, 'missing judges'],
    [{ ...validSnapshot, observerIds: undefined }, 'missing observers'],
    [{ ...validSnapshot, permissionRevision: undefined }, 'missing revision'],
    [{ ...validSnapshot, permissionRevision: -1 }, 'negative revision'],
    [{ ...validSnapshot, permissionRevision: 1.5 }, 'fractional revision'],
    [{ ...validSnapshot, managerIds: [''] }, 'empty UUID'],
    [{ ...validSnapshot, managerIds: ['not-a-uuid'] }, 'invalid UUID'],
    [
      { ...validSnapshot, managerIds: ['00000000-0000-0000-0000-000000000000'] },
      'empty GUID',
    ],
    [{ ...validSnapshot, managerIds: [ownerId] }, 'owner assigned to a role'],
    [{ ...validSnapshot, observerIds: [managerId] }, 'cross-role overlap'],
  ])('fails closed for %s (%s)', (input) => {
    expect(normalizeCompetitionPermissionSnapshot(input)).toBeNull()
  })

  test('adds, moves, and removes against the complete role sets', () => {
    const added = assignCompetitionPermission(validSnapshot, extraId, 'manager')
    expect(added).toEqual({
      ...validSnapshot,
      managerIds: [managerId, extraId],
    })

    const moved = assignCompetitionPermission(added!, judgeId, 'observer')
    expect(moved).toEqual({
      ...validSnapshot,
      managerIds: [managerId, extraId],
      judgeIds: [],
      observerIds: [judgeId, observerId],
    })

    expect(removeCompetitionPermission(moved!, managerId)).toEqual({
      ...validSnapshot,
      managerIds: [extraId],
      judgeIds: [],
      observerIds: [judgeId, observerId],
    })
  })

  test('rejects invalid or owner mutations and detects no-op edits', () => {
    expect(assignCompetitionPermission(validSnapshot, ownerId, 'manager')).toBeNull()
    expect(assignCompetitionPermission(validSnapshot, 'invalid', 'manager')).toBeNull()
    expect(removeCompetitionPermission(validSnapshot, ownerId)).toBeNull()

    const sameAssignment = assignCompetitionPermission(validSnapshot, managerId, 'manager')
    const absentRemoval = removeCompetitionPermission(validSnapshot, extraId)
    const moved = assignCompetitionPermission(validSnapshot, managerId, 'judge')

    expect(hasCompetitionPermissionChanges(validSnapshot, sameAssignment!)).toBe(false)
    expect(hasCompetitionPermissionChanges(validSnapshot, absentRemoval!)).toBe(false)
    expect(hasCompetitionPermissionChanges(validSnapshot, moved!)).toBe(true)
  })

  test('normalizes complete candidates and rejects incomplete or ambiguous records', () => {
    expect(
      normalizeCompetitionPermissionCandidates({
        items: [
          {
            id: extraId.toUpperCase(),
            userName: ' Zeta ',
            kind: 1,
            role: 1,
            emailVerified: false,
          },
          {
            id: managerId,
            userName: 'alpha',
            kind: 0,
            role: 2,
            emailVerified: true,
          },
        ],
      }),
    ).toEqual([
      {
        id: managerId,
        userName: 'alpha',
        kind: 0,
        role: 2,
        emailVerified: true,
      },
      {
        id: extraId,
        userName: 'Zeta',
        kind: 1,
        role: 1,
        emailVerified: false,
      },
    ])

    expect(normalizeCompetitionPermissionCandidates({})).toBeNull()
    expect(
      normalizeCompetitionPermissionCandidates({
        items: [{ id: extraId, userName: 'missing metadata' }],
      }),
    ).toBeNull()
    expect(
      normalizeCompetitionPermissionCandidates({
        items: [
          { id: extraId, userName: 'one', kind: 0, role: 1, emailVerified: true },
          { id: extraId.toUpperCase(), userName: 'two', kind: 1, role: 2, emailVerified: true },
        ],
      }),
    ).toBeNull()
  })

  test('enforces role-specific candidate eligibility and always excludes the owner', () => {
    const candidates = normalizeCompetitionPermissionCandidates({
      items: [
        { id: ownerId, userName: 'owner', kind: 0, role: 2, emailVerified: true },
        { id: managerId, userName: 'organizer human', kind: 0, role: 1, emailVerified: false },
        { id: judgeId, userName: 'administrator bot', kind: 1, role: 2, emailVerified: false },
        { id: observerId, userName: 'verified user', kind: 0, role: 0, emailVerified: true },
        { id: extraId, userName: 'unverified user', kind: 1, role: 0, emailVerified: false },
      ],
    })!

    expect(
      eligibleCompetitionPermissionCandidates(candidates, validSnapshot, 'manager').map(
        candidate => candidate.id,
      ),
    ).toEqual([judgeId, managerId])
    expect(
      eligibleCompetitionPermissionCandidates(candidates, validSnapshot, 'judge').map(
        candidate => candidate.id,
      ),
    ).toEqual([observerId])
    expect(
      eligibleCompetitionPermissionCandidates(candidates, validSnapshot, 'observer').map(
        candidate => candidate.id,
      ),
    ).toEqual([observerId])
  })

  test('keeps assigned IDs when the candidate directory omits their metadata', () => {
    const candidates = normalizeCompetitionPermissionCandidates({
      items: [
        {
          id: managerId,
          userName: 'known manager',
          kind: 0,
          role: 1,
          emailVerified: true,
        },
      ],
    })!

    expect(competitionPermissionMembers(validSnapshot, candidates, 'manager')).toEqual([
      {
        id: managerId,
        candidate: candidates[0],
      },
    ])
    expect(competitionPermissionMembers(validSnapshot, candidates, 'judge')).toEqual([
      {
        id: judgeId,
        candidate: null,
      },
    ])

    expect(buildCompetitionPermissionUpdateRequest(validSnapshot)).toEqual({
      managerIds: [managerId],
      judgeIds: [judgeId],
      observerIds: [observerId],
      expectedPermissionRevision: 7,
    })
  })
})
