import { message as describeMessage } from '../../utils/i18n'
import type { InjectionKey } from 'vue'
import type { NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse } from '../../api'
import { getGameplayFactStatusEndpoint } from '../../api'
import { parseApiError } from '../../utils/api-error'

type StatusReader = (competitionId: string, gameplayFactId: string) => Promise<NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse>

export const challengeGameplayFactStatusKey: InjectionKey<StatusReader> = Symbol('challengeGameplayFactStatus')

async function readStatus(
  competitionId: string,
  gameplayFactId: string,
  signal?: AbortSignal,
): Promise<NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse> {
  const { data, error } = await getGameplayFactStatusEndpoint({
    path: { competitionId, gameplayFactId },
    signal,
  })
  if (error || !data)
    throw parseApiError(error, describeMessage('common.flagSubmit.error.submissionStatusFailed'))
  return data
}

export function createChallengeGameplayFactStatusReader(
  signal?: AbortSignal,
  fetchStatus: typeof readStatus = readStatus,
): StatusReader {
  const pending = new Map<string, Promise<NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse>>()

  return (competitionId, gameplayFactId) => {
    const key = `${competitionId.toLowerCase()}:${gameplayFactId.replaceAll('-', '').toLowerCase()}`
    const active = pending.get(key)
    if (active) return active

    const request = fetchStatus(competitionId, gameplayFactId, signal)
    pending.set(key, request)
    const clear = () => {
      if (pending.get(key) === request) pending.delete(key)
    }
    void request.then(clear, clear)
    return request
  }
}
