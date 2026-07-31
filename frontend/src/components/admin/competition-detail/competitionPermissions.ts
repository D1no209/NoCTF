import type {
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionCandidateListResponse,
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionsResponse,
  NoCtfapiEndpointsAdministrationCompetitionsUpdateCompetitionPermissionsRequest,
  NoCtfDomainIdentityUserKind,
  NoCtfDomainIdentityUserRole,
} from '@/api/generated/types.gen'

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const EMPTY_UUID = '00000000-0000-0000-0000-000000000000'

const USER_KIND = {
  human: 0,
  bot: 1,
} as const satisfies Record<string, NoCtfDomainIdentityUserKind>

const USER_ROLE = {
  user: 0,
  organizer: 1,
  administrator: 2,
} as const satisfies Record<string, NoCtfDomainIdentityUserRole>

export type CompetitionPermissionRole = 'manager' | 'judge' | 'observer'

export interface CompetitionPermissionsSnapshot {
  competitionId: string
  ownerId: string
  managerIds: string[]
  judgeIds: string[]
  observerIds: string[]
  permissionRevision: number
}

export interface CompetitionPermissionCandidate {
  id: string
  userName: string
  kind: NoCtfDomainIdentityUserKind
  role: NoCtfDomainIdentityUserRole
  emailVerified: boolean
}

export interface CompetitionPermissionMember {
  id: string
  candidate: CompetitionPermissionCandidate | null
}

function normalizeUuid(value: unknown) {
  if (typeof value !== 'string')
    return null

  const normalized = value.trim().toLowerCase()
  if (!UUID_PATTERN.test(normalized) || normalized === EMPTY_UUID)
    return null

  return normalized
}

