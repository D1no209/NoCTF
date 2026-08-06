import type { NoCtfapiEndpointsTeamsGlobalTeamResponse } from './generated/types.gen'

export interface GlobalTeam {
  id: string
  name: string
  avatarUrl: string | null
  captainId: string
  memberIds: string[]
  memberCount: number
  invitationToken: string | null
  createdAt: string
}

function requireString(value: string | undefined, field: string) {
  if (typeof value !== 'string' || value.length === 0)
    throw new TypeError(`Global team response is missing ${field}.`)
  return value
}

export function toGlobalTeam(value: NoCtfapiEndpointsTeamsGlobalTeamResponse): GlobalTeam {
  if (!Array.isArray(value.memberIds) || value.memberIds.some(
    id => typeof id !== 'string' || id.length === 0,
  )) {
    throw new TypeError('Global team response is missing memberIds.')
  }
  if (value.avatarUrl !== undefined && value.avatarUrl !== null && typeof value.avatarUrl !== 'string')
    throw new TypeError('Global team response has an invalid avatarUrl.')
  if (
    value.invitationToken !== undefined
    && value.invitationToken !== null
    && typeof value.invitationToken !== 'string'
  ) {
    throw new TypeError('Global team response has an invalid invitationToken.')
  }

  return {
    id: requireString(value.id, 'id'),
    name: requireString(value.name, 'name'),
    avatarUrl: value.avatarUrl ?? null,
    captainId: requireString(value.captainId, 'captainId'),
    memberIds: [...value.memberIds],
    memberCount: value.memberIds.length,
    invitationToken: value.invitationToken ?? null,
    createdAt: requireString(value.createdAt, 'createdAt'),
  }
}
