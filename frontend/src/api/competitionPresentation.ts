import type {
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
  NoCtfDomainCompetitionsCompetitionStatus,
  NoCtfDomainCompetitionsGameMode,
} from './generated/types.gen'

export type PublicCompetitionMode = 'ctf' | 'awd' | 'awdp' | 'koh'
export type PublicCompetitionStatus
  = | 'draft'
    | 'visible'
    | 'published'
    | 'running'
    | 'paused'
    | 'finished'

export interface PublicCompetition {
  id: string
  title: string
  description: string | null
  mode: PublicCompetitionMode
  startTime: string
  endTime: string
  status: PublicCompetitionStatus
  teamRegistrationAutoApprove: boolean
  maxTeamMembers: number
  ownerId: string
}

const competitionModes: Record<NoCtfDomainCompetitionsGameMode, PublicCompetitionMode> = {
  0: 'ctf',
  1: 'awd',
  2: 'awdp',
  3: 'koh',
}

const competitionStatuses: Record<NoCtfDomainCompetitionsCompetitionStatus, PublicCompetitionStatus> = {
  0: 'draft',
  1: 'visible',
  2: 'published',
  3: 'running',
  4: 'paused',
  5: 'finished',
}

function requireString(value: string | undefined, field: string) {
  if (typeof value !== 'string' || value.length === 0)
    throw new TypeError(`Competition response is missing ${field}.`)
  return value
}

export function toPublicCompetition(
  value: NoCtfapiEndpointsCompetitionsCompetitionResponse,
): PublicCompetition {
  const mode = value.mode === undefined ? undefined : competitionModes[value.mode]
  const status = value.status === undefined ? undefined : competitionStatuses[value.status]

  if (!mode)
    throw new TypeError('Competition response has an unsupported mode.')
  if (!status)
    throw new TypeError('Competition response has an unsupported status.')
  if (typeof value.teamRegistrationAutoApprove !== 'boolean')
    throw new TypeError('Competition response is missing teamRegistrationAutoApprove.')
  if (typeof value.maxTeamMembers !== 'number')
    throw new TypeError('Competition response is missing maxTeamMembers.')

  return {
    id: requireString(value.id, 'id'),
    title: requireString(value.title, 'title'),
    description: value.description ?? null,
    mode,
    startTime: requireString(value.startTime, 'startTime'),
    endTime: requireString(value.endTime, 'endTime'),
    status,
    teamRegistrationAutoApprove: value.teamRegistrationAutoApprove,
    maxTeamMembers: value.maxTeamMembers,
    ownerId: requireString(value.ownerId, 'ownerId'),
  }
}
