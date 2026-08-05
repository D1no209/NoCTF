import type {
  NoCtfapiEndpointsAdministrationChallengeBankChallengeAttachmentResponse,
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfApplicationScoringLeaderboardLeaderboardDataScope,
  NoCtfDomainCompetitionsCompetitionLeaderboardVisibility,
} from './generated/types.gen'

export interface PublicChallenge {
  id: string
  competitionId: string
  challengeId: string
  title: string
  description: string | null
  direction: string
  baseScore: number | null
  order: number
  isPublished: boolean
  revision: number
  deletedAt: string | null
  createdAt: string
  updatedAt: string
  controlFlag: string | null
  urls: string[] | null
  leaderboardVisibility: NoCtfDomainCompetitionsCompetitionLeaderboardVisibility
  dataScope: NoCtfApplicationScoringLeaderboardLeaderboardDataScope
}

export interface PublicChallengeAttachment {
  id: string
  challengeId: string
  fileName: string
  contentType: string
  byteLength: number
  sha256: string
  deletedAt: string | null
  createdAt: string
}

export interface PublicChallengeDownload {
  content: Blob
  fileName: string | null
}

const encodedFileNamePattern = /filename\*\s*=\s*UTF-8''([^;]+)/i
const quotedFileNamePattern = /filename\s*=\s*"([^"]+)"/i
const plainFileNamePattern = /filename\s*=\s*([^;]+)/i

function requireString(value: string | undefined, field: string) {
  if (typeof value !== 'string' || value.length === 0)
    throw new TypeError(`Challenge response is missing ${field}.`)
  return value
}

function requireNumber(value: number | undefined, field: string) {
  if (typeof value !== 'number' || !Number.isFinite(value))
    throw new TypeError(`Challenge response is missing ${field}.`)
  return value
}

function nullableNumber(value: number | null | undefined, field: string) {
  if (value !== undefined && value !== null && (!Number.isFinite(value)))
    throw new TypeError(`Challenge response has an invalid ${field}.`)
  return value ?? null
}

function optionalString(value: string | null | undefined, field: string) {
  if (value !== undefined && value !== null && typeof value !== 'string')
    throw new TypeError(`Challenge response has an invalid ${field}.`)
  return value ?? null
}

export function toPublicChallenge(
  value: NoCtfapiEndpointsChallengesChallengeResponse,
): PublicChallenge {
  if (typeof value.isPublished !== 'boolean')
    throw new TypeError('Challenge response is missing isPublished.')
  if (
    value.urls !== undefined
    && value.urls !== null
    && (!Array.isArray(value.urls) || value.urls.some(url => typeof url !== 'string'))
  ) {
    throw new TypeError('Challenge response has invalid urls.')
  }

  return {
    id: requireString(value.id, 'id'),
    competitionId: requireString(value.competitionId, 'competitionId'),
    challengeId: requireString(value.challengeId, 'challengeId'),
    title: requireString(value.title, 'title'),
    description: optionalString(value.description, 'description'),
    direction: requireString(value.direction, 'direction'),
    baseScore: nullableNumber(value.baseScore, 'baseScore'),
    order: requireNumber(value.order, 'order'),
    isPublished: value.isPublished,
    revision: requireNumber(value.revision, 'revision'),
    deletedAt: optionalString(value.deletedAt, 'deletedAt'),
    createdAt: requireString(value.createdAt, 'createdAt'),
    updatedAt: requireString(value.updatedAt, 'updatedAt'),
    controlFlag: optionalString(value.controlFlag, 'controlFlag'),
    urls: value.urls ? [...value.urls] : null,
    leaderboardVisibility: value.leaderboardVisibility ?? 0,
    dataScope: value.dataScope ?? 0,
  }
}

export function toPublicChallengeAttachment(
  value: NoCtfapiEndpointsAdministrationChallengeBankChallengeAttachmentResponse,
): PublicChallengeAttachment {
  return {
    id: requireString(value.id, 'attachment id'),
    challengeId: requireString(value.challengeId, 'attachment challengeId'),
    fileName: requireString(value.fileName, 'attachment fileName'),
    contentType: requireString(value.contentType, 'attachment contentType'),
    byteLength: requireNumber(value.byteLength, 'attachment byteLength'),
    sha256: requireString(value.sha256, 'attachment sha256'),
    deletedAt: optionalString(value.deletedAt, 'attachment deletedAt'),
    createdAt: requireString(value.createdAt, 'attachment createdAt'),
  }
}

export function readDownloadFileName(contentDisposition: string | null): string | null {
  if (!contentDisposition)
    return null

  const encoded = contentDisposition.match(encodedFileNamePattern)?.[1]?.trim()
  if (encoded) {
    try {
      return decodeURIComponent(encoded)
    }
    catch {
      return null
    }
  }

  return (
    contentDisposition.match(quotedFileNamePattern)?.[1]
    ?? contentDisposition.match(plainFileNamePattern)?.[1]?.trim()
    ?? null
  )
}

export function shouldOfferRandomAttachment(
  status: number | undefined,
  participantEligible: boolean,
  hasChallengeDetail: boolean,
) {
  return status === 404 && participantEligible && hasChallengeDetail
}
