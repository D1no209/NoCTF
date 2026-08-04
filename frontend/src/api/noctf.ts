import type { PublicChallengeDownload } from './challengePresentation'
import type {
  AdminDeleteCompetitionChallengeData,
  AdminRestoreCompetitionChallengeData,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateConflictCode,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateConflictResponse,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse,
  NoCtfapiEndpointsAdministrationChallengeBankCreateChallengeTemplateRequest,
  NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeConflictCode,
  NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeConflictResponse,
  NoCtfapiEndpointsAdministrationChallengesCreateChallengeRequest,
  NoCtfapiEndpointsAdministrationChallengesUpdateChallengeRequest,
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionCandidateListResponse,
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionCandidateResponse,
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionsResponse,
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionResourceManagerConflictCode,
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionResourceManagerConflictResponse,
  NoCtfapiEndpointsAdministrationCompetitionsCreateCompetitionRequest,
  NoCtfapiEndpointsAdministrationCompetitionsUpdateCompetitionPermissionsRequest,
  NoCtfapiEndpointsAdministrationCompetitionsUpdateCompetitionRequest,
  NoCtfapiEndpointsAdministrationPlatformEmailVerificationConfigurationResponse,
  NoCtfapiEndpointsAdministrationPlatformPlatformUserResponse,
  NoCtfapiEndpointsAdministrationPlatformUpdateEmailVerificationConfigurationRequest,
  NoCtfapiEndpointsAdministrationPlatformUpdatePlatformUserRoleConflictResponse,
  NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse,
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
  NoCtfapiEndpointsCompetitionsGetLeaderboardEndpointResponse,
  NoCtfapiEndpointsTeamsCreateTeamRequest,
  NoCtfapiEndpointsTeamsUpdateTeamRequest,
  NoCtfDomainIdentityUserRole,
} from './generated/types.gen'
import type { PublicSubmissionListItem } from './submissionPresentation'
import { translate as tt } from '@/i18n'
import { configureAuthSessionRefresh, readAuthSession } from './auth-session'
import {
  readDownloadFileName,
  toPublicChallenge,
  toPublicChallengeAttachment,
} from './challengePresentation'
import { toPublicCompetition } from './competitionPresentation'
import { client } from './generated/client.gen'
import * as generatedSdk from './generated/sdk.gen'
import { toPublicNotificationPage } from './notificationPresentation'
import {
  toAcceptedFlagSubmission,
  toPublicSubmissionPage,
  toPublicSubmissionStatus,
} from './submissionPresentation'
import { toPublicTeam } from './teamPresentation'

// Some legacy screens still call optional endpoints that are not part of the
// current public competition contract. Keep their failure typed and contained
// while all current endpoints use the generated SDK directly.
const sdk: any = new Proxy(generatedSdk, {
  get(target, property: string) {
    if (property in target)
      return (target as Record<string, unknown>)[property]
    return async () => ({
      data: undefined,
      error: new Error(`Unsupported API operation: ${property}`),
    })
  },
})
const absoluteHttpUrlPattern = /^https?:\/\//i

function requestAuthenticationRefresh(signal?: AbortSignal) {
  return generatedSdk.noCtfapiEndpointsAuthenticationRefreshTokenEndpoint({
    credentials: 'include',
    headers: { Authorization: null },
    signal,
  })
}

configureAuthSessionRefresh(requestAuthenticationRefresh)

export class ApiError extends Error {
  readonly status?: number
  readonly details?: unknown

  constructor(message: string, status?: number, details?: unknown) {
    super(message)
    this.status = status
    this.details = details
  }
}

export interface PlatformRoleAssignmentBlockers {
  competitionIds: string[]
  challengeIds: string[]
}

export interface ChallengeTemplateConflict {
  code: NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateConflictCode
  userIds: string[]
}

export interface CompetitionPermissionsConflict {
  code: NoCtfapiEndpointsAdministrationCompetitionsCompetitionResourceManagerConflictCode
  userIds: string[]
}

export interface CompetitionChallengeConflict {
  code: NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeConflictCode
}

interface ApiResult<T> {
  data?: T
  error?: unknown
  response?: Response
}

function unwrap<T>(result: ApiResult<T>, fallback = tt('errors.requestFailed')): T {
  if (result.error) {
    const status = result.response?.status
    const details = isEmptyObject(result.error) && status ? `HTTP ${status}` : result.error
    throw new ApiError(fallback, status, details)
  }
  return result.data as T
}

