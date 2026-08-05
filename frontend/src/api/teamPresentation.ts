import type {
  NoCtfapiEndpointsTeamsTeamResponse,
  NoCtfDomainTeamsTeamRegistrationStatus,
} from './generated/types.gen'

export type PublicTeamRegistrationStatus = 'pending' | 'approved' | 'rejected'

export interface PublicTeam {
  id: string
  competitionId: string
  name: string
  avatarUrl: string | null
  captainId: string
  memberIds: string[]
  memberCount: number
  registrationStatus: PublicTeamRegistrationStatus
  isLocked: boolean
  isBanned: boolean
  registeredAt: string
}

const registrationStatuses: Record<
  NoCtfDomainTeamsTeamRegistrationStatus,
  PublicTeamRegistrationStatus
> = {
  0: 'pending',
  1: 'approved',
  2: 'rejected',
}

function requireString(value: string | undefined, field: string) {
  if (typeof value !== 'string' || value.length === 0)
    throw new TypeError(`Team response is missing ${field}.`)
  return value
}

export function toPublicTeam(value: NoCtfapiEndpointsTeamsTeamResponse): PublicTeam {
  const registrationStatus = value.registrationStatus === undefined
    ? undefined
    : registrationStatuses[value.registrationStatus]

  if (!Array.isArray(value.memberIds) || value.memberIds.some(id => typeof id !== 'string' || id.length === 0))
    throw new TypeError('Team response is missing memberIds.')
  if (!registrationStatus)
    throw new TypeError('Team response has an unsupported registrationStatus.')
  if (typeof value.isLocked !== 'boolean')
    throw new TypeError('Team response is missing isLocked.')
  if (typeof value.isBanned !== 'boolean')
    throw new TypeError('Team response is missing isBanned.')
  if (value.avatarUrl !== undefined && value.avatarUrl !== null && typeof value.avatarUrl !== 'string')
    throw new TypeError('Team response has an invalid avatarUrl.')

  return {
    id: requireString(value.id, 'id'),
    competitionId: requireString(value.competitionId, 'competitionId'),
    name: requireString(value.name, 'name'),
    avatarUrl: value.avatarUrl ?? null,
    captainId: requireString(value.captainId, 'captainId'),
    memberIds: [...value.memberIds],
    memberCount: value.memberIds.length,
    registrationStatus,
    isLocked: value.isLocked,
    isBanned: value.isBanned,
    registeredAt: requireString(value.registeredAt, 'registeredAt'),
  }
}
