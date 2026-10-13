import { computed, markRaw, onMounted, onUnmounted, ref, watch } from 'vue'
import { adminApplyAwdScoringRules, adminGetAwdScoringRules, adminPreviewAwdScoringRules } from '../../../api'
import type { NoCtfapiEndpointsAdministrationCompetitionsAwdScoringRulesResponse as Rules, NoCtfApplicationCompetitionsAwdAwdScoringRulePreview as Preview, NoCtfapiEndpointsAdministrationCompetitionsPreviewAwdScoringRulesRequest as Candidate, NoCtfApplicationCompetitionsAwdAwdScoringTeamImpact as TeamImpact } from '../../../api'
import ConfigFieldInputComponent from '../ConfigFieldInput.vue'
import DefinitionSectionComponent from '../../../components/ui/section/DefinitionSection.vue'
import { useOffsetPagination } from '../../../composables/useOffsetPagination'
import { awdFieldVisible, awdInvalidFields } from '../../../utils/awd-config-fields'
import { awdScoringFields, awdScoringPolicy, awdScoringValues, awdPolicyRows } from './awd-scoring-policy'
import type { ConfigValues } from '../../../utils/game-config'
import { message, translate, type UiMessage } from '../../../utils/i18n'
import { parseApiError } from '../../../utils/api-error'
import { toast } from '../../../utils/message-toast'

