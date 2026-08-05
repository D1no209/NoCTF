import type {
  NoCtfapiEndpointsAdministrationTeamsAdminTeamBanCaseResponse,
  NoCtfapiEndpointsTeamsMyTeamBanCaseResponse,
  NoCtfDomainTeamsTeamBanAppealStatus,
  NoCtfDomainTeamsTeamBanSource,
} from './generated/types.gen'
import {
  adminAcceptTeamBanAppeal,
  adminCorrectTeamBan,
  adminListTeamBanAppeals,
  adminUpholdTeamBanAppeal,
  getMyTeamBanCase,
  submitTeamBanAppeal,
} from './generated/sdk.gen'
import { ApiError } from './noctf'

export type MyTeamBanCase = NoCtfapiEndpointsTeamsMyTeamBanCaseResponse
export type AdminTeamBanCase
  = NoCtfapiEndpointsAdministrationTeamsAdminTeamBanCaseResponse

export const TeamBanSource = {
  ManualModeration: 0,
  CheatIncident: 1,
} as const satisfies Record<string, NoCtfDomainTeamsTeamBanSource>

export const TeamBanAppealStatus = {
  Submitted: 0,
  Upheld: 1,
  Accepted: 2,
} as const satisfies Record<string, NoCtfDomainTeamsTeamBanAppealStatus>

interface GeneratedResult<T> {
  data?: T
  error?: unknown
  response?: Response
}

function unwrap<T>(result: GeneratedResult<T>, message: string): T {
  if (result.error)
    throw new ApiError(message, result.response?.status, result.error)
  return result.data as T
}

export const teamBanAppealApi = {
  async getMy(competitionId: string): Promise<MyTeamBanCase | null> {
    const result = await getMyTeamBanCase({ path: { competitionId } })
    if (result.response?.status === 404)
      return null
    return unwrap(result, 'Team ban case request failed.')
  },

  async submit(competitionId: string, statement: string): Promise<void> {
    unwrap(await submitTeamBanAppeal({
      path: { competitionId },
      body: { statement },
    }), 'Team ban appeal submission failed.')
  },

  async listAdmin(competitionId: string): Promise<AdminTeamBanCase[]> {
    const response = unwrap(await adminListTeamBanAppeals({
      path: { competitionId },
    }), 'Team ban appeal list request failed.')
    return response.items ?? []
  },

  async uphold(competitionId: string, appealId: string, reason: string): Promise<void> {
    unwrap(await adminUpholdTeamBanAppeal({
      path: { competitionId, appealId },
      body: { reason },
    }), 'Team ban appeal rejection failed.')
  },

  async accept(competitionId: string, appealId: string, reason: string): Promise<void> {
    unwrap(await adminAcceptTeamBanAppeal({
      path: { competitionId, appealId },
      body: { reason },
    }), 'Team ban appeal acceptance failed.')
  },

  async correct(competitionId: string, teamId: string, reason: string): Promise<void> {
    unwrap(await adminCorrectTeamBan({
      path: { competitionId, teamId },
      body: { reason },
    }), 'Team ban correction failed.')
  },
}
