import type {
  AdminPlatformListAuditLogsData,
  AdminPlatformListLogsData,
  NoCtfapiEndpointsAdministrationPlatformDeadLetterResponse,
  NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse,
  NoCtfapiEndpointsAdministrationPlatformPlatformLogResponse,
  NoCtfApplicationAdministrationPlatformLogsPlatformLogLevel,
  NoCtfApplicationAdministrationPlatformLogsPlatformLogService,
} from './generated/types.gen'
import {
  adminPlatformListAuditLogs,
  adminPlatformListDeadLetters,
  adminPlatformListLogs,
  adminPlatformRequeueDeadLetter,
} from './generated/sdk.gen'

export type PlatformLog = NoCtfapiEndpointsAdministrationPlatformPlatformLogResponse
export type PlatformAuditLog = NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse
export type PlatformDeadLetter = NoCtfapiEndpointsAdministrationPlatformDeadLetterResponse
export type PlatformLogQuery = AdminPlatformListLogsData['query']
export type PlatformAuditLogQuery = AdminPlatformListAuditLogsData['query']
export type PlatformLogLevel = NoCtfApplicationAdministrationPlatformLogsPlatformLogLevel
export type PlatformLogService = NoCtfApplicationAdministrationPlatformLogsPlatformLogService

function requireData<T>(result: { data?: T, error?: unknown, response?: Response }): T {
  if (result.error)
    throw result.error
  if (!result.data)
    throw new Error(`Platform log request failed with HTTP ${result.response?.status ?? 'unknown'}.`)
  return result.data
}

export const platformLogsApi = {
  async list(query: PlatformLogQuery) {
    return requireData(await adminPlatformListLogs({ query }))
  },
  async audits(query: PlatformAuditLogQuery) {
    return requireData(await adminPlatformListAuditLogs({ query }))
  },
  async deadLetters(limit = 100): Promise<PlatformDeadLetter[]> {
    return requireData(await adminPlatformListDeadLetters({ query: { limit } })).items ?? []
  },
  async requeueDeadLetter(messageId: string): Promise<void> {
    const result = await adminPlatformRequeueDeadLetter({ path: { messageId } })
    if (result.error)
      throw result.error
  },
}
