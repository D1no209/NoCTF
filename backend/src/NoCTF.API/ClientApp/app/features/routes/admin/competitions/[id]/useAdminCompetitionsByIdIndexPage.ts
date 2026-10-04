
import { api, multipartBody } from '../../../../../lib/api'


import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { proxyRefs } from 'vue'

import { toast } from '../../../../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationCompetitionsCompetitionHardDeletePreviewResponse, NoCTFAPIEndpointsAdministrationCompetitionsCompetitionHardDeleteReferenceCode, NoCTFAPIEndpointsAdministrationCompetitionsStartGateErrorResponse } from '../../../../../api/models'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import { startGateErrorMessage } from '../../../../../lib/start-gate-error'
import { useCompetitionPoster } from '../../../../competitions/useCompetitionPoster'

interface LifecycleAction {
  key: string
  label: string
  visible: boolean
  destructive?: boolean
  confirm?: { title: string; description: string }
  run: () => Promise<unknown>
}

/** Owns state, effects and commands for AdminCompetitionsByIdIndexPage. */
export function useAdminCompetitionsByIdIndexPage() {
  const { competitionId, competition, canWrite, canManagePermissions, refresh } = useCompetitionAdmin()

  const { isAdministrator } = useAuth()

  const pendingAction = ref<string | null>(null)

  const actionError = ref<UiMessage | null>(null)

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

  const posterSelectionError = ref<UiMessage | null>(null)

  const posterRemoveOpen = ref(false)

  function posterUploadError(error: unknown): UiMessage {
    const parsed = parseApiError(error)
    return parsed.code === 'UploadTooLarge'
      ? translate('common.error.uploadTooLarge')
      : ['SizeInvalid', 'SourceMetadataMismatch', 'UnsupportedFormat', 'InvalidDimensions', 'PixelLimitExceeded', 'MultipleFrames', 'MalformedImage'].includes(parsed.code ?? '')
        ? translate('common.createCompetition.validation.posterJpegFormat')
        : parsed.displayMessage
  }

  async function selectPoster(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement
    const file = input.files?.[0] ?? null
    posterSelectionError.value = null
    if (!file || posterPending.value || !canWrite.value || isDeleted.value) return
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
      posterSelectionError.value = describeMessage('common.createCompetition.validation.posterJpegFormat')
      return
    }

    posterPending.value = true
    try {
      await api.api.v1.admin.competitions.byCompetitionId(competitionId).poster.put(await multipartBody({ file }));
      await refreshPoster()
      await loadHardDeletePreview()
      toast.success(describeMessage('administration.label.competitionPosterUpdated'))
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
      await api.api.v1.admin.competitions.byCompetitionId(competitionId).poster.delete();
      clearPoster()
      posterRemoveOpen.value = false
      posterInputKey.value += 1
      await loadHardDeletePreview()
      toast.success(describeMessage('administration.label.competitionPosterRemoved'))
    }
    catch (error) {
      toast.error(parseApiError(error).displayMessage)
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
    { value: 'Draft', label: "common.label.draft" },
    { value: 'Visible', label: "common.label.visible" },
    { value: 'Published', label: "administration.label.published" },
    { value: 'Running', label: "common.label.running" },
    { value: 'Finished', label: "common.label.finished" },
  ]

  const updateStatus = (target: 'Visible' | 'Published' | 'Running' | 'Paused' | 'Finished') =>
    api.api.v1.admin.competitions.byCompetitionId(competitionId).status.put({ status: target })

  const actions = computed<LifecycleAction[]>(() => [
    {
      key: 'make-visible',
      label: translate("administration.competitionsBy.label.visibleOutsideWorld"),
      visible: status.value === 'Draft',
      run: () => updateStatus('Visible'),
    },
    {
      key: 'publish',
      label: translate("administration.label.postContest"),
      visible: status.value === 'Visible',
      run: () => updateStatus('Published'),
    },
    {
      key: 'start',
      label: translate("administration.label.startGame"),
      visible: status.value === 'Published',
      confirm: { title: translate("administration.label.startGame"), description: translate("administration.competitionsBy.description.matchStartImmediatelyRuntime") },
      run: () => updateStatus('Running'),
    },
    {
      key: 'pause',
      label: translate("administration.label.pauseGame"),
      visible: status.value === 'Running',
      run: () => updateStatus('Paused'),
    },
    {
      key: 'resume',
      label: translate("administration.label.resumePlay"),
      visible: status.value === 'Paused',
      run: () => updateStatus('Running'),
    },
    {
      key: 'finish',
      label: translate("administration.label.endGame"),
      visible: status.value === 'Published' || status.value === 'Running' || status.value === 'Paused',
      destructive: true,
      confirm: { title: translate("administration.label.endGame"), description: translate("administration.competitionsBy.description.endingMatchIrreversibleClean") },
      run: () => updateStatus('Finished'),
    },
  ])

  const confirmTarget = ref<LifecycleAction | null>(null)

  async function execute(action: LifecycleAction) {
    pendingAction.value = action.key
    actionError.value = null
    try {
      await action.run()
      toast.success(describeMessage("administration.label.succeeded", { action: action.label }))
      await refresh()
    }
    catch (e) {
      actionError.value = parseApiError(e).displayMessage
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

  const validationErrors = ref<NoCTFAPIEndpointsAdministrationCompetitionsStartGateErrorResponse[] | null>(null)

  async function validateStart() {
    validating.value = true
    validationErrors.value = null
    try {
      const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).startValidation.get();
      validationErrors.value = data?.errors ?? []
      if (validationErrors.value.length === 0) toast.success(describeMessage("administration.label.preStartCheckPassed"))
    }
    catch (e) {
      toast.error(parseApiError(e).displayMessage)
    }
    finally {
      validating.value = false
    }
  }

  const generating = ref(false)

  const generateFailures = ref<{ competitionChallengeId?: string | null; teamId?: string | null; code?: string | null; description?: string | null }[]>([])

  async function generateMissingFlags() {
    generating.value = true
      generateFailures.value = []
    try {
      const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).flags.generateMissing.post();
      generateFailures.value = data?.failures ?? []
      if (generateFailures.value.length === 0) toast.success(describeMessage("administration.label.missingFlagGenerated"))
      else toast.warning(describeMessage("administration.label.generationCompletedFailures", { count: generateFailures.value.length }))
    }
    catch (e) {
      toast.error(parseApiError(e).displayMessage)
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

  const forceDeleteError = ref<UiMessage | null>(null)

  const forceDeleteConflictingIds = ref<string[]>([])

  const hardDeletePreview = ref<NoCTFAPIEndpointsAdministrationCompetitionsCompetitionHardDeletePreviewResponse | null>(null)

  const hardDeletePreviewLoading = ref(false)

  const hardDeletePreviewError = ref<UiMessage | null>(null)

  let hardDeletePreviewRequest = 0

  const hardDeleteReferenceLabels: Record<NoCTFAPIEndpointsAdministrationCompetitionsCompetitionHardDeleteReferenceCode, string> = {
    HistoricalEvent: "common.label.permanentCompetitionEvents",
    Team: "common.label.team",
    CompetitionChallenge: "common.label.competitionChallenges",
    GameplayFact: "common.label.gameplayFacts",
    RuntimeInstance: "common.label.runtimeEnvironment",
    PatchUpload: "common.label.patchUpload.platformUsersPage",
    Notification: "common.label.notificationsQuestions",
    PosterFile: "common.label.competitionPoster",
    TeamWriteUp: "writeUp.teamWriteUpFiles",
    ActiveRuntimeResource: "competitions.deletion.activeRuntimeResources",
    NotificationScopeConflict: "common.competitionsBy.description.crossScopeUnprovenNotification",
    ProgressionGraph: 'progression.referenceGraph',
    Badge: 'progression.referenceBadge',
    BadgeGrant: 'progression.referenceBadgeGrant',
  }

  function hardDeleteReferenceLabel(code?: NoCTFAPIEndpointsAdministrationCompetitionsCompetitionHardDeleteReferenceCode | null) {
    return code ? translate(hardDeleteReferenceLabels[code]) : translate("administration.label.unknownReference")
  }

  function isHardDeletePreview(value: unknown): value is NoCTFAPIEndpointsAdministrationCompetitionsCompetitionHardDeletePreviewResponse {
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
      const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).hardDeletePreview.get();
      if (request === hardDeletePreviewRequest) hardDeletePreview.value = data ?? null
    }
    catch (error) {
      if (request === hardDeletePreviewRequest) {
        hardDeletePreviewError.value = parseApiError(error).displayMessage
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
      await api.api.v1.admin.competitions.byCompetitionId(competitionId).delete();
      toast.success(describeMessage("administration.label.contestDeleted"))
      await refresh()
      await loadHardDeletePreview()
    }
    catch (e) {
      toast.error(parseApiError(e).displayMessage)
    }
    finally {
      deleting.value = false
      deleteConfirm.value = null
    }
  }

  async function restore() {
    restoring.value = true
    try {
      await api.api.v1.admin.competitions.byCompetitionId(competitionId).restore.post();
      toast.success(describeMessage("administration.label.competitionResumed"))
      await refresh()
      await loadHardDeletePreview()
    }
    catch (e) {
      toast.error(parseApiError(e).displayMessage)
    }
    finally {
      restoring.value = false
    }
  }

  async function hardDelete() {
    hardDeleting.value = true
    try {
      await api.api.v1.admin.competitions.byCompetitionId(competitionId).hardDelete.delete();
      toast.success(describeMessage("administration.competitionsBy.label.contestCompletelyDeleted"))
      await navigateTo('/competitions')
    }
    catch (e) {
      if (isHardDeletePreview(e)) {
        hardDeletePreview.value = e
        toast.error(describeMessage("administration.competitionsBy.description.competitionStillPermanentHistory"))
      }
      else {
        toast.error(parseApiError(e).displayMessage)
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
      let error: unknown;
      await api.api.v1.admin.competitions.byCompetitionId(competitionId).forceDelete.post({
          confirmationTitle: forceDeleteTitle.value,
          reason: forceDeleteReason.value.trim(),
        }).catch(cause => { error = cause; return undefined });
      if (error) {
        if (typeof error === 'object' && 'conflictingNotificationIds' in error
          && Array.isArray(error.conflictingNotificationIds)) {
          forceDeleteConflictingIds.value = error.conflictingNotificationIds.filter((id): id is string => typeof id === 'string')
        }
        throw error
      }
      forceDeleteOpen.value = false
      toast.success(describeMessage("administration.competitionsBy.description.competitionScopedDataWere"))
      await navigateTo('/competitions')
    }
    catch (error) {
      forceDeleteError.value = parseApiError(error).displayMessage
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