function unwrapChallengeDownload(
  result: ApiResult<unknown>,
  fallback: string,
): PublicChallengeDownload {
  const content = unwrap(result, fallback)
  if (!(content instanceof Blob))
    throw new ApiError(fallback, result.response?.status)

  return {
    content,
    fileName: readDownloadFileName(result.response?.headers.get('Content-Disposition') ?? null),
  }
}

async function requireSuccess<T>(request: Promise<ApiResult<T>>, fallback?: string): Promise<T> {
  return unwrap(await request, fallback)
}

function isEmptyObject(value: unknown) {
  return Boolean(
    value && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === 0,
  )
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value && typeof value === 'object' && !Array.isArray(value))
}

function normalizedIds(value: unknown) {
  if (!Array.isArray(value))
    return []

  return [
    ...new Set(value.filter((id): id is string => typeof id === 'string' && id.length > 0)),
  ].sort()
}

export function readPlatformRoleAssignmentBlockers(
  error: unknown,
): PlatformRoleAssignmentBlockers | null {
  if (!(error instanceof ApiError) || error.status !== 409 || !isRecord(error.details))
    return null

  const details
    = error.details as NoCtfapiEndpointsAdministrationPlatformUpdatePlatformUserRoleConflictResponse
  if (details.code !== 'ActiveOwnerOrManagerAssignments')
    return null

  return {
    competitionIds: normalizedIds(details.competitionIds),
    challengeIds: normalizedIds(details.challengeIds),
  }
}

const challengeTemplateConflictCodes
  = new Set<NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateConflictCode>([
    'ResourceIdConflict',
    'RevisionConflict',
    'ActiveCompetitionModeConflict',
    'OwnerIncludedInManagerSet',
    'UserNotFound',
    'RoleNotEligible',
  ])

export function readChallengeTemplateConflict(error: unknown): ChallengeTemplateConflict | null {
  if (!(error instanceof ApiError) || error.status !== 409 || !isRecord(error.details))
    return null

  const details
    = error.details as NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateConflictResponse
  if (!details.code || !challengeTemplateConflictCodes.has(details.code))
    return null

  return {
    code: details.code,
    userIds: normalizedIds(details.userIds),
  }
}

const competitionChallengeConflictCodes
  = new Set<NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeConflictCode>([
    'ResourceIdConflict',
    'ChallengeOrderConflict',
    'ChallengeTemplateConflict',
    'RevisionConflict',
    'LifecycleStateConflict',
    'ChallengeTemplateNotFound',
    'ChallengeTemplateModeMismatch',
  ])

export function readCompetitionChallengeConflict(
  error: unknown,
): CompetitionChallengeConflict | null {
  if (!(error instanceof ApiError) || error.status !== 409 || !isRecord(error.details))
    return null

  const details
    = error.details as NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeConflictResponse
  if (!details.code || !competitionChallengeConflictCodes.has(details.code))
    return null

  return { code: details.code }
}

const competitionPermissionsConflictCodes
  = new Set<NoCtfapiEndpointsAdministrationCompetitionsCompetitionResourceManagerConflictCode>([
    'RevisionConflict',
    'EmailNotVerified',
    'RolesOverlap',
    'OwnerIncluded',
    'UserNotFound',
    'RoleNotEligible',
  ])

export function readCompetitionPermissionsConflict(
  error: unknown,
): CompetitionPermissionsConflict | undefined {
  if (!(error instanceof ApiError) || error.status !== 409 || !isRecord(error.details))
    return undefined

  const details
    = error.details as NoCtfapiEndpointsAdministrationCompetitionsCompetitionResourceManagerConflictResponse
  if (!details.code || !competitionPermissionsConflictCodes.has(details.code))
    return undefined

  return {
    code: details.code,
    userIds: normalizedIds(details.userIds),
  }
}

export function apiUrl(path: string) {
  const baseUrl = import.meta.env.VITE_API_BASE_URL ?? ''
  if (!baseUrl || absoluteHttpUrlPattern.test(path))
    return path
  return new URL(path, baseUrl).toString()
}

export function setAuthToken(token: string | null) {
  client.setConfig({
    headers: { Authorization: token ? `Bearer ${token}` : '' },
  })
}

