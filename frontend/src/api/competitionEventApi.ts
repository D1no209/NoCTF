import type {
  AdminExportCompetitionEventsData,
  ListCompetitionEventsData,
  NoCtfapiEndpointsCompetitionsEventsAccessCompetitionSubmissionFlagResponse,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventListResponse,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
  NoCtfDomainCompetitionsEventsCompetitionEventKind,
  NoCtfDomainCompetitionsEventsCompetitionEventLevel,
} from './generated/types.gen'
import {
  adminAccessCompetitionSubmissionFlag,
  adminExportCompetitionEvents,
  listCompetitionEvents,
} from './generated/sdk.gen'
import { ApiError } from './noctf'

interface GeneratedResult<T> {
  data?: T
  error?: unknown
  response?: Response
}

const encodedFileNamePattern = /filename\*=UTF-8''([^;]+)/i
const plainFileNamePattern = /filename="?([^";]+)"?/i

function unwrap<T>(result: GeneratedResult<T>): T {
  if (result.error) {
    throw new ApiError(
      'Competition event request failed.',
      result.response?.status,
      result.error,
    )
  }
  return result.data as T
}

export type CompetitionEvent
  = NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse
export type CompetitionEventPage
  = NoCtfapiEndpointsCompetitionsEventsCompetitionEventListResponse
export type CompetitionEventKind
  = NoCtfDomainCompetitionsEventsCompetitionEventKind
export type CompetitionEventLevel
  = NoCtfDomainCompetitionsEventsCompetitionEventLevel

export const competitionEventApi = {
  async list(
    competitionId: string,
    query: ListCompetitionEventsData['query'],
  ): Promise<CompetitionEventPage> {
    return unwrap(await listCompetitionEvents({
      path: { competitionId },
      query,
    }))
  },

  async export(
    competitionId: string,
    query: AdminExportCompetitionEventsData['query'],
  ): Promise<{ content: Blob, fileName: string }> {
    const result = await adminExportCompetitionEvents({
      path: { competitionId },
      query,
    })
    const content = unwrap(result)
    if (!(content instanceof Blob))
      throw new ApiError('Competition event export returned an invalid body.')
    const disposition = result.response?.headers.get('content-disposition') ?? ''
    const encoded = disposition.match(encodedFileNamePattern)?.[1]
    const plain = disposition.match(plainFileNamePattern)?.[1]
    return {
      content,
      fileName: encoded
        ? decodeURIComponent(encoded)
        : plain ?? `competition-events.jsonl`,
    }
  },

  async accessFlag(
    competitionId: string,
    submissionId: string,
    reason: string,
  ): Promise<NoCtfapiEndpointsCompetitionsEventsAccessCompetitionSubmissionFlagResponse> {
    return unwrap(await adminAccessCompetitionSubmissionFlag({
      path: { competitionId, submissionId },
      body: { reason },
    }))
  },
}
