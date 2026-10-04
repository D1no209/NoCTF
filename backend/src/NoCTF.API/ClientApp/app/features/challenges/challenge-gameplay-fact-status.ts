
import { api, RequestPolicyOption } from '../../lib/api'
import { message as describeMessage } from '../../utils/i18n'
import type { InjectionKey } from 'vue'
import type { NoCTFAPIEndpointsGameplayFactsGameplayFactStatusResponse } from '../../api/models'

import { parseApiError } from '../../utils/api-error'

type StatusReader = (competitionId: string, gameplayFactId: string) => Promise<NoCTFAPIEndpointsGameplayFactsGameplayFactStatusResponse>

export const challengeGameplayFactStatusKey: InjectionKey<StatusReader> = Symbol('challengeGameplayFactStatus')

async function readStatus(
  competitionId: string,
  gameplayFactId: string,
  signal?: AbortSignal,
): Promise<NoCTFAPIEndpointsGameplayFactsGameplayFactStatusResponse> {
  let error: unknown;
  const data = await api.api.v1.competitions.byCompetitionId(competitionId).gameplayFacts.byGameplayFactId(gameplayFactId).get({ options: [new RequestPolicyOption({ signal: signal })] }).catch(cause => { error = cause; return undefined });
  if (error || !data)
    throw parseApiError(error, describeMessage('common.flagSubmit.error.submissionStatusFailed'))
  return data
}

export function createChallengeGameplayFactStatusReader(
  signal?: AbortSignal,
  fetchStatus: typeof readStatus = readStatus,
): StatusReader {
  const pending = new Map<string, Promise<NoCTFAPIEndpointsGameplayFactsGameplayFactStatusResponse>>()

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