async function postJson<T>(path: string, body?: unknown): Promise<T> {
  const token = readAuthSession()?.accessToken
  const response = await fetch(apiUrl(path), {
    method: 'POST',
    headers: {
      'Accept': 'application/json',
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    credentials: 'include',
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const data = await response.json().catch(() => undefined)
  if (!response.ok)
    throw new ApiError(tt('errors.requestFailed'), response.status, data)
  return data as T
}

export const notificationApi = {
  async listPage(
    {
      cursor,
      limit = 20,
    }: {
      cursor?: string | null
      limit?: number
    } = {},
    signal?: AbortSignal,
  ) {
    return toPublicNotificationPage(
      unwrap(
        await generatedSdk.noCtfapiEndpointsNotificationsListNotificationsEndpoint({
          query: { cursor, limit },
          signal,
        }),
        tt('errors.loadNotifications'),
      ),
    )
  },
}

export const authApi = {
  async getMe() {
    const response = unwrap(
      await generatedSdk.noCtfapiEndpointsAuthenticationGetMeEndpoint(),
      tt('errors.requestFailed'),
    )
    if (!response.userId)
      throw new ApiError(tt('errors.requestFailed'))
    return response
  },
  async updateProfile(description: string | null) {
    return unwrap(
      await generatedSdk.authenticationUpdateMyProfile({ body: { description } }),
      tt('errors.requestFailed'),
    )
  },
  async uploadAvatar(file: File) {
    return unwrap(
      await generatedSdk.authenticationUploadMyAvatar({ body: { file } }),
      tt('errors.requestFailed'),
    )
  },
  async login(email: string, password: string) {
    return unwrap(
      await generatedSdk.noCtfapiEndpointsAuthenticationLoginEndpoint({
        body: { login: email, password },
        credentials: 'include',
      }),
      tt('errors.login'),
    )
  },
  async register(userName: string, email: string, password: string) {
    return unwrap(
      await generatedSdk.noCtfapiEndpointsAuthenticationRegisterEndpoint({
        body: { userName, email, password },
        credentials: 'include',
      }),
      tt('errors.registration'),
    )
  },
  async verifyEmail(token: string) {
    return unwrap(
      await generatedSdk.noCtfapiEndpointsAuthenticationVerifyEmailEndpoint({
        body: { token },
      }),
      tt('errors.verifyEmail'),
    )
  },
  async resendEmailVerification() {
    return unwrap(
      await generatedSdk.noCtfapiEndpointsAuthenticationResendEmailVerificationEndpoint(),
      tt('errors.resendVerification'),
    )
  },
  async refresh() {
    return unwrap(await requestAuthenticationRefresh(), tt('errors.refreshToken'))
  },
}

export const healthApi = {
  async get() {
    return unwrap(await generatedSdk.noCtfapiEndpointsHealthEndpoint(), tt('errors.loadHealth'))
  },
}

export const competitionApi = {
  async list() {
    const response = unwrap(
      await generatedSdk.noCtfapiEndpointsCompetitionsListCompetitionsEndpoint(),
      tt('errors.loadCompetitions'),
    )
    if (!response.items)
      throw new ApiError(tt('errors.loadCompetitions'))
    return response.items.map(toPublicCompetition)
  },
  async get(competitionId: string) {
    const response = unwrap(
      await generatedSdk.noCtfapiEndpointsCompetitionsGetCompetitionEndpoint({
        path: { competitionId },
      }),
      tt('errors.loadCompetition'),
    )
    return toPublicCompetition(response)
  },
  async create(body: Record<string, unknown>) {
    return unwrap(
      await sdk.noCtfapiEndpointsCompetitionsCreateCompetitionEndpoint({ body }),
      tt('errors.createCompetition'),
    )
  },
  async leaderboard(
    competitionId: string,
  ): Promise<NoCtfapiEndpointsCompetitionsGetLeaderboardEndpointResponse> {
    return unwrap<NoCtfapiEndpointsCompetitionsGetLeaderboardEndpointResponse>(
      await sdk.noCtfapiEndpointsCompetitionsGetLeaderboardEndpoint({
        path: { competitionId },
      }),
      tt('errors.loadLeaderboard'),
    )
  },
  async createInstance<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges/{challengeId}/instance',
        path: { id: competitionId, challengeId },
        body: {},
      }),
      tt('errors.createInstance'),
    )
  },
  async getInstance<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges/{challengeId}/instance',
        path: { id: competitionId, challengeId },
      }),
      tt('errors.loadInstance'),
    )
  },
  async destroyInstance<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(
      await client.delete<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges/{challengeId}/instance',
        path: { id: competitionId, challengeId },
      }),
      tt('errors.destroyInstance'),
    )
  },
  async extendInstance<T = unknown>(competitionId: string, challengeId: string) {
    return unwrap(
      await client.post<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/challenges/{challengeId}/instance/extend',
        path: { id: competitionId, challengeId },
        body: {},
      }),
      tt('errors.extendInstance'),
    )
  },
  async view<T = unknown>(competitionId: string, viewKey: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/views/{viewKey}',
        path: { id: competitionId, viewKey },
      }),
      tt('errors.loadCompetitionView'),
    )
  },
  async submitPatch<T = unknown>(
    competitionId: string,
    teamId: string,
    challengeId: string,
    file: File,
  ) {
    const checksum = Array.from(
      new Uint8Array(await crypto.subtle.digest('SHA-256', await file.arrayBuffer())),
    )
      .map(byte => byte.toString(16).padStart(2, '0'))
      .join('')
    const upload = await postJson<{
      uploadId: string
      uploadUrl: string
      expiresAt: string
    }>(`/competitions/${competitionId}/submissions/fixes/uploads`, {
      teamId,
      challengeId,
      fileName: file.name,
      contentType: file.type || 'application/octet-stream',
      length: file.size,
      sha256: checksum,
    })
    const applicationOrigin = globalThis.location?.origin ?? 'http://localhost'
    const uploadUrl = new URL(upload.uploadUrl, applicationOrigin)
    const token = readAuthSession()?.accessToken
    const apiOrigin = new URL(apiUrl('/'), applicationOrigin).origin
    const isApiUpload
      = uploadUrl.origin === apiOrigin && uploadUrl.pathname.startsWith('/storage/uploads/')
    const uploadBody = isApiUpload
      ? (() => {
          const form = new FormData()
          form.append('file', file, file.name)
          return form
        })()
      : file
    const uploadResponse = await fetch(uploadUrl, {
      method: 'PUT',
      headers: {
        ...(isApiUpload ? {} : { 'Content-Type': file.type || 'application/octet-stream' }),
        ...(isApiUpload && token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: uploadBody,
    })
    if (!uploadResponse.ok)
      throw new ApiError(tt('errors.requestFailed'), uploadResponse.status)
    return postJson<T>(`/competitions/${competitionId}/submissions/fixes`, {
      teamId,
      challengeId,
      uploadId: upload.uploadId,
    })
  },
  async patchSubmissions<T = unknown[]>(competitionId: string) {
    return unwrap(
      await client.get<{ 200: T }, unknown, false>({
        url: '/api/competitions/{id}/patch-submissions',
        path: { id: competitionId },
      }),
      tt('errors.loadPatchSubmissions'),
    )
  },
  async patchSubmission(competitionId: string, submissionId: string) {
    return unwrap(
      await sdk.noCtfapiEndpointsCompetitionsGetPatchSubmissionEndpoint({
        path: { id: competitionId, submissionId },
      }),
      tt('errors.loadPatchSubmissions'),
    )
  },
  async capabilities(competitionId: string) {
    return unwrap(
      await sdk.noCtfapiEndpointsCompetitionsGetCompetitionCapabilitiesEndpoint({
        path: { id: competitionId },
      }),
      tt('errors.loadCompetition'),
    )
  },
  async action(competitionId: string, actionKey: string, body: Record<string, unknown>) {
    return unwrap(
      await sdk.noCtfapiEndpointsCompetitionsPostCompetitionActionEndpoint({
        path: { id: competitionId, actionKey },
        body,
      }),
      tt('errors.requestFailed'),
    )
  },
  async scoreboard(competitionId: string) {
    return unwrap(
      await sdk.noCtfapiEndpointsCompetitionsGetCompetitionScoreboardEndpoint({
        path: { id: competitionId },
      }),
      tt('errors.loadLeaderboard'),
    )
  },
}

async function listSubmissionPage(
  competitionId: string,
  options: { limit: number, cursor?: string | null },
  signal?: AbortSignal,
) {
  const response = unwrap(
    await generatedSdk.noCtfapiEndpointsSubmissionsListSubmissionsEndpoint({
      path: { competitionId },
      query: {
        limit: options.limit,
        cursor: options.cursor,
      },
      signal,
    }),
    tt('errors.loadSubmissions'),
  )
  return toPublicSubmissionPage(response)
}

export const submissionApi = {
  listPage: listSubmissionPage,
  async listAll(competitionId: string, signal?: AbortSignal): Promise<PublicSubmissionListItem[]> {
    const submissions: PublicSubmissionListItem[] = []
    const seenCursors = new Set<string>()
    let cursor: string | null | undefined

    do {
      const page = await listSubmissionPage(competitionId, { limit: 200, cursor }, signal)
      submissions.push(...page.items)
      cursor = page.nextCursor

      if (cursor !== null) {
        if (seenCursors.has(cursor))
          throw new TypeError('Submission list returned a repeated cursor.')
        seenCursors.add(cursor)
      }
    } while (cursor !== null)

    return submissions
  },
  async submitFlag(
    competitionId: string,
    competitionChallengeId: string,
    flag: string,
    signal?: AbortSignal,
  ) {
    const response = unwrap(
      await generatedSdk.noCtfapiEndpointsSubmissionsSubmitFlagEndpoint({
        path: { competitionId, competitionChallengeId },
        body: { flag },
        signal,
      }),
      tt('errors.submitFlag'),
    )
    return toAcceptedFlagSubmission(response)
  },
  async getStatus(competitionId: string, submissionId: string, signal?: AbortSignal) {
    const response = unwrap(
      await generatedSdk.noCtfapiEndpointsSubmissionsGetSubmissionStatusEndpoint({
        path: { competitionId, submissionId },
        signal,
      }),
      tt('errors.loadSubmissionStatus'),
    )
    return toPublicSubmissionStatus(response)
  },
}

export const challengeApi = {
  async list(competitionId: string) {
    const response = unwrap(
      await generatedSdk.noCtfapiEndpointsChallengesListChallengesEndpoint({
        path: { competitionId },
      }),
      tt('errors.loadChallenges'),
    )
    if (!response.items)
      throw new ApiError(tt('errors.loadChallenges'))
    return response.items.map(toPublicChallenge)
  },
  async get(competitionId: string, competitionChallengeId: string) {
    return toPublicChallenge(
      unwrap(
        await generatedSdk.noCtfapiEndpointsChallengesGetChallengeEndpoint({
          path: { competitionId, competitionChallengeId },
        }),
        tt('errors.loadChallenges'),
      ),
    )
  },
  async listAttachments(competitionId: string, competitionChallengeId: string) {
    const response = unwrap(
      await generatedSdk.noCtfapiEndpointsChallengesListChallengeAttachmentsEndpoint({
        path: { competitionId, competitionChallengeId },
      }),
      tt('errors.loadChallenges'),
    )
    if (!response.items)
      throw new ApiError(tt('errors.loadChallenges'))
    return response.items.map(toPublicChallengeAttachment)
  },
  async downloadAttachment(
    competitionId: string,
    competitionChallengeId: string,
    attachmentId: string,
  ) {
    return unwrapChallengeDownload(
      await generatedSdk.noCtfapiEndpointsChallengesDownloadChallengeAttachmentEndpoint({
        path: { competitionId, competitionChallengeId, attachmentId },
        parseAs: 'blob',
      }),
      tt('errors.requestFailed'),
    )
  },
  async downloadRandomAttachment(competitionId: string, competitionChallengeId: string) {
    return unwrapChallengeDownload(
      await generatedSdk.noCtfapiEndpointsChallengesDownloadRandomChallengeAttachmentEndpoint({
        path: { competitionId, competitionChallengeId },
        parseAs: 'blob',
      }),
      tt('errors.requestFailed'),
    )
  },
}

export const teamApi = {
  async list(competitionId: string) {
    const response = unwrap(
      await generatedSdk.noCtfapiEndpointsTeamsListCompetitionTeamsEndpoint({
        path: { competitionId },
      }),
      tt('errors.loadTeams'),
    )
    if (!response.items)
      throw new ApiError(tt('errors.loadTeams'))
    return response.items.map(toPublicTeam)
  },
  async getMy(competitionId: string) {
    const result = await generatedSdk.noCtfapiEndpointsTeamsGetMyTeamEndpoint({
      path: { competitionId },
    })
    if (result.response?.status === 404)
      return null
    return toPublicTeam(unwrap(result, tt('errors.loadMyTeams')))
  },
  async create(
    competitionId: string,
    body: NoCtfapiEndpointsTeamsCreateTeamRequest & { name: string },
  ) {
    const response = unwrap(
      await generatedSdk.noCtfapiEndpointsTeamsCreateTeamEndpoint({
        path: { competitionId },
        body,
      }),
      tt('errors.createTeam'),
    )
    return toPublicTeam(response)
  },
  async update(
    competitionId: string,
    teamId: string,
    body: NoCtfapiEndpointsTeamsUpdateTeamRequest & { name: string },
  ) {
    const response = unwrap(
      await generatedSdk.noCtfapiEndpointsTeamsUpdateTeamEndpoint({
        path: { competitionId, teamId },
        body,
      }),
      tt('errors.updateTeam'),
    )
    return toPublicTeam(response)
  },
  async join(competitionId: string, invitationToken: string) {
    await requireSuccess(
      generatedSdk.noCtfapiEndpointsTeamsJoinTeamByInvitationEndpoint({
        path: { competitionId },
        body: { invitationToken },
      }),
      tt('errors.joinTeam'),
    )
  },
  async leave(competitionId: string) {
    await requireSuccess(
      generatedSdk.noCtfapiEndpointsTeamsLeaveTeamEndpoint({
        path: { competitionId },
      }),
      tt('errors.requestFailed'),
    )
  },
}

export type ChallengeTemplate
  = NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse
export type AdminCompetition = NoCtfapiEndpointsCompetitionsCompetitionResponse
export type CompetitionChallenge = NoCtfapiEndpointsChallengesChallengeResponse
export type CompetitionPermissions
  = NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionsResponse
export type CompetitionPermissionCandidate
  = NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionCandidateResponse
export type CompetitionPermissionCandidates
  = NoCtfapiEndpointsAdministrationCompetitionsCompetitionPermissionCandidateListResponse
export type UpdateCompetitionPermissionsRequest
  = NoCtfapiEndpointsAdministrationCompetitionsUpdateCompetitionPermissionsRequest

export const competitionAdminApi = {
  async list(): Promise<AdminCompetition[]> {
    const response = unwrap(
      await generatedSdk.adminListCompetitions(),
      tt('errors.loadCompetitions'),
    )
    return response.items ?? []
  },
  async get(competitionId: string): Promise<AdminCompetition> {
    return unwrap(
      await generatedSdk.adminGetCompetition({
        path: { competitionId },
      }),
      tt('errors.loadCompetition'),
    )
  },
  async create(body: NoCtfapiEndpointsAdministrationCompetitionsCreateCompetitionRequest) {
    return unwrap(
      await generatedSdk.adminCreateCompetition({ body }),
      tt('errors.createCompetition'),
    )
  },
  async update(
    competitionId: string,
    body: NoCtfapiEndpointsAdministrationCompetitionsUpdateCompetitionRequest,
  ) {
    return unwrap(
      await generatedSdk.adminUpdateCompetition({ path: { competitionId }, body }),
      tt('errors.updateCompetition'),
    )
  },
  async delete(competitionId: string): Promise<void> {
    await requireSuccess(
      generatedSdk.adminDeleteCompetition({ path: { competitionId } }),
      tt('errors.requestFailed'),
    )
  },
}

export const competitionTeamAdminApi = {
  async list(competitionId: string) {
    const response = unwrap(
      await generatedSdk.adminListTeams({ path: { competitionId } }),
      tt('errors.loadCompetitionTeams'),
    )
    return response.items ?? []
  },
  async approve(competitionId: string, teamId: string): Promise<void> {
    await requireSuccess(
      generatedSdk.adminApproveTeam({ path: { competitionId, teamId } }),
      tt('errors.approveTeam'),
    )
  },
  async reject(competitionId: string, teamId: string): Promise<void> {
    await requireSuccess(
      generatedSdk.adminRejectTeam({ path: { competitionId, teamId } }),
      tt('errors.rejectTeam'),
    )
  },
  async ban(competitionId: string, teamId: string, reason: string): Promise<void> {
    await requireSuccess(
      generatedSdk.adminBanTeam({ path: { competitionId, teamId }, body: { reason } }),
      tt('errors.banTeam'),
    )
  },
  async unban(competitionId: string, teamId: string): Promise<void> {
    await requireSuccess(
      generatedSdk.adminUnbanTeam({ path: { competitionId, teamId } }),
      tt('errors.unbanTeam'),
    )
  },
}

export const competitionRuntimeAdminApi = {
  async list(
    competitionId: string,
  ): Promise<NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse[]> {
    const response = unwrap(
      await generatedSdk.adminListRuntimes({
        path: { competitionId },
        query: { limit: 200 },
      }),
      tt('errors.loadContainers'),
    )
    return response.items ?? []
  },
  async reset(
    competitionId: string,
    runtime: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse,
  ) {
    const competitionChallengeId = requireRuntimeChallengeId(runtime)
    if (runtime.teamId) {
      return unwrap(
        await generatedSdk.adminResetTeamRuntime({
          path: { competitionId, teamId: runtime.teamId, competitionChallengeId },
        }),
      )
    }
    return unwrap(
      await generatedSdk.adminResetSharedRuntime({
        path: { competitionId, competitionChallengeId },
      }),
    )
  },
  async stop(
    competitionId: string,
    runtime: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse,
  ) {
    const competitionChallengeId = requireRuntimeChallengeId(runtime)
    if (runtime.teamId) {
      return unwrap(
        await generatedSdk.adminStopTeamRuntime({
          path: { competitionId, teamId: runtime.teamId, competitionChallengeId },
        }),
      )
    }
    return unwrap(
      await generatedSdk.adminStopSharedRuntime({
        path: { competitionId, competitionChallengeId },
      }),
    )
  },
}

function requireRuntimeChallengeId(
  runtime: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse,
) {
  if (!runtime.competitionChallengeId)
    throw new TypeError('Admin runtime response is missing competitionChallengeId.')
  return runtime.competitionChallengeId
}

export const competitionPermissionsAdminApi = {
  async get(competitionId: string): Promise<CompetitionPermissions> {
    return unwrap(
      await generatedSdk.adminGetCompetitionPermissions({
        path: { competitionId },
      }),
      tt('errors.requestFailed'),
    )
  },
  async candidates(competitionId: string): Promise<CompetitionPermissionCandidates> {
    return unwrap(
      await generatedSdk.adminListCompetitionPermissionCandidates({
        path: { competitionId },
      }),
      tt('errors.requestFailed'),
    )
  },
  async update(competitionId: string, body: UpdateCompetitionPermissionsRequest): Promise<void> {
    await requireSuccess(
      generatedSdk.adminUpdateCompetitionPermissions({
        path: { competitionId },
        body,
      }),
      tt('errors.requestFailed'),
    )
  },
}

export const competitionChallengeAdminApi = {
  async list(competitionId: string, includeDeleted: boolean): Promise<CompetitionChallenge[]> {
    const response = unwrap(
      await generatedSdk.adminListCompetitionChallenges({
        path: { competitionId },
        query: { includeDeleted },
      }),
      tt('errors.loadCompetitionChallenges'),
    )
    return response.items ?? []
  },
  async get(
    competitionId: string,
    competitionChallengeId: string,
    includeDeleted: boolean,
  ): Promise<CompetitionChallenge> {
    return unwrap(
      await generatedSdk.adminGetCompetitionChallenge({
        path: { competitionId, competitionChallengeId },
        query: { includeDeleted },
      }),
      tt('errors.loadCompetitionChallenges'),
    )
  },
  async create(
    competitionId: string,
    body: NoCtfapiEndpointsAdministrationChallengesCreateChallengeRequest,
  ): Promise<CompetitionChallenge> {
    return unwrap(
      await generatedSdk.adminCreateCompetitionChallenge({
        path: { competitionId },
        body,
      }),
      tt('errors.bindChallenge'),
    )
  },
  async update(
    competitionId: string,
    competitionChallengeId: string,
    body: NoCtfapiEndpointsAdministrationChallengesUpdateChallengeRequest,
  ): Promise<CompetitionChallenge> {
    return unwrap(
      await generatedSdk.adminUpdateCompetitionChallenge({
        path: { competitionId, competitionChallengeId },
        body,
      }),
      tt('errors.updateCompetitionChallenge'),
    )
  },
  async delete(
    competitionId: string,
    competitionChallengeId: string,
    query: AdminDeleteCompetitionChallengeData['query'],
  ): Promise<void> {
    await requireSuccess(
      generatedSdk.adminDeleteCompetitionChallenge({
        path: { competitionId, competitionChallengeId },
        query,
      }),
      tt('errors.requestFailed'),
    )
  },
  async restore(
    competitionId: string,
    competitionChallengeId: string,
    query: AdminRestoreCompetitionChallengeData['query'],
  ): Promise<void> {
    await requireSuccess(
      generatedSdk.adminRestoreCompetitionChallenge({
        path: { competitionId, competitionChallengeId },
        query,
      }),
      tt('errors.requestFailed'),
    )
  },
}

export const challengeBankAdminApi = {
  async templates(includeDeleted: boolean): Promise<ChallengeTemplate[]> {
    const response = unwrap(
      await generatedSdk.adminChallengeBankListTemplates({
        query: { includeDeleted },
      }),
      tt('errors.loadChallenges'),
    )
    return response.items ?? []
  },
  async template(challengeId: string, includeDeleted = false): Promise<ChallengeTemplate> {
    return unwrap(
      await generatedSdk.adminChallengeBankGetTemplate({
        path: { challengeId },
        query: { includeDeleted },
      }),
      tt('errors.loadChallenges'),
    )
  },
  async create(
    body: NoCtfapiEndpointsAdministrationChallengeBankCreateChallengeTemplateRequest,
  ): Promise<ChallengeTemplate> {
    return unwrap(
      await generatedSdk.adminChallengeBankCreateTemplate({ body }),
      tt('errors.createChallenge'),
    )
  },
  async uploadAttachment(challengeId: string, file: File) {
    return unwrap(
      await generatedSdk.adminChallengeBankUploadAttachment({
        path: { challengeId },
        body: { file },
      }),
      tt('errors.uploadAttachment'),
    )
  },
  async updatePermissions(
    challengeId: string,
    managerIds: string[],
    expectedRevision: number,
  ): Promise<ChallengeTemplate> {
    return unwrap(
      await generatedSdk.adminChallengeBankUpdatePermissions({
        path: { challengeId },
        body: { managerIds, expectedRevision },
      }),
      tt('errors.requestFailed'),
    )
  },
  async deleteTemplate(challengeId: string): Promise<void> {
    await requireSuccess(
      generatedSdk.adminChallengeBankDeleteTemplate({
        path: { challengeId },
      }),
      tt('errors.requestFailed'),
    )
  },
  async restoreTemplate(challengeId: string): Promise<void> {
    await requireSuccess(
      generatedSdk.adminChallengeBankRestoreTemplate({
        path: { challengeId },
      }),
      tt('errors.requestFailed'),
    )
  },
}

export type PlatformUser = NoCtfapiEndpointsAdministrationPlatformPlatformUserResponse
export type EmailVerificationConfiguration
  = NoCtfapiEndpointsAdministrationPlatformEmailVerificationConfigurationResponse

export interface IssuedBotToken {
  accessToken: string
  expiresAt: string
}

export const platformAdminApi = {
  async emailVerificationConfiguration(): Promise<EmailVerificationConfiguration> {
    return unwrap(
      await generatedSdk.adminPlatformGetEmailVerificationConfiguration(),
      tt('errors.loadEmailVerificationConfiguration'),
    )
  },
  async updateEmailVerificationConfiguration(
    body: NoCtfapiEndpointsAdministrationPlatformUpdateEmailVerificationConfigurationRequest,
  ): Promise<EmailVerificationConfiguration> {
    return unwrap(
      await generatedSdk.adminPlatformUpdateEmailVerificationConfiguration({ body }),
      tt('errors.saveEmailVerificationConfiguration'),
    )
  },
  async replaceEmailVerificationPassword(
    password: string,
    expectedRevision: number,
  ): Promise<EmailVerificationConfiguration> {
    return unwrap(
      await generatedSdk.adminPlatformReplaceEmailVerificationPassword({
        body: { password, expectedRevision },
      }),
      tt('errors.saveEmailVerificationPassword'),
    )
  },
  async sendEmailVerificationTest(): Promise<void> {
    await requireSuccess(
      generatedSdk.adminPlatformSendEmailVerificationTest(),
      tt('errors.sendEmailVerificationTest'),
    )
  },
  async users(): Promise<PlatformUser[]> {
    const response = unwrap(await generatedSdk.adminPlatformListUsers(), tt('errors.loadUsers'))
    return response.items ?? []
  },
  async createBot(userName: string, role: NoCtfDomainIdentityUserRole): Promise<PlatformUser> {
    return unwrap(
      await generatedSdk.adminPlatformCreateBot({
        body: {
          userName,
          role: role === 0 ? 'User' : 'Organizer',
        },
      }),
      tt('errors.requestFailed'),
    )
  },
  async updateUserRole(userId: string, role: NoCtfDomainIdentityUserRole): Promise<PlatformUser> {
    return unwrap(
      await generatedSdk.adminPlatformUpdateUserRole({
        path: { userId },
        body: { role },
      }),
      tt('errors.requestFailed'),
    )
  },
  async issueBotToken(userId: string, expiresInSeconds: number): Promise<IssuedBotToken> {
    const response = unwrap(
      await generatedSdk.adminPlatformIssueBotToken({
        path: { userId },
        body: { expiresInSeconds },
      }),
      tt('errors.requestFailed'),
    )
    if (!response.accessToken || !response.expiresAt)
      throw new ApiError(tt('errors.requestFailed'))

    return {
      accessToken: response.accessToken,
      expiresAt: response.expiresAt,
    }
  },
  async invalidateUserTokens(userId: string): Promise<PlatformUser> {
    return unwrap(
      await generatedSdk.adminPlatformInvalidateUserTokens({
        path: { userId },
      }),
      tt('errors.requestFailed'),
    )
  },
}