export interface AwdScoringRuleChangesProps { competitionId: string; competitionChallengeId: string; canWrite: boolean }
export function useAwdScoringRuleChanges(props: AwdScoringRuleChangesProps) {
  const path = () => ({ competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId })
  const requestedRound = ref<number | null>(null)
  const rules = ref<Rules | null>(null)
  const selectedRulesId = ref('')
  const selectedPolicy = computed(() => rules.value?.policies?.find(item => item.rulesId === selectedRulesId.value))
  const fields = computed(awdScoringFields)
  const values = ref<ConfigValues>({})
  const firstRound = ref<number | null>(null)
  const lastRound = ref<number | null>(null)
  const timing = ref<'Immediately' | 'RecalculateHistory'>('RecalculateHistory')
  const reason = ref('')
  const loading = ref(false)
  const readError = ref<UiMessage | null>(null)
  const actionError = ref<UiMessage | null>(null)
  const preview = ref<Preview | null>(null)
  const previewOpen = ref(false)
  const previewLoading = ref(false)
  const saving = ref(false)
  let readGeneration = 0
  let previewGeneration = 0
  let readAbort: AbortController | undefined
  let previewAbort: AbortController | undefined
  let disposed = false
  let reviewedCandidate: Candidate | null = null
  const now = ref(Date.now())
  const localPreviewDeadline = ref(0)
  const previewExpired = computed(() => now.value >= localPreviewDeadline.value)
  const clock = setInterval(() => { now.value = Date.now() }, 1000)
  const invalidFields = computed(() => awdInvalidFields(fields.value, values.value))
  const validRange = computed(() => Number.isSafeInteger(firstRound.value) && Number.isSafeInteger(lastRound.value)
    && firstRound.value! > 0 && lastRound.value! >= firstRound.value!
    && (timing.value !== 'Immediately' || firstRound.value === lastRound.value))
  const canPreview = computed(() => props.canWrite && !!selectedPolicy.value && !loading.value && !saving.value && !previewLoading.value
    && validRange.value && invalidFields.value.length === 0 && !!reason.value.trim() && reason.value.trim().length <= 512)
  const canApply = computed(() => props.canWrite && !!preview.value?.token && !previewExpired.value && !!reviewedCandidate
    && !saving.value && !previewLoading.value && !loading.value)
  const groups = computed(() => ['scoring', 'checker', 'sla', 'penalty', 'composition'].map(key => ({ key,
    title: translate(`administration.awd.group.${key}`), fields: fields.value.filter(field => field.group === key
      && (awdFieldVisible(field, values.value) || invalidFields.value.includes(field.key))) })).filter(group => group.fields.length))
  const proposedRows = computed(() => preview.value && reviewedCandidate ? awdPolicyRows(reviewedCandidate.policy, reviewedCandidate.policy) : [])
  const policyRows = computed(() => awdPolicyRows(selectedPolicy.value?.issuedPolicy, selectedPolicy.value?.effectivePolicy))
  const impactPagination = useOffsetPagination<TeamImpact>(async ({ offset, limit }) => ({
    items: (preview.value?.teams ?? []).slice(offset, offset + limit), total: preview.value?.teams?.length ?? 0,
  }))
  const changedTeams = computed(() => preview.value?.teams?.filter(team => team.scoreBefore !== team.scoreAfter).length ?? 0)
  function invalidatePreview() {
    previewGeneration++
    previewAbort?.abort()
    preview.value = null
    reviewedCandidate = null
    previewOpen.value = false
    previewLoading.value = false
    impactPagination.reset()
  }
  watch([values, firstRound, lastRound, timing, reason], invalidatePreview, { deep: true, flush: 'sync' })
  watch(selectedPolicy, current => {
    values.value = current?.effectivePolicy ? awdScoringValues(current.effectivePolicy) : {}
  }, { flush: 'sync' })
  function updateField(key: string, value: unknown) { if (!saving.value) values.value = { ...values.value, [key]: value } }
  async function loadRules() {
    if (!props.canWrite || saving.value || (requestedRound.value != null && (!Number.isSafeInteger(requestedRound.value) || requestedRound.value < 1))) return
    const generation = ++readGeneration
    readAbort?.abort(); readAbort = new AbortController()
    invalidatePreview()
    rules.value = null; selectedRulesId.value = ''; readError.value = null; loading.value = true
    try {
      const { data, error } = await adminGetAwdScoringRules({ path: path(), query: { roundNumber: requestedRound.value }, signal: readAbort.signal })
      if (error || !data) throw parseApiError(error, message('administration.awd.scoring.readFailure'))
      if (generation !== readGeneration) return
      rules.value = data
      selectedRulesId.value = data.policies?.at(-1)?.rulesId ?? ''
      firstRound.value = data.roundNumber ?? null; lastRound.value = data.roundNumber ?? null
      reason.value = ''; actionError.value = null
    }
    catch (error) { if (generation === readGeneration) readError.value = parseApiError(error, message('administration.awd.scoring.readFailure')).displayMessage }
    finally { if (generation === readGeneration) loading.value = false }
  }
  async function previewChanges() {
    if (!canPreview.value) return
    invalidatePreview()
    const generation = previewGeneration
    const requestedAt = Date.now()
    const candidate: Candidate = { firstRound: firstRound.value!, lastRound: lastRound.value!, timing: timing.value,
      reason: reason.value.trim(), policy: awdScoringPolicy(values.value) }
    previewAbort = new AbortController()
    previewLoading.value = true; actionError.value = null
    try {
      const { data, error } = await adminPreviewAwdScoringRules({ path: path(), body: candidate, signal: previewAbort.signal })
      if (error || !data?.token) throw parseApiError(error)
      if (generation !== previewGeneration) return
      reviewedCandidate = candidate
      // Use the server's preview lifetime; a workstation clock offset must not extend a token's life.
      const lifetime = Date.parse(data.expiresAt ?? '') - Date.parse(data.projectedAt ?? '')
      localPreviewDeadline.value = requestedAt + (Number.isFinite(lifetime) ? Math.max(0, lifetime) : 0)
      now.value = Date.now()
      preview.value = data
      await impactPagination.loadPage(1)
      if (generation === previewGeneration) previewOpen.value = true
    }
    catch (error) { if (generation === previewGeneration) actionError.value = parseApiError(error).displayMessage }
    finally { if (generation === previewGeneration) previewLoading.value = false }
  }
  async function applyChanges() {
    if (!canApply.value || !reviewedCandidate || !preview.value?.token) return
    const body = { ...reviewedCandidate, previewToken: preview.value.token }
    saving.value = true; actionError.value = null
    try {
      const { data, error } = await adminApplyAwdScoringRules({ path: path(), body })
      if (error || !data) throw parseApiError(error)
      if (disposed) return
      invalidatePreview()
      toast.success(message('administration.awd.scoring.saved'))
      saving.value = false
      await loadRules()
    }
    catch (error) {
      if (!disposed) { invalidatePreview(); actionError.value = parseApiError(error).displayMessage }
    }
    finally { saving.value = false }
  }
  function closePreview() { if (!saving.value) previewOpen.value = false }
  const auditOpen = ref(false)
  const auditEventId = ref<string | undefined>()
  const auditRows = computed(() => {
    const policy = rules.value?.recentChanges?.find(item => item.eventId === auditEventId.value)?.policy
    return awdPolicyRows(policy, policy)
  })
  function showAuditPolicy(eventId?: string) {
    auditEventId.value = eventId
    auditOpen.value = true
  }
  watch(() => [props.competitionId, props.competitionChallengeId, props.canWrite], () => { invalidatePreview(); void loadRules() })
  onMounted(() => void loadRules())
  onUnmounted(() => { disposed = true; readGeneration++; readAbort?.abort(); invalidatePreview(); clearInterval(clock) })
  return { requestedRound, rules, selectedRulesId, selectedPolicy, values, firstRound, lastRound, timing, reason, loading, readError, actionError,
    preview, previewOpen, previewLoading, saving, previewExpired, canPreview, canApply, groups, invalidFields, validRange, policyRows, proposedRows,
    changedTeams, updateField, loadRules, previewChanges, applyChanges, closePreview, auditOpen, auditRows, showAuditPolicy,
    impactItems: impactPagination.items, impactPage: impactPagination.page, impactPageCount: impactPagination.pageCount, impactLimit: impactPagination.limit, impactTotal: impactPagination.total,
    loadImpactPage: impactPagination.loadPage, setImpactLimit: impactPagination.setPageSize,
    ConfigFieldInput: markRaw(ConfigFieldInputComponent), DefinitionSection: markRaw(DefinitionSectionComponent) }
}
export type AwdScoringRuleChangesViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAwdScoringRuleChanges>>
