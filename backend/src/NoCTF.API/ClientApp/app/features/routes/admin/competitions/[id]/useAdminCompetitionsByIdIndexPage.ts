import { proxyRefs } from 'vue'

import { toast } from 'vue-sonner'
import { adminCompetitionPosterClear, adminCompetitionPosterReplace, adminDeleteCompetition, adminForceDeleteCompetition, adminGenerateMissingFlags, adminHardDeleteCompetition, adminPreviewCompetitionHardDelete, adminRestoreCompetition, adminUpdateCompetitionStatus, adminValidateCompetitionStart } from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionHardDeletePreviewResponse, NoCtfapiEndpointsAdministrationCompetitionsCompetitionHardDeleteReferenceCode, NoCtfapiEndpointsAdministrationCompetitionsStartGateErrorResponse } from '../../../../../api'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import { startGateErrorMessage } from '../../../../../lib/start-gate-error'
import { useCompetitionPoster } from '../../../../competitions/useCompetitionPoster'

interface LifecycleAction {
  key: string
  label: string
  visible: boolean
  destructive?: boolean
  confirm?: { title: string; description: string }
  run: () => Promise<{ error?: unknown }>
}

/** Owns state, effects and commands for AdminCompetitionsByIdIndexPage. */
export function useAdminCompetitionsByIdIndexPage() {
  const { competitionId, competition, canWrite, canManagePermissions, refresh } = useCompetitionAdmin()

  const { isAdministrator } = useAuth()

  const pendingAction = ref<string | null>(null)

  const actionError = ref<string | null>(null)

  const status = computed(() => competition.value?.status)

  const isDeleted = computed(() => !!competition.value?.deletedAt)

  const {
    posterUrl,
    posterLoading,
    posterError,
    refreshPoster,
    clearPoster,
  } = useCompetitionPoster(competitionId)

  const posterPending = ref(false)

  const posterInputKey = ref(0)

  const posterSelectionError = ref<string | null>(null)

  const posterRemoveOpen = ref(false)

  function posterUploadError(error: unknown): string {
    const parsed = parseApiError(error)
    return parsed.code === 'UploadTooLarge'
      ? translate('ui.theUploadedFileIsTooLarge')
      : ['SizeInvalid', 'SourceMetadataMismatch', 'UnsupportedFormat', 'InvalidDimensions', 'PixelLimitExceeded', 'MultipleFrames', 'MalformedImage'].includes(parsed.code ?? '')
        ? translate('ui.posterMustBeAJpegPngOrWebpImage')
        : parsed.message
  }

  async function selectPoster(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement
    const file = input.files?.[0] ?? null
    posterSelectionError.value = null
    if (!file || posterPending.value || !canWrite.value || isDeleted.value) return
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
      posterSelectionError.value = translate('ui.posterMustBeAJpegPngOrWebpImage')
      return
    }

    posterPending.value = true
    try {
      const { error } = await adminCompetitionPosterReplace({
        path: { competitionId },
        body: { file },
      })
      if (error) throw error
      await refreshPoster()
      await loadHardDeletePreview()
      toast.success(translate('ui.competitionPosterUpdated'))
      posterInputKey.value += 1
    }
    catch (error) {
      toast.error(posterUploadError(error))
    }
    finally {
      posterPending.value = false
    }
  }

  async function removePoster(): Promise<void> {
    if (posterPending.value || !canWrite.value || isDeleted.value) return
    posterPending.value = true
    try {
      const { error } = await adminCompetitionPosterClear({ path: { competitionId } })
      if (error) throw error
      clearPoster()
      posterRemoveOpen.value = false
      posterInputKey.value += 1
      await loadHardDeletePreview()
      toast.success(translate('ui.competitionPosterRemoved'))
    }
    catch (error) {
      toast.error(parseApiError(error).message)
    }
    finally {
      posterPending.value = false
    }
  }

  function setPosterRemoveOpen(open: boolean): void {
    if (!posterPending.value) posterRemoveOpen.value = open
  }

  onMounted(refreshPoster)

  const steps = [
    { value: 'Draft', label: "ui.draft" },
    { value: 'Visible', label: "ui.visible" },
    { value: 'Published', label: "ui.published" },
    { value: 'Running', label: "ui.running" },
    { value: 'Finished', label: "ui.finished" },
  ]

  const updateStatus = (target: 'Visible' | 'Published' | 'Running' | 'Paused' | 'Finished') =>
    adminUpdateCompetitionStatus({ path: { competitionId }, body: { status: target } })

  const actions = computed<LifecycleAction[]>(() => [
    {
      key: 'make-visible',
      label: translate("ui.visibleToTheOutsideWorld"),
      visible: status.value === 'Draft',
      run: () => updateStatus('Visible'),
    },
    {
      key: 'publish',
      label: translate("ui.postAContest"),
      visible: status.value === 'Visible',
      run: () => updateStatus('Published'),
    },
    {
      key: 'start',
      label: translate("ui.startTheGame"),
      visible: status.value === 'Published',
      confirm: { title: translate("ui.startTheGame"), description: translate("ui.theMatchWillStartImmediatelyAndARuntimeInstanceWill") },
      run: () => updateStatus('Running'),
    },
    {
      key: 'pause',
      label: translate("ui.pauseTheGame"),
      visible: status.value === 'Running',
      run: () => updateStatus('Paused'),
    },
    {
      key: 'resume',
      label: translate("ui.resumePlay"),
      visible: status.value === 'Paused',
      run: () => updateStatus('Running'),
    },
    {
      key: 'finish',
      label: translate("ui.endGame"),
      visible: status.value === 'Published' || status.value === 'Running' || status.value === 'Paused',
      destructive: true,
      confirm: { title: translate("ui.endGame"), description: translate("ui.endingTheMatchIsIrreversibleAndWillCleanUpAll") },
      run: () => updateStatus('Finished'),
    },
  ])

  const confirmTarget = ref<LifecycleAction | null>(null)

  async function execute(action: LifecycleAction) {
    pendingAction.value = action.key
    actionError.value = null
    try {
      const { error } = await action.run()
      if (error) throw error
      toast.success(translate("ui.succeeded", { action: action.label }))
      await refresh()
    }
    catch (e) {
      actionError.value = parseApiError(e).message
    }
    finally {
      pendingAction.value = null
      confirmTarget.value = null
    }
  }

  function trigger(action: LifecycleAction) {
    if (action.confirm) {
      confirmTarget.value = action
      return
    }
    void execute(action)
  }

  const validating = ref(false)

  const validationErrors = ref<NoCtfapiEndpointsAdministrationCompetitionsStartGateErrorResponse[] | null>(null)

  async function validateStart() {
    validating.value = true
    validationErrors.value = null
    try {
      const { data, error } = await adminValidateCompetitionStart({ path: { competitionId } })
      if (error) throw error
      validationErrors.value = data?.errors ?? []
      if (validationErrors.value.length === 0) toast.success(translate("ui.preStartCheckPassed"))
    }
    catch (e) {
      toast.error(parseApiError(e).message)
    }
    finally {
      validating.value = false
    }
  }

  const generating = ref(false)

  const generateFailures = ref<{ competitionChallengeId?: string; teamId?: string; code?: string; description?: string }[]>([])

  async function generateMissingFlags() {
    generating.value = true
      generateFailures.value = []
    try {
      const { data, error } = await adminGenerateMissingFlags({ path: { competitionId } })
      if (error) throw error
      generateFailures.value = data?.failures ?? []
      if (generateFailures.value.length === 0) toast.success(translate("ui.missingFlagAllGenerated"))
      else toast.warning(translate("ui.generationCompletedWithFailures", { count: generateFailures.value.length }))
    }
    catch (e) {
      toast.error(parseApiError(e).message)
    }
    finally {
      generating.value = false
    }
  }

  const deleting = ref(false)

  const restoring = ref(false)

  const hardDeleting = ref(false)

  const forceDeleting = ref(false)

  const deleteConfirm = ref<'soft' | 'hard' | null>(null)

  const forceDeleteOpen = ref(false)

  const forceDeleteTitle = ref('')

  const forceDeleteReason = ref('')

  const forceDeleteError = ref<string | null>(null)

  const forceDeleteConflictingIds = ref<string[]>([])

  const hardDeletePreview = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionHardDeletePreviewResponse | null>(null)

  const hardDeletePreviewLoading = ref(false)

  const hardDeletePreviewError = ref<string | null>(null)

  let hardDeletePreviewRequest = 0

  const hardDeleteReferenceLabels: Record<NoCtfapiEndpointsAdministrationCompetitionsCompetitionHardDeleteReferenceCode, string> = {
    HistoricalEvent: "ui.permanentCompetitionEvents",
    Team: "ui.team",
    CompetitionChallenge: "ui.competitionChallenges",
    GameplayFact: "ui.gameplayFacts",
    RuntimeInstance: "ui.runtimeEnvironment",
    PatchUpload: "ui.patchUpload2",
    Notification: "ui.notificationsAndQuestions",
    PosterFile: "ui.competitionPoster",
    ActiveRuntimeResource: "ui.message9",
    NotificationScopeConflict: "ui.crossScopeOrUnprovenNotificationReferences",
  }

  function hardDeleteReferenceLabel(code?: NoCtfapiEndpointsAdministrationCompetitionsCompetitionHardDeleteReferenceCode) {
    return code ? translate(hardDeleteReferenceLabels[code]) : translate("ui.unknownReference")
  }

  function isHardDeletePreview(value: unknown): value is NoCtfapiEndpointsAdministrationCompetitionsCompetitionHardDeletePreviewResponse {
    return !!value && typeof value === 'object' && 'canHardDelete' in value && 'references' in value
  }

  async function loadHardDeletePreview() {
    const request = ++hardDeletePreviewRequest
    if (!canManagePermissions.value || !competition.value) {
      hardDeletePreview.value = null
      hardDeletePreviewError.value = null
      hardDeletePreviewLoading.value = false
      return
    }
    hardDeletePreviewLoading.value = true
    hardDeletePreviewError.value = null
    try {
      const { data, error } = await adminPreviewCompetitionHardDelete({ path: { competitionId } })
      if (error) throw error
      if (request === hardDeletePreviewRequest) hardDeletePreview.value = data ?? null
    }
    catch (error) {
      if (request === hardDeletePreviewRequest) {
        hardDeletePreviewError.value = parseApiError(error).message
      }
    }
    finally {
      if (request === hardDeletePreviewRequest) hardDeletePreviewLoading.value = false
    }
  }

  watch(
    [() => competition.value?.id, canManagePermissions],
    () => { void loadHardDeletePreview() },
    { immediate: true },
  )

  async function softDelete() {
    deleting.value = true
    try {
      const { error } = await adminDeleteCompetition({ path: { competitionId } })
      if (error) throw error
      toast.success(translate("ui.contestDeleted"))
      await refresh()
      await loadHardDeletePreview()
    }
    catch (e) {
      toast.error(parseApiError(e).message)
    }
    finally {
      deleting.value = false
      deleteConfirm.value = null
    }
  }

  async function restore() {
    restoring.value = true
    try {
      const { error } = await adminRestoreCompetition({ path: { competitionId } })
      if (error) throw error
      toast.success(translate("ui.competitionHasResumed"))
      await refresh()
      await loadHardDeletePreview()
    }
    catch (e) {
      toast.error(parseApiError(e).message)
    }
    finally {
      restoring.value = false
    }
  }

  async function hardDelete() {
    hardDeleting.value = true
    try {
      const { error } = await adminHardDeleteCompetition({ path: { competitionId } })
      if (error) throw error
      toast.success(translate("ui.contestHasBeenCompletelyDeleted"))
      await navigateTo('/competitions')
    }
    catch (e) {
      if (isHardDeletePreview(e)) {
        hardDeletePreview.value = e
        toast.error(translate("ui.theCompetitionStillHasPermanentHistoryOrBusinessReferencesAnd"))
      }
      else {
        toast.error(parseApiError(e).message)
      }
    }
    finally {
      hardDeleting.value = false
      deleteConfirm.value = null
    }
  }

  const forceDeleteValid = computed(() =>
    forceDeleteTitle.value === (competition.value?.title ?? '')
    && forceDeleteReason.value.trim().length >= 8
    && forceDeleteReason.value.trim().length <= 500,
  )

  function beginForceDelete(): void {
    forceDeleteTitle.value = ''
    forceDeleteReason.value = ''
    forceDeleteError.value = null
    forceDeleteOpen.value = true
  }

  async function forceDelete(): Promise<void> {
    if (!forceDeleteValid.value) return
    forceDeleting.value = true
    forceDeleteError.value = null
    forceDeleteConflictingIds.value = []
    try {
      const { error } = await adminForceDeleteCompetition({
        path: { competitionId },
        body: {
          confirmationTitle: forceDeleteTitle.value,
          reason: forceDeleteReason.value.trim(),
        },
      })
      if (error) {
        if (typeof error === 'object' && 'conflictingNotificationIds' in error
          && Array.isArray(error.conflictingNotificationIds)) {
          forceDeleteConflictingIds.value = error.conflictingNotificationIds.filter((id): id is string => typeof id === 'string')
        }
        throw error
      }
      forceDeleteOpen.value = false
      toast.success(translate("ui.theCompetitionAndItsScopedDataWerePermanentlyDeletedThe"))
      await navigateTo('/competitions')
    }
    catch (error) {
      forceDeleteError.value = parseApiError(error).message
      await loadHardDeletePreview()
    }
    finally {
      forceDeleting.value = false
    }
  }

  async function submitDelete() {
    const action = deleteConfirm.value
    if (action === 'hard') await hardDelete()
    else if (action === 'soft') await softDelete()
  }

  const viewBindings = {
      startGateErrorMessage,
      competitionId,
      competition,
      canWrite,
      canManagePermissions,
      isAdministrator,
      pendingAction,
      actionError,
      status,
      isDeleted,
      posterUrl,
      posterLoading,
      posterError,
      posterPending,
      posterInputKey,
      posterSelectionError,
      posterRemoveOpen,
      refreshPoster,
      selectPoster,
      removePoster,
      setPosterRemoveOpen,
      steps,
      actions,
      confirmTarget,
      execute,
      trigger,
      validating,
      validationErrors,
      validateStart,
      generating,
      generateFailures,
      generateMissingFlags,
      deleting,
      restoring,
      hardDeleting,
      forceDeleting,
      deleteConfirm,
      forceDeleteOpen,
      forceDeleteTitle,
      forceDeleteReason,
      forceDeleteError,
      forceDeleteConflictingIds,
      hardDeletePreview,
      hardDeletePreviewLoading,
      hardDeletePreviewError,
      hardDeleteReferenceLabel,
      restore,
      forceDeleteValid,
      beginForceDelete,
      forceDelete,
      submitDelete
    }
  const viewState = proxyRefs(viewBindings)

  function onClickDeleteConfirm(value: typeof viewState.deleteConfirm) {
    viewState.deleteConfirm = value
  }

  function onClickDeleteConfirm2(value: typeof viewState.deleteConfirm) {
    viewState.deleteConfirm = value
  }

  function onUpdateOpenConfirmTarget(v: boolean) {
     if (!v) viewState.confirmTarget = null
  }

  function onClickForceDeleteOpen(value: typeof viewState.forceDeleteOpen) {
    viewState.forceDeleteOpen = value
  }

  function onUpdateOpenDeleteConfirm(v: boolean) {
     if (!v) viewState.deleteConfirm = null
  }

  return { ...viewBindings, onClickDeleteConfirm, onClickDeleteConfirm2, onUpdateOpenConfirmTarget, onClickForceDeleteOpen, onUpdateOpenDeleteConfirm }
}

export type AdminCompetitionsByIdIndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdIndexPage>>>
