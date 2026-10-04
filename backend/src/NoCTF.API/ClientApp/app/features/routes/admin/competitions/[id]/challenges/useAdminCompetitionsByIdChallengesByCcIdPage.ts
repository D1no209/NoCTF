import { ProjectionResponseOption } from '../../../../../../lib/api'
import { createNoCTFAPIEndpointsCompetitionsScoreboardSnapshotResponseFromDiscriminatorValue, createNoCTFAPIEndpointsCompetitionsScoreboardSchemaResponseFromDiscriminatorValue } from '../../../../../../api/models'
import { dateObject } from '~/utils/date-value'

import { api, RequestPolicyOption } from '../../../../../../lib/api'
import { message as describeMessage } from '../../../../../../utils/i18n'
import type { UiMessage } from '../../../../../../utils/i18n'
import { challengeTagOptions, uniqueTags, validChallengeTags } from '~/lib/challenge-tags'
import { adminTeamPath, adminTemplatePath } from '~/features/admin/admin-navigation'
import { proxyRefs } from 'vue'
import { markRaw } from 'vue'

import { Plus } from '@lucide/vue'
import { toast } from '../../../../../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationChallengesChallengeHintResponse, NoCTFAPIEndpointsAdministrationChallengesCompetitionChallengeRulesContract, NoCTFAPIEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract, NoCTFAPIEndpointsChallengesChallengeResponse, NoCTFAPIEndpointsCompetitionsGameModeProtocol, NoCTFAPIEndpointsCompetitionsScoreboardSchemaResponse, NoCTFAPIEndpointsCompetitionsScoreboardSnapshotResponse, NoCTFAPIEndpointsGameplayFactsGameplayFactListItemResponse, NoCTFAPIEndpointsTeamsTeamResponse } from '../../../../../../api/models'
import { useOffsetPagination } from '../../../../../../composables/useOffsetPagination'
import { useCompetitionAdmin } from '../../../../../../lib/admin-competition'

import ChallengeRulesEditorComponent from '../../../../../admin/ChallengeRulesEditor.vue'

interface ChallengeTeamScoringRow {
  team: NoCTFAPIEndpointsTeamsTeamResponse
  score: number
  adjustment: number
  progressLabel: string
  progressVariant: 'default' | 'secondary' | 'outline'
}

interface ChallengeConfiguration {
  mode?: NoCTFAPIEndpointsCompetitionsGameModeProtocol | null
  rules?: NoCTFAPIEndpointsAdministrationChallengesCompetitionChallengeRulesContract | null
}

