import type { AwdpScreenEvent, AwdpScreenEventLevel, AwdpScreenEventType, AwdpScreenSnapshot } from '@/types/awdpScreen'
import { ApiError, apiUrl } from '@/api/noctf'

function authHeaders(): Record<string, string> {
  const token = localStorage.getItem('accessToken')
  return token ? { Authorization: `Bearer ${token}` } : {}
}

async function requestJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(apiUrl(url), {
    method: 'GET',
    headers: {
      Accept: 'application/json',
      ...authHeaders(),
    },
    signal,
  })

  if (!response.ok) {
    throw new ApiError('Failed to load AWDP screen data', response.status)
  }

  const contentType = response.headers.get('content-type') ?? ''
  const body = await response.text()
  if (!contentType.toLowerCase().includes('application/json')) {
    throw new ApiError(
      'AWDP screen endpoint did not return JSON. Rebuild or restart the backend so /api/awdp/screen/snapshot is registered.',
      response.status,
      body.slice(0, 160),
    )
  }

  try {
    return JSON.parse(body) as T
  }
  catch (err) {
    throw new ApiError('AWDP screen endpoint returned invalid JSON', response.status, err)
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
  const round = event.round > 0 ? `Round ${event.round}` : 'Round'
  const team = event.teamName ?? 'Team'
  const challenge = event.challengeName ?? 'challenge'

  switch (event.type) {
    case 'ROUND_STARTED':
      return `${round} started`
    case 'ROUND_ENDED':
      return `${round} ended`
    case 'CHALLENGE_SELECTED':
      return `${round} / ${team} selected ${challenge}`
    case 'INSTANCE_CREATED':
      return `${round} / ${team} created ${challenge} instance`
    case 'DEFENSE_REQUESTED':
      return `${round} / ${team} requested defense on ${challenge}`
    case 'PATCH_UPLOADED':
      return `${round} / ${team} uploaded patch for ${challenge}`
    case 'DEFENSE_CHECK_PASSED':
      return `${round} / ${team} defense succeeded on ${challenge}`
    case 'DEFENSE_CHECK_FAILED':
      return `${round} / ${team} defense failed on ${challenge}`
    case 'ATTACK_SUBMITTED':
      return `${round} / ${team} submitted attack on ${challenge}`
    case 'ATTACK_ACCEPTED':
      return `${round} / ${team} attack succeeded on ${challenge}`
    case 'ATTACK_REJECTED':
      return `${round} / ${team} attack failed on ${challenge}`
    case 'SERVICE_ERROR':
      return `${round} / ${team} ${challenge} service error`
    case 'SCORE_UPDATED':
      return `${round} / ${team} round score settled`
    case 'TEAM_RANK_CHANGED':
      return `${round} / ${team} rank changed`
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
