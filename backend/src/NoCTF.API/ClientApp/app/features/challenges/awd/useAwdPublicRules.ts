import { computed, onMounted, onBeforeUnmount, ref, watch } from 'vue'
import { getAwdRules } from '../../../api'
import type { NoCtfapiEndpointsChallengesAwdPublicRulesResponse as Rules } from '../../../api'
import { watchCompetition } from '../../../composables/useCompetitionHub'
import { ensureLocaleDomains, message, translate, type UiMessage } from '../../../utils/i18n'
import { parseApiError } from '../../../utils/api-error'
import { publicRuleGroups, publicFlagWindow } from './awd-public-rules'

export interface AwdPublicRulesProps { competitionId: string; competitionChallengeId: string }
export function useAwdPublicRules(props: AwdPublicRulesProps) {
  const rules = ref<Rules | null>(null), loading = ref(false), error = ref<UiMessage | null>(null)
  const open = ref(false), tab = ref('current'), selectedRulesId = ref('')
  let generation = 0, disposed = false, queued = false, mounted = false
  let abort: AbortController | undefined, stopHub: (() => void) | undefined, timer: ReturnType<typeof setInterval> | undefined
  const current = computed(() => rules.value?.issuedPolicies?.find(policy => policy.rulesId === selectedRulesId.value))
  const policy = computed(() => tab.value === 'next' ? rules.value?.nextRound : current.value)
  const groups = computed(() => rules.value && policy.value ? publicRuleGroups(rules.value, policy.value, tab.value === 'next') : [])
  const rankingRows = computed(() => (tab.value === 'next' ? rules.value?.nextTieBreakers : rules.value?.currentTieBreakers)?.map((rule, index) => ({
    index, field: translate(`administration.awd.tieBreak.field.${rule.field}`), direction: translate(`administration.awd.tieBreak.${rule.direction}`),
  })) ?? [])
  const flagWindow = computed(() => publicFlagWindow(policy.value, false))
  const historyWindow = computed(() => publicFlagWindow(policy.value, true))
  const health = computed(() => rules.value?.ownService)
  const healthText = (value: string | null | undefined) => translate(`challenges.awdRules.health.${value ?? 'Unknown'}`)
  const serviceState = computed(() => health.value?.enabled === false ? 'Disabled' : health.value?.health ?? (health.value?.execution === 'Pending' || health.value?.execution === 'Running' ? health.value.execution : 'Unknown'))
  const serviceText = computed(() => healthText(serviceState.value))
  const settlementText = computed(() => translate(`challenges.awdRules.settlement.${health.value?.settlement ?? 'Provisional'}`))
  const observedAt = computed(() => formatDate(health.value?.observedAt))
  function formatDate(value: string | null | undefined) { return value ? new Date(value).toLocaleString() : '—' }
  async function loadRules() {
    if (disposed) return
    if (loading.value) { queued = true; return }
    const ticket = generation
    loading.value = true; abort = new AbortController()
    try {
      await ensureLocaleDomains(['administration'])
      if (disposed || ticket !== generation) return
      const response = await getAwdRules({ path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId }, signal: abort.signal })
      if (disposed || ticket !== generation) return
      if (response.error || !response.data) {
        if ([401, 403, 404].includes(response.response?.status ?? 0)) rules.value = null
        error.value = parseApiError(response.error, message('challenges.awdRules.loadFailed')).displayMessage
        return
      }
      rules.value = response.data; error.value = null
      if (!response.data.issuedPolicies?.some(policy => policy.rulesId === selectedRulesId.value))
        selectedRulesId.value = response.data.issuedPolicies?.at(-1)?.rulesId ?? ''
      if (!selectedRulesId.value) tab.value = 'next'
    } catch { if (!disposed && ticket === generation) error.value = message('challenges.awdRules.loadFailed') }
    finally { if (!disposed && ticket === generation) { loading.value = false; if (queued) { queued = false; void loadRules() } } }
  }
  function subscribe() {
    stopHub?.()
    const refresh = () => { void loadRules() }
    stopHub = watchCompetition(props.competitionId, { scoreboardUpdated: refresh, competitionLifecycleChanged: refresh, competitionEventChanged: refresh, onReconnected: refresh })
  }
  watch(() => [props.competitionId, props.competitionChallengeId], () => {
    generation++; abort?.abort(); queued = false; loading.value = false; rules.value = null; error.value = null
    selectedRulesId.value = ''; tab.value = 'current'; open.value = false
    void loadRules(); if (mounted) subscribe()
  }, { immediate: true })
  const visibility = () => { if (!document.hidden) void loadRules() }
  onMounted(() => { mounted = true; subscribe(); timer = setInterval(visibility, 30000); document.addEventListener('visibilitychange', visibility) })
  onBeforeUnmount(() => { disposed = true; generation++; abort?.abort(); stopHub?.(); clearInterval(timer); document.removeEventListener('visibilitychange', visibility) })
  return { rules, loading, error, open, tab, selectedRulesId, policy, groups, rankingRows, flagWindow, historyWindow, health, serviceText, settlementText, observedAt, healthText, formatDate, loadRules }
}
export type AwdPublicRulesViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAwdPublicRules>>
