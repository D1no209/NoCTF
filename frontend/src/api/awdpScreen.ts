import type { AwdpScreenEvent, AwdpScreenEventLevel, AwdpScreenEventType, AwdpScreenSnapshot } from '@/types/awdpScreen'
import { ApiError, apiUrl } from '@/api/noctf'
import { createMockAwareFetch } from '@/mocks/runtime'
import { translate as tt } from '@/i18n'
import { readAuthSession } from './auth-session'

const appFetch = import.meta.env.DEV ? createMockAwareFetch(globalThis.fetch) : globalThis.fetch

function authHeaders(): Record<string, string> {
  const token = readAuthSession()?.accessToken
  return token ? { Authorization: `Bearer ${token}` } : {}
}

async function requestJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await appFetch(apiUrl(url), {
    method: 'GET',
    headers: {
      Accept: 'application/json',
      ...authHeaders(),
    },
    signal,
  })

  if (!response.ok) {
    throw new ApiError(tt('errors.awdpScreenLoad'), response.status)
  }

  const contentType = response.headers.get('content-type') ?? ''
  const body = await response.text()
  if (!contentType.toLowerCase().includes('application/json')) {
    throw new ApiError(
      tt('errors.awdpScreenNonJson'),
      response.status,
      body.slice(0, 160),
    )
  }

  try {
    return JSON.parse(body) as T
  }
  catch (err) {
    throw new ApiError(tt('errors.awdpScreenInvalidJson'), response.status, err)
  }
}

export const awdpScreenApi = {
  snapshot(gameId: string, signal?: AbortSignal) {
    return requestJson<AwdpScreenSnapshot>(`/api/awdp/screen/snapshot?gameId=${encodeURIComponent(gameId)}`, signal)
  },
  eventStreamUrl(gameId: string) {
    return apiUrl(`/api/awdp/screen/events?gameId=${encodeURIComponent(gameId)}`)
  },
}

export function parseAwdpScreenEvent(raw: unknown): AwdpScreenEvent | null {
  const value = typeof raw === 'string' ? parseJson(raw) : raw
  if (!value || typeof value !== 'object')
    return null

  const record = value as Record<string, unknown>
  if (record.snapshot && typeof record.snapshot === 'object')
    return null

  const type = normalizeEventType(record.type)
  if (!type)
    return null

  const event: AwdpScreenEvent = {
    id: toText(record.id) || `event-${Date.now()}`,
    gameId: toText(record.gameId),
    round: toNumber(record.round),
    type,
    teamId: optionalText(record.teamId),
    teamName: optionalText(record.teamName),
    challengeId: optionalText(record.challengeId),
    challengeName: optionalText(record.challengeName),
    challengeCategory: normalizeCategory(record.challengeCategory),
    scoreDelta: optionalNumber(record.scoreDelta),
    level: normalizeLevel(record.level, type),
    message: '',
    createdAt: toText(record.createdAt) || new Date().toISOString(),
  }

  event.message = buildSafeEventMessage(event)
  return event
}

export function parseAwdpScreenSnapshot(raw: unknown): AwdpScreenSnapshot | null {
  const value = typeof raw === 'string' ? parseJson(raw) : raw
  if (!value || typeof value !== 'object')
    return null
  const record = value as Record<string, unknown>
  return record.snapshot && typeof record.snapshot === 'object'
    ? record.snapshot as AwdpScreenSnapshot
    : null
}

export function buildSafeEventMessage(event: Pick<AwdpScreenEvent, 'round' | 'type' | 'teamName' | 'challengeName'>) {
  const round = event.round > 0 ? tt('awdpScreen.events.roundFallback') + ' ' + event.round : tt('awdpScreen.events.roundFallback')
  const team = event.teamName ?? tt('awdpScreen.events.teamFallback')
  const challenge = event.challengeName ?? tt('awdpScreen.events.challengeFallback')

  switch (event.type) {
    case 'ROUND_STARTED':
      return tt('awdpScreen.events.roundStarted', { round })
    case 'ROUND_ENDED':
      return tt('awdpScreen.events.roundEnded', { round })
    case 'CHALLENGE_SELECTED':
      return tt('awdpScreen.events.challengeSelected', { round, team, challenge })
    case 'INSTANCE_CREATED':
      return tt('awdpScreen.events.instanceCreated', { round, team, challenge })
    case 'DEFENSE_REQUESTED':
      return tt('awdpScreen.events.defenseRequested', { round, team, challenge })
    case 'PATCH_UPLOADED':
      return tt('awdpScreen.events.patchUploaded', { round, team, challenge })
    case 'DEFENSE_CHECK_PASSED':
      return tt('awdpScreen.events.defensePassed', { round, team, challenge })
    case 'DEFENSE_CHECK_FAILED':
      return tt('awdpScreen.events.defenseFailed', { round, team, challenge })
    case 'ATTACK_SUBMITTED':
      return tt('awdpScreen.events.attackSubmitted', { round, team, challenge })
    case 'ATTACK_ACCEPTED':
      return tt('awdpScreen.events.attackAccepted', { round, team, challenge })
    case 'ATTACK_REJECTED':
      return tt('awdpScreen.events.attackRejected', { round, team, challenge })
    case 'SERVICE_ERROR':
      return tt('awdpScreen.events.serviceError', { round, team, challenge })
    case 'SCORE_UPDATED':
      return tt('awdpScreen.events.scoreUpdated', { round, team })
    case 'TEAM_RANK_CHANGED':
      return tt('awdpScreen.events.rankChanged', { round, team })
  }
}

function parseJson(value: string) {
  try {
    return JSON.parse(value)
  }
  catch {
    return null
  }
}

function toText(value: unknown) {
  return typeof value === 'string' ? value : ''
}

function optionalText(value: unknown) {
  return typeof value === 'string' && value.length > 0 ? value : undefined
}

function toNumber(value: unknown) {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0
}

function optionalNumber(value: unknown) {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined
}

function normalizeCategory(value: unknown) {
  return value === 'web' || value === 'pwn' || value === 'crypto' || value === 'reverse' || value === 'misc'
    ? value
    : undefined
}

function normalizeLevel(value: unknown, type: AwdpScreenEventType): AwdpScreenEventLevel {
  if (value === 'info' || value === 'success' || value === 'warning' || value === 'danger')
    return value
  if (type === 'ATTACK_ACCEPTED' || type === 'DEFENSE_CHECK_PASSED')
    return 'success'
  if (type === 'SERVICE_ERROR')
    return 'danger'
  if (type === 'ATTACK_REJECTED' || type === 'DEFENSE_CHECK_FAILED')
    return 'warning'
  return 'info'
}

function normalizeEventType(value: unknown): AwdpScreenEventType | null {
  if (
    value === 'ROUND_STARTED'
    || value === 'ROUND_ENDED'
    || value === 'CHALLENGE_SELECTED'
    || value === 'INSTANCE_CREATED'
    || value === 'DEFENSE_REQUESTED'
    || value === 'PATCH_UPLOADED'
    || value === 'DEFENSE_CHECK_PASSED'
    || value === 'DEFENSE_CHECK_FAILED'
    || value === 'ATTACK_SUBMITTED'
    || value === 'ATTACK_ACCEPTED'
    || value === 'ATTACK_REJECTED'
    || value === 'SERVICE_ERROR'
    || value === 'SCORE_UPDATED'
    || value === 'TEAM_RANK_CHANGED'
  ) {
    return value
  }

  return null
}
