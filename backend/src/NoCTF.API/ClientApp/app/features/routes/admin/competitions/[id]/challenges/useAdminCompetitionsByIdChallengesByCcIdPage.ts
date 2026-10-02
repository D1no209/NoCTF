import { adminTeamPath, adminTemplatePath } from '~/features/admin/admin-navigation'
import { proxyRefs } from 'vue'
import { markRaw } from 'vue'

import { Plus } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminCreateManualAdjustment, adminCreateCompetitionChallengeHint, adminDeleteCompetitionChallengeHint, adminGetCompetition, adminGetCompetitionChallenge, adminListGameplayFacts, adminListCompetitionChallengeHints, adminListTeams, adminPatchCompetitionChallenge, adminRestoreCompetitionChallengeHint, adminUpdateCompetitionChallengeHint, getLeaderboardEndpoint, getScoreboardSchemaEndpoint } from '../../../../../../api'
import type { NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse, NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeRulesContract, NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract, NoCtfapiEndpointsChallengesChallengeResponse, NoCtfapiEndpointsCompetitionsGameModeProtocol, NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse, NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse, NoCtfapiEndpointsGameplayFactsGameplayFactListResponse, NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse, NoCtfapiEndpointsTeamsTeamResponse } from '../../../../../../api'
import { useOffsetPagination } from '../../../../../../composables/useOffsetPagination'
import { useCompetitionAdmin } from '../../../../../../lib/admin-competition'

import ChallengeRulesEditorComponent from '../../../../../admin/ChallengeRulesEditor.vue'

interface ChallengeTeamScoringRow {
  team: NoCtfapiEndpointsTeamsTeamResponse
  score: number
  adjustment: number
  progressLabel: string
  progressVariant: 'default' | 'secondary' | 'outline'
}

interface ChallengeConfiguration {
  mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol
  rules?: NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeRulesContract
}

