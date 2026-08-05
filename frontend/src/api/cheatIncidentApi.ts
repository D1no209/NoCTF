import type {
  AdminListCheatIncidentsData,
  NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentDetailResponse,
  NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentListResponse,
  NoCtfDomainSubmissionsCheatIncidentStatus,
} from './generated/types.gen'
import {
  adminConfirmCheatIncident,
  adminCorrectCheatIncident,
  adminDismissCheatIncident,
  adminGetCheatIncident,
  adminListCheatIncidents,
} from './generated/sdk.gen'
import { ApiError } from './noctf'

interface GeneratedResult<T> {
  data?: T
  error?: unknown
  response?: Response
}

function unwrap<T>(result: GeneratedResult<T>, message: string): T {
  if (result.error) {
    throw new ApiError(message, result.response?.status, result.error)
  }
  return result.data as T
}

export type CheatIncidentPage
  = NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentListResponse
export type CheatIncidentDetail
  = NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentDetailResponse
export type CheatIncidentStatus = NoCtfDomainSubmissionsCheatIncidentStatus
export type CheatIncidentQuery = AdminListCheatIncidentsData['query']

export const cheatIncidentApi = {
  async list(
    competitionId: string,
    query: CheatIncidentQuery,
  ): Promise<CheatIncidentPage> {
    return unwrap(await adminListCheatIncidents({
      path: { competitionId },
      query,
    }), 'Cheat incident list request failed.')
  },

  async get(
    competitionId: string,
    scoringEventId: string,
  ): Promise<CheatIncidentDetail> {
    return unwrap(await adminGetCheatIncident({
      path: { competitionId, scoringEventId },
    }), 'Cheat incident evidence request failed.')
  },

  async dismiss(
    competitionId: string,
    scoringEventId: string,
    reason: string,
  ): Promise<void> {
    unwrap(await adminDismissCheatIncident({
      path: { competitionId, scoringEventId },
      body: { reason },
    }), 'Cheat incident dismissal failed.')
  },

  async confirm(
    competitionId: string,
    scoringEventId: string,
    reason: string,
  ): Promise<void> {
    unwrap(await adminConfirmCheatIncident({
      path: { competitionId, scoringEventId },
      body: { reason },
    }), 'Cheat incident confirmation failed.')
  },

  async correct(
    competitionId: string,
    scoringEventId: string,
    reason: string,
  ): Promise<void> {
    unwrap(await adminCorrectCheatIncident({
      path: { competitionId, scoringEventId },
      body: { reason },
    }), 'Cheat incident correction failed.')
  },
}
