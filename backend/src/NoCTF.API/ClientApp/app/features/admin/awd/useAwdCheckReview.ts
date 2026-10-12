import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { adminAdjudicateAwdCheck, adminGetAwdCheckEvidence, adminListAwdChecks } from '../../../api'
import type { NoCtfapiEndpointsAdministrationGameplayFactsAwdCheckEvidenceResponse as Evidence, NoCtfapiEndpointsAdministrationGameplayFactsAwdCheckListItemResponse as Check, NoCtfDomainCompetitionsAwdAwdCheckAdjudicationAction as Action, NoCtfDomainCompetitionsAwdAwdSettlementState as Settlement, NoCtfDomainCompetitionsAwdAwdSettlementState2 as SettlementQuery } from '../../../api'
import { useOffsetPagination } from '../../../composables/useOffsetPagination'
import { parseApiError } from '../../../utils/api-error'
import { message, translate, type UiMessage } from '../../../utils/i18n'
import { toast } from '../../../utils/message-toast'
import { awdCheckLabel, awdEvidenceCanBeReviewed } from './awd-check-evidence'

export interface AwdCheckReviewProps {
  competitionId: string
  canJudge: boolean
  teams: { id: string; name: string }[]
  challenges: { id: string; title: string }[]
}

export function useAwdCheckReview(props: AwdCheckReviewProps) {
  const team = ref('all')
  const challenge = ref('all')
  const round = ref<number | null>(null)
  const settlement = ref<Settlement | 'all'>('PendingAdjudication')
  const settlements: Record<Settlement, SettlementQuery> = { Provisional: 0, Final: 1, PendingAdjudication: 2, Exempted: 3 }
  const settlementOptions = Object.keys(settlements) as Settlement[]
  const roundValid = computed(() => round.value == null || (Number.isSafeInteger(round.value) && round.value >= 1))
  let listAbort: AbortController | undefined
  const pagination = useOffsetPagination<Check>(async query => {
    if (!roundValid.value) return { items: [], total: 0 }
    listAbort?.abort()
    listAbort = new AbortController()
    const { data, error } = await adminListAwdChecks({ path: { competitionId: props.competitionId }, query: {
      ...query, allSettlements: settlement.value === 'all', settlement: settlement.value === 'all' ? null : settlements[settlement.value],
      teamId: team.value === 'all' ? null : team.value, competitionChallengeId: challenge.value === 'all' ? null : challenge.value,
      roundNumber: round.value,
    }, signal: listAbort.signal })
    if (error || !data) throw parseApiError(error)
    return data
  }, { initialPageSize: 10, initialDesc: true })
  const teamName = (id?: string) => props.teams.find(item => item.id === id)?.name ?? id ?? '—'
  const challengeTitle = (id?: string) => props.challenges.find(item => item.id === id)?.title ?? id ?? '—'
  const detailOpen = ref(false)
  const evidence = ref<Evidence | null>(null)
  const selected = ref<Check | null>(null)
  const detailLoading = ref(false)
  const detailError = ref<UiMessage | null>(null)
  const action = ref<Action>('AcceptEvidence')
  const reason = ref('')
  const saving = ref(false)
  const needsReload = ref(false)
  let detailAbort: AbortController | undefined
  let detailGeneration = 0
  let disposed = false
  const canAccept = computed(() => awdEvidenceCanBeReviewed(evidence.value))
  const canAdjudicate = computed(() => props.canJudge && evidence.value?.settlement === 'PendingAdjudication'
    && !saving.value && !detailLoading.value && !needsReload.value && !!reason.value.trim() && reason.value.trim().length <= 512
    && (action.value === 'Exempt' || canAccept.value))
  async function loadEvidence() {
    const id = selected.value?.gameplayFactId
    if (!id || saving.value) return
    const generation = ++detailGeneration
    detailAbort?.abort()
    detailAbort = new AbortController()
    evidence.value = null
    detailError.value = null
    detailLoading.value = true
    needsReload.value = false
    try {
      const { data, error } = await adminGetAwdCheckEvidence({ path: { competitionId: props.competitionId, gameplayFactId: id }, signal: detailAbort.signal })
      if (error || !data) throw parseApiError(error)
      if (generation === detailGeneration) evidence.value = data
    }
    catch (error) { if (generation === detailGeneration) detailError.value = parseApiError(error).displayMessage }
    finally { if (generation === detailGeneration) detailLoading.value = false }
  }
  function openEvidence(item: Check) {
    if (saving.value) return
    selected.value = item
    reason.value = ''
    action.value = 'AcceptEvidence'
    detailOpen.value = true
    void loadEvidence()
  }
  function refresh() { pagination.reset(); void pagination.loadPage(1) }
  async function adjudicate() {
    const current = evidence.value
    if (!canAdjudicate.value || !current?.gameplayFactId) return
    saving.value = true
    detailError.value = null
    const generation = detailGeneration
    try {
      const { data, error } = await adminAdjudicateAwdCheck({ path: { competitionId: props.competitionId, gameplayFactId: current.gameplayFactId },
        body: { action: action.value, reason: reason.value.trim(), expectedAttemptId: current.attemptId, expectedReceivedAt: current.receivedAt } })
      if (error || !data) throw parseApiError(error)
      if (disposed) return
      if (generation === detailGeneration) { evidence.value = data; reason.value = '' }
      toast.success(message('administration.awd.review.saved'))
      refresh()
    }
    catch (error) {
      if (generation !== detailGeneration || disposed) return
      const parsed = parseApiError(error)
      detailError.value = parsed.displayMessage
      needsReload.value = true
    }
    finally { saving.value = false }
  }
  watch(detailOpen, open => {
    if (open) return
    detailGeneration++
    detailAbort?.abort()
    detailLoading.value = false
    evidence.value = null
  })
  watch([team, challenge, round, settlement, () => props.competitionId], () => { detailOpen.value = false; refresh() })
  onMounted(() => void pagination.loadPage(1))
  onUnmounted(() => { disposed = true; detailGeneration++; listAbort?.abort(); detailAbort?.abort(); pagination.reset() })
  const evidenceTimes = computed(() => {
    const current = evidence.value
    if (!current) return []
    return (['scheduledAt', 'attemptStartedAt', 'attemptDeadline', 'observedAt', 'receivedAt', 'receiptDeadline', 'adjudicatedAt'] as const)
      .map(key => ({ key, label: translate(`administration.awd.review.${key}`), value: current[key] }))
  })
  return { team, challenge, round, roundValid, settlement, settlementOptions, teams: computed(() => props.teams), challenges: computed(() => props.challenges),
    canJudge: computed(() => props.canJudge), teamName, challengeTitle, label: awdCheckLabel,
    items: pagination.items, total: pagination.total, page: pagination.page, pageCount: pagination.pageCount, pageLimit: pagination.limit,
    loading: pagination.loading, listError: pagination.error, loadPage: pagination.loadPage, setPageSize: pagination.setPageSize, refresh,
    detailOpen, evidence, selected, detailLoading, detailError, action, reason, saving, needsReload, canAccept, canAdjudicate, loadEvidence, openEvidence, adjudicate, evidenceTimes }
}
export type AwdCheckReviewViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAwdCheckReview>>