/** Owns state, effects and commands for AdminCompetitionsByIdChallengesByCcIdPage. */
export function useAdminCompetitionsByIdChallengesByCcIdPage() {
  const route = useRoute()

  const ccId = route.params.ccId as string

  const { competitionId, competition, canWrite, canJudge } = useCompetitionAdmin()

  const challenge = ref<NoCtfapiEndpointsChallengesChallengeResponse | null>(null)

  const hiddenRuleKeys = computed(() => {
    if (challenge.value?.interactionKind === 'PatchVerification')
      return ['maxFlagAttempts', 'flagTemplate']
    const hidden = ['maxPatchAttempts']
    if (challenge.value?.usesDynamicFlag !== true) hidden.push('flagTemplate')
    return hidden
  })

  const loading = ref(true)

  const loadError = ref<string | null>(null)

  const activeSection = ref('general')

  const sectionOptions = computed(() => [
    { value: 'general', label: translate('ui.basicSettings') },
    { value: 'config', label: translate('ui.questionConfiguration') },
    { value: 'hints', label: translate('ui.hint') },
    { value: 'scoring', label: translate('ui.teamScoring') },
  ])

  async function loadChallenge() {
    loading.value = true
    const { data, error } = await adminGetCompetitionChallenge({
      path: { competitionId, competitionChallengeId: ccId },
      query: { includeDeleted: false },
    })
    if (error) loadError.value = parseApiError(error).message
    else challenge.value = data?.challenge ?? null
    loading.value = false
  }

  const editCustomTitle = ref('')

  const editOrder = ref(0)

  const editPublished = ref(false)

  const savingEdit = ref(false)

  watch(challenge, (c) => {
    if (!c) return
    editCustomTitle.value = c.customTitle ?? ''
    editOrder.value = c.order ?? 0
    editPublished.value = c.isPublished ?? false
  }, { immediate: true })

  async function saveEdit() {
    if (!challenge.value) return
    savingEdit.value = true
    try {
      const { data, error } = await adminPatchCompetitionChallenge({
        path: { competitionId, competitionChallengeId: ccId },
        body: { presentation: {
          customTitle: editCustomTitle.value.trim() || null,
          order: editOrder.value,
          isPublished: editPublished.value,
        } },
      })
      if (error) throw error
      challenge.value = data?.challenge ?? challenge.value
      toast.success(translate("ui.questionSettingsSaved"))
    }
    catch (e) {
      toastWriteError(e)
    }
    finally {
      savingEdit.value = false
    }
  }

  const config = ref<ChallengeConfiguration | null>(null)

  const configLoading = ref(true)

  const savingConfig = ref(false)

  const inheritedConfiguration = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract | null>(null)

  async function loadConfig() {
    configLoading.value = true
    const [challengeResult, competitionResult] = await Promise.all([
      adminGetCompetitionChallenge({
        path: { competitionId, competitionChallengeId: ccId },
        query: { includeDeleted: false },
      }),
      adminGetCompetition({ path: { competitionId } }),
    ])
    if (!challengeResult.error && challengeResult.data) {
      config.value = {
        mode: challengeResult.data.mode,
        rules: challengeResult.data.rules,
      }
    }
    if (!competitionResult.error && competitionResult.data) {
      inheritedConfiguration.value = competitionResult.data.modeConfiguration?.configuration ?? null
    }
    configLoading.value = false
  }

  async function saveConfig(rules: NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeRulesContract) {
    if (!config.value) return
    savingConfig.value = true
    try {
      const { data, error } = await adminPatchCompetitionChallenge({
        path: { competitionId, competitionChallengeId: ccId },
        body: { rules: { configuration: rules } },
      })
      if (error) throw error
      config.value = data
        ? { mode: data.mode, rules: data.rules }
        : config.value
      toast.success(translate("ui.questionConfigurationHasBeenSaved"))
    }
    catch (e) {
      toastWriteError(e)
    }
    finally {
      savingConfig.value = false
    }
  }

  const hints = ref<NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse[]>([])

  const hintsLoading = ref(true)

  const hintsLoadError = ref<string | null>(null)

  const includeDeletedHints = ref(false)

  const hintDialogOpen = ref(false)

  const editingHint = ref<NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse | null>(null)

  const hintForm = ref({ content: '', cost: 0, publishedAt: '' })

  const hintError = ref<string | null>(null)

  const savingHint = ref(false)

  const pendingHintId = ref<string | null>(null)

  async function loadHints() {
    hintsLoading.value = true
    const { data, error } = await adminListCompetitionChallengeHints({
      path: { competitionId, competitionChallengeId: ccId },
      query: { includeDeleted: includeDeletedHints.value },
    })
    if (error || !data) {
      hintsLoadError.value = parseApiError(error).message
    }
    else {
      hintsLoadError.value = null
      hints.value = data.items ?? []
    }
    hintsLoading.value = false
  }

  watch(includeDeletedHints, loadHints)

  function openHintDialog(hint?: NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse) {
    editingHint.value = hint ?? null
    hintError.value = null
    hintForm.value = {
      content: hint?.content ?? '',
      cost: hint?.cost ?? 0,
      publishedAt: isoToLocalInput(hint?.publishedAt),
    }
    hintDialogOpen.value = true
  }

  async function saveHint() {
    if (!hintForm.value.content.trim()) {
      hintError.value = translate("ui.pleaseEnterThePromptContent")
      return
    }
    savingHint.value = true
    hintError.value = null
    const body = {
      content: hintForm.value.content,
      cost: hintForm.value.cost,
      publishedAt: localInputToIso(hintForm.value.publishedAt) ?? null,
    }
    try {
      const path = { competitionId, competitionChallengeId: ccId }
      const { error } = editingHint.value?.id
        ? await adminUpdateCompetitionChallengeHint({ path: { ...path, hintId: editingHint.value.id }, body })
        : await adminCreateCompetitionChallengeHint({ path, body })
      if (error) throw error
      toast.success(editingHint.value ? translate("ui.tipHasBeenUpdated") : translate("ui.promptAdded"))
      hintDialogOpen.value = false
      await loadHints()
    }
    catch (e) {
      hintError.value = parseApiError(e).message
    }
    finally {
      savingHint.value = false
    }
  }

  async function deleteHint(h: NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse) {
    if (!h.id) return
    pendingHintId.value = h.id
    try {
      const { error } = await adminDeleteCompetitionChallengeHint({
        path: { competitionId, competitionChallengeId: ccId, hintId: h.id },
      })
      if (error) throw error
      toast.success(translate("ui.tipHasBeenDeleted"))
      await loadHints()
    }
    catch (e) {
      toast.error(parseApiError(e).message)
    }
    finally {
      pendingHintId.value = null
    }
  }

  async function restoreHint(h: NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse) {
    if (!h.id) return
    pendingHintId.value = h.id
    try {
      const { error } = await adminRestoreCompetitionChallengeHint({
        path: { competitionId, competitionChallengeId: ccId, hintId: h.id },
      })
      if (error) throw error
      toast.success(translate("ui.tipHasBeenRestored"))
      await loadHints()
    }
    catch (e) {
      toast.error(parseApiError(e).message)
    }
    finally {
      pendingHintId.value = null
    }
  }

  const scoringSearch = ref('')

  const scoringPagination = useOffsetPagination<NoCtfapiEndpointsTeamsTeamResponse>(async ({ offset, limit, desc }) => {
    const { data, error } = await adminListTeams({
      path: { competitionId },
      query: { keyword: scoringSearch.value.trim() || null, offset, limit, desc },
    })
    if (error || !data) throw error ?? new Error(translate("ui.failedToLoadTeams"))
    return { items: data.items ?? [], total: data.total ?? 0 }
  })

  const scoringTeams = scoringPagination.items

  const scoringFacts = ref<NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse[]>([])

  const scoringSnapshot = ref<NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse | null>(null)

  const scoringSchema = ref<NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null>(null)

  const scoringContextLoading = ref(true)

  const scoringContextError = ref<string | null>(null)

  const scoringLoading = computed(() => scoringContextLoading.value
    || (scoringPagination.loading.value && !scoringPagination.initialized.value))

  const scoringError = computed(() => scoringContextError.value
    ?? scoringPagination.error.value?.message
    ?? null)

  let scoringLoadGeneration = 0

  async function loadAllChallengeFacts(): Promise<NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse[]> {
    const facts: NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse[] = []
    let offset = 0
    let total = 0
    do {
      const response: {
        data?: NoCtfapiEndpointsGameplayFactsGameplayFactListResponse
        error?: unknown
      } = await adminListGameplayFacts({
        path: { competitionId },
        query: { competitionChallengeId: ccId, offset, limit: 200, desc: true },
      })
      if (response.error || !response.data)
        throw response.error ?? new Error(translate("ui.failedToLoadThisChallengeSAdjudicationRecords"))
      facts.push(...(response.data.items ?? []))
      total = response.data.total ?? facts.length
      offset += response.data.items?.length ?? 0
    } while (offset < total && offset > 0)
    return facts
  }

  async function loadChallengeTeamScoring(): Promise<void> {
    const generation = ++scoringLoadGeneration
    scoringContextLoading.value = true
    scoringContextError.value = null
    try {
      const [, facts, leaderboardResult, schemaResult] = await Promise.all([
        scoringPagination.loadPage(scoringPagination.page.value),
        loadAllChallengeFacts(),
        getLeaderboardEndpoint({ path: { competitionId } }),
        getScoreboardSchemaEndpoint({ path: { competitionId } }),
      ])
      if (generation !== scoringLoadGeneration) return
      scoringFacts.value = facts
      scoringSnapshot.value = leaderboardResult.data && 'teams' in leaderboardResult.data
        ? leaderboardResult.data
        : null
      scoringSchema.value = schemaResult.data && 'columns' in schemaResult.data
        ? schemaResult.data
        : null
    }
    catch (requestError) {
      if (generation === scoringLoadGeneration)
        scoringContextError.value = parseApiError(requestError, translate("ui.failedToLoadTeamScoringForThisChallenge")).message
    }
    finally {
      if (generation === scoringLoadGeneration) scoringContextLoading.value = false
    }
  }

  let scoringSearchTimer: ReturnType<typeof setTimeout> | null = null
  function reloadScoringTeamsFromFirstPage(): void {
    scoringPagination.reset()
    if (scoringSearchTimer) clearTimeout(scoringSearchTimer)
    scoringSearchTimer = setTimeout(() => { void scoringPagination.loadPage(1) }, 250)
  }

  watch(scoringSearch, reloadScoringTeamsFromFirstPage)

  function teamFacts(teamId?: string): NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse[] {
    return teamId ? scoringFacts.value.filter(fact => fact.teamId === teamId) : []
  }

  function hasSuccessfulFact(
    facts: NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse[],
    kind: NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse['kind'],
  ): boolean {
    return facts.some(fact => fact.kind === kind
      && (fact.result === 'Correct' || fact.result === 'Controlled'))
  }

  function progressForTeam(teamId?: string): Pick<ChallengeTeamScoringRow, 'progressLabel' | 'progressVariant'> {
    const facts = teamFacts(teamId)
    switch (competition.value?.mode) {
      case 'Awdp': {
        const attack = hasSuccessfulFact(facts, 'BreakAttempt')
        const defense = hasSuccessfulFact(facts, 'FixAttempt')
        if (attack && defense) return { progressLabel: translate("ui.attackAndDefenseSucceeded2"), progressVariant: 'default' }
        if (attack) return { progressLabel: translate("ui.attackSucceeded"), progressVariant: 'secondary' }
        if (defense) return { progressLabel: translate("ui.defenseSucceeded"), progressVariant: 'secondary' }
        return { progressLabel: translate("ui.noSuccessfulOperationYet"), progressVariant: 'outline' }
      }
      case 'Awd':
        return hasSuccessfulFact(facts, 'FlagAttempt')
          ? { progressLabel: translate("ui.attackSucceeded"), progressVariant: 'default' }
          : { progressLabel: translate("ui.noSuccessfulAttackYet"), progressVariant: 'outline' }
      case 'Koh':
        return hasSuccessfulFact(facts, 'KohControlObservation')
          ? { progressLabel: translate("ui.controlAcquired2"), progressVariant: 'default' }
          : { progressLabel: translate("ui.controlNotAcquired2"), progressVariant: 'outline' }
      default:
        return hasSuccessfulFact(facts, 'FlagAttempt')
          ? { progressLabel: translate("ui.solved"), progressVariant: 'default' }
          : { progressLabel: translate("ui.notSolved"), progressVariant: 'outline' }
    }
  }

  function manualAdjustmentForTeam(teamId?: string): number {
    return teamFacts(teamId)
      .filter(fact => fact.kind === 'ManualAdjustment' && fact.result === 'Applied')
      .reduce((total, fact) => total + (Number.parseInt(fact.value ?? '0', 10) || 0), 0)
  }

  function projectedChallengeScore(teamId?: string): number {
    if (!teamId) return 0
    const snapshotTeam = scoringSnapshot.value?.teams?.find(team => team.teamId === teamId)
    if (!snapshotTeam) return manualAdjustmentForTeam(teamId)
    const adjustment = manualAdjustmentForTeam(teamId)
    if (competition.value?.mode === 'Awdp') {
      const score = snapshotTeam.challengeScores?.find(item => item.competitionChallengeId === ccId)
      return (score?.attackScore ?? 0) + (score?.defenseScore ?? 0) + adjustment
    }
    const indexes = new Set(scoringSchema.value?.columns
      ?.filter(column => column.competitionChallengeId === ccId)
      .map(column => column.index)
      .filter((index): index is number => index !== undefined) ?? [])
    const slotScore = snapshotTeam.slots
      ?.filter(slot => slot.columnIndex !== undefined && indexes.has(slot.columnIndex))
      .reduce((total, slot) => total + (slot.netPoints ?? 0), 0) ?? 0
    return slotScore + adjustment
  }

  const scoringDisplayNames = computed(() => buildTeamDisplayNames(scoringTeams.value))

  const scoringRows = computed<ChallengeTeamScoringRow[]>(() => scoringTeams.value
    .map((team) => {
      const adjustment = manualAdjustmentForTeam(team.id)
      return {
        team,
        score: projectedChallengeScore(team.id),
        adjustment,
        ...progressForTeam(team.id),
      }
    })
    .sort((left, right) => teamDisplayName(left.team, scoringDisplayNames.value)
      .localeCompare(teamDisplayName(right.team, scoringDisplayNames.value))))

  const adjustmentTarget = ref<ChallengeTeamScoringRow | null>(null)

  const adjustmentDelta = ref(0)

  const adjustmentPending = ref(false)

  const adjustmentError = ref<string | null>(null)

  const adjustmentValid = computed(() => Number.isInteger(adjustmentDelta.value) && adjustmentDelta.value !== 0)

  function openAdjustment(row: ChallengeTeamScoringRow): void {
    adjustmentTarget.value = row
    adjustmentDelta.value = 0
    adjustmentError.value = null
  }

  function closeAdjustment(open: boolean): void {
    if (!open && !adjustmentPending.value) adjustmentTarget.value = null
  }

  async function submitAdjustment(): Promise<void> {
    const teamId = adjustmentTarget.value?.team.id
    if (!teamId || !adjustmentValid.value || adjustmentPending.value) return
    adjustmentPending.value = true
    adjustmentError.value = null
    try {
      const { error } = await adminCreateManualAdjustment({
        path: { competitionId },
        body: { teamId, competitionChallengeId: ccId, delta: adjustmentDelta.value },
      })
      if (error) throw error
      toast.success(translate("ui.challengeScoringCorrected"))
      adjustmentTarget.value = null
      await loadChallengeTeamScoring()
    }
    catch (requestError) {
      adjustmentError.value = parseApiError(requestError, translate("ui.failedToCorrectChallengeScoring")).message
    }
    finally {
      adjustmentPending.value = false
    }
  }

  onMounted(() => {
    void loadChallenge()
    void loadConfig()
    void loadHints()
    void loadChallengeTeamScoring()
  })

  onBeforeUnmount(() => {
    if (scoringSearchTimer) clearTimeout(scoringSearchTimer)
    scoringPagination.reset()
  })

  const ChallengeRulesEditor = markRaw(ChallengeRulesEditorComponent)

  const viewBindings = {
      adminTeamPath, adminTemplatePath,
      Plus,
      competitionId,
      competition,
      canWrite,
      canJudge,
      challenge,
      loading,
      loadError,
      activeSection,
      sectionOptions,
      editCustomTitle,
      editOrder,
      editPublished,
      savingEdit,
      saveEdit,
      config,
      configLoading,
      savingConfig,
      inheritedConfiguration,
      saveConfig,
      hints,
      hintsLoading,
      hintsLoadError,
      includeDeletedHints,
      hintDialogOpen,
      editingHint,
      hintForm,
      hintError,
      savingHint,
      pendingHintId,
      openHintDialog,
      saveHint,
      deleteHint,
      restoreHint,
      scoringLoading,
      scoringError,
      scoringSearch,
      loadChallengeTeamScoring,
      scoringPage: scoringPagination.page,
      scoringPageCount: scoringPagination.pageCount,
      scoringTotal: scoringPagination.total,
      scoringPageLimit: scoringPagination.limit,
      scoringPageLoading: scoringPagination.loading,
      loadScoringPage: scoringPagination.loadPage,
      setScoringPageSize: scoringPagination.setPageSize,
      scoringDisplayNames,
      scoringRows,
      adjustmentTarget,
      adjustmentDelta,
      adjustmentPending,
      adjustmentError,
      adjustmentValid,
      openAdjustment,
      closeAdjustment,
      submitAdjustment,
      ChallengeRulesEditor,
      hiddenRuleKeys
    }
  const viewState = proxyRefs(viewBindings)

  function onClickAdjustmentTarget(value: typeof viewState.adjustmentTarget) {
    viewState.adjustmentTarget = value
  }

  function onClickHintDialogOpen(value: typeof viewState.hintDialogOpen) {
    viewState.hintDialogOpen = value
  }

  return { ...viewBindings, onClickAdjustmentTarget, onClickHintDialogOpen }
}

export type AdminCompetitionsByIdChallengesByCcIdPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdChallengesByCcIdPage>>>