function normalizeIds(value: unknown) {
  if (!Array.isArray(value))
    return null

  const ids: string[] = []
  for (const item of value) {
    const id = normalizeUuid(item)
    if (!id)
      return null
    ids.push(id)
  }

  return [...new Set(ids)].sort()
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isUserKind(value: unknown): value is NoCtfDomainIdentityUserKind {
  return value === USER_KIND.human || value === USER_KIND.bot
}

function isUserRole(value: unknown): value is NoCtfDomainIdentityUserRole {
  return (
    value === USER_ROLE.user
    || value === USER_ROLE.organizer
    || value === USER_ROLE.administrator
  )
}

function compareCandidates(
  left: CompetitionPermissionCandidate,
  right: CompetitionPermissionCandidate,
) {
  const leftName = left.userName.toLocaleLowerCase()
  const rightName = right.userName.toLocaleLowerCase()
  if (leftName < rightName)
    return -1
  if (leftName > rightName)
    return 1
  return left.id.localeCompare(right.id)
}

function idsForRole(snapshot: CompetitionPermissionsSnapshot, role: CompetitionPermissionRole) {
  switch (role) {
    case 'manager':
      return snapshot.managerIds
    case 'judge':
      return snapshot.judgeIds
    case 'observer':
      return snapshot.observerIds
  }
}

function sameIds(left: string[], right: string[]) {
  return left.length === right.length && left.every((id, index) => id === right[index])
}

export function normalizeCompetitionPermissionSnapshot(
  input:
    | NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionsResponse
    | null
    | undefined,
): CompetitionPermissionsSnapshot | null {
  if (!isRecord(input))
    return null

  const competitionId = normalizeUuid(input.competitionId)
  const ownerId = normalizeUuid(input.ownerId)
  const managerIds = normalizeIds(input.managerIds)
  const judgeIds = normalizeIds(input.judgeIds)
  const observerIds = normalizeIds(input.observerIds)
  const permissionRevision = input.permissionRevision

  if (
    !competitionId
    || !ownerId
    || !managerIds
    || !judgeIds
    || !observerIds
    || typeof permissionRevision !== 'number'
    || !Number.isInteger(permissionRevision)
    || permissionRevision < 0
  ) {
    return null
  }

  const assignedIds = [...managerIds, ...judgeIds, ...observerIds]
  if (assignedIds.includes(ownerId) || new Set(assignedIds).size !== assignedIds.length)
    return null

  return {
    competitionId,
    ownerId,
    managerIds,
    judgeIds,
    observerIds,
    permissionRevision,
  }
}

export function normalizeCompetitionPermissionCandidates(
  input:
    | NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionCandidateListResponse
    | null
    | undefined,
): CompetitionPermissionCandidate[] | null {
  if (!isRecord(input) || !Array.isArray(input.items))
    return null

  const candidates: CompetitionPermissionCandidate[] = []
  const candidateIds = new Set<string>()

  for (const item of input.items) {
    if (!isRecord(item))
      return null

    const id = normalizeUuid(item.id)
    const userName = typeof item.userName === 'string' ? item.userName.trim() : ''
    if (
      !id
      || !userName
      || !isUserKind(item.kind)
      || !isUserRole(item.role)
      || typeof item.emailVerified !== 'boolean'
      || candidateIds.has(id)
    ) {
      return null
    }

    candidateIds.add(id)
    candidates.push({
      id,
      userName,
      kind: item.kind,
      role: item.role,
      emailVerified: item.emailVerified,
    })
  }

  return candidates.sort(compareCandidates)
}

export function eligibleCompetitionPermissionCandidates(
  candidates: CompetitionPermissionCandidate[],
  snapshot: CompetitionPermissionsSnapshot,
  targetRole: CompetitionPermissionRole,
) {
  return candidates
    .filter((candidate) => {
      if (candidate.id === snapshot.ownerId)
        return false

      if (targetRole === 'manager') {
        return (
          (candidate.kind === USER_KIND.human || candidate.kind === USER_KIND.bot)
          && (
            candidate.role === USER_ROLE.organizer
            || candidate.role === USER_ROLE.administrator
          )
        )
      }

      return candidate.emailVerified
    })
    .sort(compareCandidates)
}

export function competitionPermissionMembers(
  snapshot: CompetitionPermissionsSnapshot,
  candidates: CompetitionPermissionCandidate[],
  role: CompetitionPermissionRole,
): CompetitionPermissionMember[] {
  const candidatesById = new Map(candidates.map(candidate => [candidate.id, candidate]))
  return idsForRole(snapshot, role).map(id => ({
    id,
    candidate: candidatesById.get(id) ?? null,
  }))
}

export function assignCompetitionPermission(
  snapshot: CompetitionPermissionsSnapshot,
  userId: string,
  targetRole: CompetitionPermissionRole,
): CompetitionPermissionsSnapshot | null {
  const normalizedSnapshot = normalizeCompetitionPermissionSnapshot(snapshot)
  const normalizedUserId = normalizeUuid(userId)
  if (!normalizedSnapshot || !normalizedUserId || normalizedUserId === normalizedSnapshot.ownerId)
    return null

  const nextSnapshot: CompetitionPermissionsSnapshot = {
    ...normalizedSnapshot,
    managerIds: normalizedSnapshot.managerIds.filter(id => id !== normalizedUserId),
    judgeIds: normalizedSnapshot.judgeIds.filter(id => id !== normalizedUserId),
    observerIds: normalizedSnapshot.observerIds.filter(id => id !== normalizedUserId),
  }

  idsForRole(nextSnapshot, targetRole).push(normalizedUserId)
  nextSnapshot.managerIds.sort()
  nextSnapshot.judgeIds.sort()
  nextSnapshot.observerIds.sort()
  return nextSnapshot
}

export function removeCompetitionPermission(
  snapshot: CompetitionPermissionsSnapshot,
  userId: string,
): CompetitionPermissionsSnapshot | null {
  const normalizedSnapshot = normalizeCompetitionPermissionSnapshot(snapshot)
  const normalizedUserId = normalizeUuid(userId)
  if (!normalizedSnapshot || !normalizedUserId || normalizedUserId === normalizedSnapshot.ownerId)
    return null

  return {
    ...normalizedSnapshot,
    managerIds: normalizedSnapshot.managerIds.filter(id => id !== normalizedUserId),
    judgeIds: normalizedSnapshot.judgeIds.filter(id => id !== normalizedUserId),
    observerIds: normalizedSnapshot.observerIds.filter(id => id !== normalizedUserId),
  }
}

export function hasCompetitionPermissionChanges(
  baseline: CompetitionPermissionsSnapshot,
  current: CompetitionPermissionsSnapshot,
) {
  return (
    !sameIds(baseline.managerIds, current.managerIds)
    || !sameIds(baseline.judgeIds, current.judgeIds)
    || !sameIds(baseline.observerIds, current.observerIds)
  )
}

export function buildCompetitionPermissionUpdateRequest(
  snapshot: CompetitionPermissionsSnapshot,
): NoCtfapiEndpointsAdministrationCompetitionsUpdateCompetitionPermissionsRequest | null {
  const normalizedSnapshot = normalizeCompetitionPermissionSnapshot(snapshot)
  if (!normalizedSnapshot)
    return null

  return {
    managerIds: [...normalizedSnapshot.managerIds],
    judgeIds: [...normalizedSnapshot.judgeIds],
    observerIds: [...normalizedSnapshot.observerIds],
    expectedPermissionRevision: normalizedSnapshot.permissionRevision,
  }
}
