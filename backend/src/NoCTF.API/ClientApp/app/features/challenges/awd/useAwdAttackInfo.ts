import { computed, onBeforeUnmount, onMounted, toRefs } from 'vue'
import { listAwdAttackInfo } from '../../../api'
import type { NoCtfapiEndpointsChallengesAwdAttackInfoResponse } from '../../../api'
import { useOffsetPagination } from '../../../composables/useOffsetPagination'
import { watchCompetition } from '../../../composables/useCompetitionHub'
import { parseApiError } from '../../../utils/api-error'
import { message, translate } from '../../../utils/i18n'

export interface AwdAttackInfoProps { competitionId: string; competitionChallengeId: string; teamId: string; teamName: string }
export function useAwdAttackInfo(props: AwdAttackInfoProps, emit: { (event: 'close'): void }) {
  let abort: AbortController | undefined, stopHub: (() => void) | undefined, timer: ReturnType<typeof setInterval> | undefined
  const pagination = useOffsetPagination<NoCtfapiEndpointsChallengesAwdAttackInfoResponse>(async query => {
    abort?.abort(); abort = new AbortController()
    const request = abort
    const response = await listAwdAttackInfo({ path: { competitionId: props.competitionId,
      competitionChallengeId: props.competitionChallengeId, teamId: props.teamId }, query, signal: request.signal })
    if (request.signal.aborted) throw new Error('Request aborted')
    if (response.error || !response.data) {
      pagination.items.value = []; pagination.total.value = 0
      throw parseApiError(response.error, message('challenges.awdAttackInfo.loadFailed'))
    }
    return response.data
  })
  const rows = computed(() => pagination.items.value.map(item => ({ ...item,
    issuedText: item.issuedAt ? new Date(item.issuedAt).toLocaleString() : '—',
    expiryText: item.exclusiveExpiryRound != null
      ? translate('challenges.awdAttackInfo.expiryRound', { round: item.exclusiveExpiryRound })
      : item.remainingActiveSeconds != null
        ? translate('challenges.awdAttackInfo.expirySeconds', { seconds: Math.ceil(item.remainingActiveSeconds) })
        : translate('challenges.awdAttackInfo.matchEnd'),
  })))
  function refresh() { const page = pagination.page.value; abort?.abort(); pagination.reset(); void pagination.loadPage(page) }
  function visibleRefresh() { if (!document.hidden) refresh() }
  function setOpen(open: boolean) { if (!open) emit('close') }
  onMounted(() => {
    refresh()
    stopHub = watchCompetition(props.competitionId, { scoreboardUpdated: refresh, competitionLifecycleChanged: refresh,
      competitionEventChanged: refresh, onReconnected: refresh })
    timer = setInterval(visibleRefresh, 30000)
    document.addEventListener('visibilitychange', visibleRefresh)
  })
  onBeforeUnmount(() => { abort?.abort(); pagination.reset(); stopHub?.(); clearInterval(timer); document.removeEventListener('visibilitychange', visibleRefresh) })
  return { ...toRefs(props), ...pagination, rows, refresh, setOpen }
}
export type AwdAttackInfoViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAwdAttackInfo>>