/** Owns state, effects and commands for AdminCompetitionsByIdChallengesByCcIdPage. */
export function useAdminCompetitionsByIdChallengesByCcIdPage() {
  const route = useRoute()

  const ccId = route.params.ccId as string

  const { competitionId, competition, canWrite, canJudge } = useCompetitionAdmin()

  const challenge = ref<NoCTFAPIEndpointsChallengesChallengeResponse | null>(null)

  const hiddenRuleKeys = computed(() => {
    if (challenge.value?.interactionKind === 'PatchVerification')
      return ['maxFlagAttempts', 'flagTemplate']
    const hidden = ['maxPatchAttempts']
    if (challenge.value?.usesDynamicFlag !== true) hidden.push('flagTemplate')
    return hidden
  })

  const loading = ref(true)

  const loadError = ref<UiMessage | null>(null)

  const activeSection = ref('general')

  const sectionOptions = computed(() => [
    { value: 'general', label: translate('administration.label.basicSettings') },
    { value: 'config', label: translate('administration.label.questionConfiguration') },
    { value: 'hints', label: translate('administration.label.hint') },
    { value: 'scoring', label: translate('administration.label.teamScoring') },
  ])

  async function loadChallenge() {
    loading.value = true
    let error: unknown;
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.byCompetitionChallengeId(ccId).get({ queryParameters: { includeDeleted: false } }).catch(cause => { error = cause; return undefined });
    if (error) loadError.value = parseApiError(error).displayMessage
    else challenge.value = data?.challenge ?? null
    loading.value = false
  }

  const directions = ref<import('~/api/models').NoCTFAPIEndpointsAdministrationCompetitionsCompetitionDirectionResponse[]>([])
  const directionLoading = ref(true)
  const directionError = ref<UiMessage | null>(null)
  const editDirectionId = ref('')
  const directionRequest = new AbortController()
  async function loadDirections() {
    let error: unknown;
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).directions.get({ options: [new RequestPolicyOption({ signal: directionRequest.signal })] }).catch(cause => { error = cause; return undefined });
    if (directionRequest.signal.aborted) return
    directionLoading.value = false
    directionError.value = error || !data ? parseApiError(error).displayMessage : null
    directions.value = data?.items ?? []
  }
  const editCustomTitle = ref('')
  const editTags = ref<string[]>([])
  const tagSuggestions = ref<string[]>([])
  const tagOptions = computed(() => uniqueTags([...tagSuggestions.value, ...(challenge.value?.tags ?? [])]))
  function updateEditTags(tags: string[]) {
    if (!validChallengeTags(tags)) { toast.error(describeMessage('challengeTags.invalid')); return }
    editTags.value = uniqueTags(tags)
  }
  async function loadTagSuggestions() {
    let error: unknown;
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.get({ queryParameters: { includeDeleted: false }, options: [new RequestPolicyOption({ signal: directionRequest.signal })] }).catch(cause => { error = cause; return undefined });
    if (directionRequest.signal.aborted) return
    if (error) toast.error(parseApiError(error).displayMessage)
    else tagSuggestions.value = challengeTagOptions(data?.items ?? [])
  }

  const editOrder = ref(0)

  const editPublished = ref(false)

  const savingEdit = ref(false)

  watch(challenge, (c) => {
    if (!c) return
    editDirectionId.value = c.directionId ?? ''
    editCustomTitle.value = c.customTitle ?? ''
    editTags.value = [...(c.tags ?? [])]
    editOrder.value = c.order ?? 0
    editPublished.value = c.isPublished ?? false
  }, { immediate: true })

  async function saveEdit() {
    if (!challenge.value) return
    savingEdit.value = true
    try {

      const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.byCompetitionChallengeId(ccId).patch({ presentation: {
          customTitle: editCustomTitle.value.trim() || null,
          order: editOrder.value,
          isPublished: editPublished.value,
          directionId: editDirectionId.value || undefined,
          tags: editTags.value,
        } });
      challenge.value = data?.challenge ?? challenge.value
      void loadTagSuggestions()
      toast.success(describeMessage("administration.label.questionSettingsSaved"))
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

  const inheritedConfiguration = ref<NoCTFAPIEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract | null>(null)

  async function loadConfig() {
    configLoading.value = true
    const settledRequests = await Promise.allSettled([
      api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.byCompetitionChallengeId(ccId).get({ queryParameters: { includeDeleted: false } }),
      api.api.v1.admin.competitions.byCompetitionId(competitionId).get(),
    ]);
    const challengeResult = settledRequests[0].status === 'fulfilled' ? settledRequests[0].value : undefined;
    const challengeResultError = settledRequests[0].status === 'rejected' ? settledRequests[0].reason : undefined;
    const competitionResult = settledRequests[1].status === 'fulfilled' ? settledRequests[1].value : undefined;
    const competitionResultError = settledRequests[1].status === 'rejected' ? settledRequests[1].reason : undefined;

    if (!challengeResultError && challengeResult) {
      config.value = {
        mode: challengeResult.mode,
        rules: challengeResult.rules,
      }
    }
    if (!competitionResultError && competitionResult) {
      inheritedConfiguration.value = competitionResult.modeConfiguration?.configuration ?? null
    }
    configLoading.value = false
  }

  async function saveConfig(rules: NoCTFAPIEndpointsAdministrationChallengesCompetitionChallengeRulesContract) {
    if (!config.value) return
    savingConfig.value = true
    try {
      const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.byCompetitionChallengeId(ccId).patch({ rules: { configuration: rules } });
      config.value = data
        ? { mode: data.mode, rules: data.rules }
        : config.value
      toast.success(describeMessage("administration.competitionsBy.label.questionConfigurationSaved"))
    }
    catch (e) {
      toastWriteError(e)
    }
    finally {
      savingConfig.value = false
    }
  }

  const hints = ref<NoCTFAPIEndpointsAdministrationChallengesChallengeHintResponse[]>([])

  const hintsLoading = ref(true)

  const hintsLoadError = ref<UiMessage | null>(null)

  const includeDeletedHints = ref(false)

  const hintDialogOpen = ref(false)

  const editingHint = ref<NoCTFAPIEndpointsAdministrationChallengesChallengeHintResponse | null>(null)

  const hintForm = ref({ content: '', cost: 0, publishedAt: '' })

  const hintError = ref<UiMessage | null>(null)

  const savingHint = ref(false)

  const pendingHintId = ref<string | null>(null)

  async function loadHints() {
    hintsLoading.value = true
    let error: unknown;
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.byCompetitionChallengeId(ccId).hints.get({ queryParameters: { includeDeleted: includeDeletedHints.value } }).catch(cause => { error = cause; return undefined });
    if (error || !data) {
      hintsLoadError.value = parseApiError(error).displayMessage
    }
    else {
      hintsLoadError.value = null
      hints.value = data.items ?? []
    }
    hintsLoading.value = false
  }

  watch(includeDeletedHints, loadHints)

  function openHintDialog(hint?: NoCTFAPIEndpointsAdministrationChallengesChallengeHintResponse | null) {
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
      hintError.value = describeMessage("administration.competitionsBy.label.enterPromptContent")
      return
    }
    savingHint.value = true
    hintError.value = null
    const body = {
      content: hintForm.value.content,
      cost: hintForm.value.cost,
      publishedAt: dateObject(localInputToIso(hintForm.value.publishedAt) ?? null),
    }
    try {
      const path = { competitionId, competitionChallengeId: ccId }

      await (editingHint.value?.id
        ? api.api.v1.admin.competitions.byCompetitionId(path.competitionId).challenges.byCompetitionChallengeId(path.competitionChallengeId).hints.byHintId(editingHint.value.id).put(body)
        : api.api.v1.admin.competitions.byCompetitionId(path.competitionId).challenges.byCompetitionChallengeId(path.competitionChallengeId).hints.post(body));
      toast.success(editingHint.value ? translate("administration.label.tipUpdated") : translate("administration.label.promptAdded"))
      hintDialogOpen.value = false
      await loadHints()
    }
    catch (e) {
      hintError.value = parseApiError(e).displayMessage
    }
    finally {
      savingHint.value = false
    }
  }

  async function deleteHint(h: NoCTFAPIEndpointsAdministrationChallengesChallengeHintResponse) {
    if (!h.id) return
    pendingHintId.value = h.id
    try {
      await api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.byCompetitionChallengeId(ccId).hints.byHintId(h.id).delete();
      toast.success(describeMessage("administration.label.tipDeleted"))
      await loadHints()
    }
    catch (e) {
      toast.error(parseApiError(e).displayMessage)
    }
    finally {
      pendingHintId.value = null
    }
  }

  async function restoreHint(h: NoCTFAPIEndpointsAdministrationChallengesChallengeHintResponse) {
    if (!h.id) return
    pendingHintId.value = h.id
    try {
      await api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.byCompetitionChallengeId(ccId).hints.byHintId(h.id).restore.post();
      toast.success(describeMessage("administration.label.tipRestored"))
      await loadHints()
    }
    catch (e) {
      toast.error(parseApiError(e).displayMessage)
    }
    finally {
      pendingHintId.value = null
    }
  }

  const scoringSearch = ref('')

  const scoringPagination = useOffsetPagination<NoCTFAPIEndpointsTeamsTeamResponse>(async ({ offset, limit, desc }) => {
    let error: unknown;
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).teams.get({ queryParameters: { keyword: scoringSearch.value.trim() || undefined, offset, limit, desc } }).catch(cause => { error = cause; return undefined });
    if (error || !data) throw error ?? new Error(translate("administration.error.loadTeamsFailed"))
    return { items: data.items ?? [], total: data.total ?? 0 }
  })

  const scoringTeams = scoringPagination.items

  const scoringFacts = ref<NoCTFAPIEndpointsGameplayFactsGameplayFactListItemResponse[]>([])

  const scoringSnapshot = ref<NoCTFAPIEndpointsCompetitionsScoreboardSnapshotResponse | null>(null)

  const scoringSchema = ref<NoCTFAPIEndpointsCompetitionsScoreboardSchemaResponse | null>(null)

  const scoringContextLoading = ref(true)

  const scoringContextError = ref<UiMessage | null>(null)

  const scoringLoading = computed(() => scoringContextLoading.value
    || (scoringPagination.loading.value && !scoringPagination.initialized.value))

  const scoringError = computed(() => scoringContextError.value
    ?? scoringPagination.error.value?.message
    ?? null)

  let scoringLoadGeneration = 0

  async function loadAllChallengeFacts(): Promise<NoCTFAPIEndpointsGameplayFactsGameplayFactListItemResponse[]> {
    const facts: NoCTFAPIEndpointsGameplayFactsGameplayFactListItemResponse[] = []
    let offset = 0
    let total = 0
    do {
      let responseError: unknown;
      const response = await api.api.v1.admin.competitions.byCompetitionId(competitionId).gameplayFacts.get({ queryParameters: { competitionChallengeId: ccId, offset, limit: 200, desc: true } }).catch(cause => { responseError = cause; return undefined });
      if (responseError || !response)
        throw responseError ?? new Error(translate("administration.competitionsBy.error.loadChallengeSFailed"))
      facts.push(...(response.items ?? []))
      total = response.total ?? facts.length
      offset += response.items?.length ?? 0
    } while (offset < total && offset > 0)
    return facts
  }

  async function loadChallengeTeamScoring(): Promise<void> {
    const generation = ++scoringLoadGeneration
    scoringContextLoading.value = true
    scoringContextError.value = null
    try {
      const settledRequests = await Promise.allSettled([
        scoringPagination.loadPage(scoringPagination.page.value),
        loadAllChallengeFacts(),
        api.api.v1.competitions.byCompetitionId(competitionId).leaderboard.get({ options: [new ProjectionResponseOption(createNoCTFAPIEndpointsCompetitionsScoreboardSnapshotResponseFromDiscriminatorValue)] }),
        api.api.v1.competitions.byCompetitionId(competitionId).leaderboard.schema.get({ options: [new ProjectionResponseOption(createNoCTFAPIEndpointsCompetitionsScoreboardSchemaResponseFromDiscriminatorValue)] }),
      ]);
      const facts = settledRequests[1].status === 'fulfilled' ? settledRequests[1].value : undefined;
      const leaderboardResult = settledRequests[2].status === 'fulfilled' ? settledRequests[2].value : undefined;
      const schemaResult = settledRequests[3].status === 'fulfilled' ? settledRequests[3].value : undefined;

      if (generation !== scoringLoadGeneration) return
      const failed = settledRequests.find((result): result is PromiseRejectedResult => result.status === 'rejected')
      if (failed) throw failed.reason
      scoringFacts.value = facts ?? []
      scoringSnapshot.value = leaderboardResult && 'teams' in leaderboardResult
        ? leaderboardResult
        : null
      scoringSchema.value = schemaResult && 'columns' in schemaResult
        ? schemaResult
        : null
    }
    catch (requestError) {
      if (generation === scoringLoadGeneration)
        scoringContextError.value = parseApiError(requestError, describeMessage("administration.competitionsBy.error.loadTeamScoringFailed")).displayMessage
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

  function teamFacts(teamId?: string | null): NoCTFAPIEndpointsGameplayFactsGameplayFactListItemResponse[] {
    return teamId ? scoringFacts.value.filter(fact => fact.teamId === teamId) : []
  }

  function hasSuccessfulFact(
    facts: NoCTFAPIEndpointsGameplayFactsGameplayFactListItemResponse[],
    kind: NoCTFAPIEndpointsGameplayFactsGameplayFactListItemResponse['kind'],
  ): boolean {
    return facts.some(fact => fact.kind === kind
      && (fact.result === 'Correct' || fact.result === 'Controlled'))
  }

  function progressForTeam(teamId?: string | null): Pick<ChallengeTeamScoringRow, 'progressLabel' | 'progressVariant'> {
    const facts = teamFacts(teamId)
    switch (competition.value?.mode) {
      case 'Awdp': {
        const attack = hasSuccessfulFact(facts, 'BreakAttempt')
        const defense = hasSuccessfulFact(facts, 'FixAttempt')
        if (attack && defense) return { progressLabel: translate("administration.label.attackDefenseSucceeded"), progressVariant: 'default' }
        if (attack) return { progressLabel: translate("common.label.attackSucceeded"), progressVariant: 'secondary' }
        if (defense) return { progressLabel: translate("common.label.defenseSucceeded"), progressVariant: 'secondary' }
        return { progressLabel: translate("administration.label.successfulYet"), progressVariant: 'outline' }
      }
      case 'Awd':
        return hasSuccessfulFact(facts, 'FlagAttempt')
          ? { progressLabel: translate("common.label.attackSucceeded"), progressVariant: 'default' }
          : { progressLabel: translate("administration.label.successfulAttackYet"), progressVariant: 'outline' }
      case 'Koh':
        return hasSuccessfulFact(facts, 'KohControlObservation')
          ? { progressLabel: translate("administration.label.controlAcquired"), progressVariant: 'default' }
          : { progressLabel: translate("administration.label.controlAcquired.ccIdPage"), progressVariant: 'outline' }
      default:
        return hasSuccessfulFact(facts, 'FlagAttempt')
          ? { progressLabel: translate("common.label.solved.competitionChallengeNavigator"), progressVariant: 'default' }
          : { progressLabel: translate("common.label.solved"), progressVariant: 'outline' }
    }
  }

  function manualAdjustmentForTeam(teamId?: string | null): number {
    return teamFacts(teamId)
      .filter(fact => fact.kind === 'ManualAdjustment' && fact.result === 'Applied')
      .reduce((total, fact) => total + (Number.parseInt(fact.value ?? '0', 10) || 0), 0)
  }

  function projectedChallengeScore(teamId?: string | null): number {
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
      ?.filter(slot => slot.columnIndex != null && indexes.has(slot.columnIndex))
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

  const adjustmentError = ref<UiMessage | null>(null)

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

      await api.api.v1.admin.competitions.byCompetitionId(competitionId).gameplayFacts.manualAdjustments.post({ teamId, competitionChallengeId: ccId, delta: adjustmentDelta.value });
      toast.success(describeMessage("administration.label.challengeScoringCorrected"))
      adjustmentTarget.value = null
      await loadChallengeTeamScoring()
    }
    catch (requestError) {
      adjustmentError.value = parseApiError(requestError, describeMessage("administration.competitionsBy.error.correctChallengeScoringFailed")).displayMessage
    }
    finally {
      adjustmentPending.value = false
    }
  }

  onMounted(() => {
    void loadDirections()
    void loadTagSuggestions()
    void loadChallenge()
    void loadConfig()
    void loadHints()
    void loadChallengeTeamScoring()
  })

  onBeforeUnmount(() => {
    directionRequest.abort()
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
      directions, directionLoading, directionError, editDirectionId, loadDirections,
      editCustomTitle,
      editTags, tagOptions, updateEditTags,
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
