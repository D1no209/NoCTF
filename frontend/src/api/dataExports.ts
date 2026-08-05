import type {
  NoCtfapiEndpointsAdministrationDataExportsDataExportResponse,
  NoCtfDomainDataExportsDataExportFailureCode,
  NoCtfDomainDataExportsDataExportStatus,
} from './generated/types.gen'
import { readDownloadFileName } from './challengePresentation'
import {
  adminCreateCompetitionDataExport,
  adminCreatePlatformAuditDataExport,
  adminDownloadDataExport,
  adminListCompetitionDataExports,
  adminListPlatformAuditDataExports,
} from './generated/sdk.gen'

export type DataExportJob = NoCtfapiEndpointsAdministrationDataExportsDataExportResponse
export type DataExportFailureCode = NoCtfDomainDataExportsDataExportFailureCode

export const DataExportStatus = {
  Queued: 0,
  Processing: 1,
  Available: 2,
  Failed: 3,
  Expired: 4,
} as const satisfies Record<string, NoCtfDomainDataExportsDataExportStatus>

function requireData<T>(result: { data?: T, error?: unknown, response?: Response }): T {
  if (result.error)
    throw result.error
  if (!result.data)
    throw new Error(`Data export request failed with HTTP ${result.response?.status ?? 'unknown'}.`)
  return result.data
}

function requireJob(
  result: { data?: DataExportJob, error?: unknown, response?: Response },
): DataExportJob {
  if (result.response?.status === 409 && isDataExportJob(result.error))
    return result.error
  return requireData(result)
}

function isDataExportJob(value: unknown): value is DataExportJob {
  return Boolean(value && typeof value === 'object' && !Array.isArray(value)
    && typeof Reflect.get(value, 'id') === 'string')
}

export const dataExportApi = {
  async listCompetition(competitionId: string): Promise<DataExportJob[]> {
    const result = requireData(await adminListCompetitionDataExports({
      path: { competitionId },
    }))
    return result.items ?? []
  },
  async createCompetition(
    competitionId: string,
    includeProtectedFlags: boolean,
    reason: string | null,
  ): Promise<DataExportJob> {
    return requireJob(await adminCreateCompetitionDataExport({
      path: { competitionId },
      body: {
        includeProtectedFlags,
        reason: reason ?? undefined,
      },
    }))
  },
  async listPlatformAudit(): Promise<DataExportJob[]> {
    const result = requireData(await adminListPlatformAuditDataExports())
    return result.items ?? []
  },
  async createPlatformAudit(): Promise<DataExportJob> {
    return requireJob(await adminCreatePlatformAuditDataExport())
  },
  async download(dataExportId: string): Promise<{ content: Blob, fileName: string }> {
    const result = await adminDownloadDataExport({
      path: { dataExportId },
      parseAs: 'blob',
    })
    const content = requireData(result)
    if (!(content instanceof Blob))
      throw new TypeError('Data export download returned an invalid body.')
    return {
      content,
      fileName: readDownloadFileName(
        result.response?.headers.get('Content-Disposition') ?? null,
      ) ?? 'noctf-data-export',
    }
  },
}
