import { proxyRefs } from 'vue'
import { markRaw } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { Paperclip, RotateCcw, Trash2, Upload } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminChallengeBankCreateFlag, adminChallengeBankDeleteAttachment, adminChallengeBankDeleteFlag, adminChallengeBankDeleteTemplate, adminChallengeBankGetTemplate, adminChallengeBankListAttachments, adminChallengeBankListFlags, adminChallengeBankPatchTemplate, adminChallengeBankRestoreAttachment, adminChallengeBankRestoreFlag, adminChallengeBankRestoreTemplate, adminChallengeBankUploadAttachments } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeAttachmentResponse, NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagResponse, NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagMatchKindProtocol, NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse, NoCtfapiEndpointsAdministrationChallengeBankChallengeVisibilityProtocol, NoCtfapiEndpointsCompetitionsGameModeProtocol, NoCtfapiEndpointsAdministrationChallengeBankAttachmentDeliveryPolicyProtocol, NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagFailureResponse, NoCtfapiEndpointsAdministrationChallengeBankAttachmentBatchFailureResponse } from '../../../../api'
import { challengeTemplateWriteErrorMessages } from '../../../../lib/challenge-template-error'
import { validateChallengeTemplateDraft } from '../../../../lib/challenge-template-validation'
import { applyCtfInteraction, CtfInteraction, defaultDefinitionJson, FlagSource, normalizeDefinitionJson, serializeDefinition } from '../../../../utils/game-config'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'
import AdminGameModeBadgeComponent from '../../../admin/AdminGameModeBadge.vue'
import ChallengeTestRuntimePanelComponent from '../../../admin/ChallengeTestRuntimePanel.vue'
import DefinitionCheckerSectionComponent from '../../../admin/DefinitionCheckerSection.vue'
import DefinitionFlagInjectionSectionComponent from '../../../admin/DefinitionFlagInjectionSection.vue'
import DefinitionFlagTemplateSectionComponent from '../../../admin/DefinitionFlagTemplateSection.vue'
import DefinitionPatchSectionComponent from '../../../admin/DefinitionPatchSection.vue'
import DefinitionRuntimeSectionComponent from '../../../admin/DefinitionRuntimeSection.vue'

type Template = NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse

type Attachment = NoCtfapiEndpointsAdministrationChallengeBankChallengeAttachmentResponse

type Flag = NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagResponse

/** Owns state, effects and commands for AdminChallengesByIdPage. */
export function useAdminChallengesByIdPage() {
  const route = useRoute()

  const challengeId = route.params.id as string

  const { canOrganize } = useAuth()
  const { configuration: platformConfiguration } = usePlatform()

  const template = ref<Template | null>(null)

  const loading = ref(true)

  const loadError = ref<string | null>(null)

  const form = reactive({
    title: '',
    mode: 'Ctf' as NoCtfapiEndpointsCompetitionsGameModeProtocol,
    visibility: 'Private' as NoCtfapiEndpointsAdministrationChallengeBankChallengeVisibilityProtocol,
    direction: '',
    description: '',
    definitionJson: '{}',
  })

  const saving = ref(false)

  const saveErrors = ref<string[]>([])

  const saveAttempted = ref(false)

  const deleting = ref(false)

  const restoring = ref(false)

  const isDeleted = computed(() => !!template.value?.deletedAt)

  const titleInvalid = computed(() => saveAttempted.value
    && (!form.title.trim() || form.title.trim().length > 160))

  const directionInvalid = computed(() => saveAttempted.value
    && (!form.direction.trim() || form.direction.trim().length > 96))

  const { model: definitionModel, parseFailed: definitionParseFailed } = useDefinitionModel(
    () => form.definitionJson,
    () => form.mode,
    (json) => { form.definitionJson = json },
  )

  const hasModeDefinition = computed(() => {
    if (form.mode === 'Awd' || form.mode === 'Awdp') return true
    if (form.mode === 'Ctf') return definitionModel.value?.interactionKind === CtfInteraction.PatchVerification
      || definitionModel.value?.runtime?.flagSource === FlagSource.PerTeam
    return false
  })

  const usesRuntimeFlagInjection = computed(() =>
    (form.mode === 'Ctf' || form.mode === 'Awdp')
    && definitionModel.value?.runtime?.flagSource === FlagSource.PerTeam,
  )

  const runtimeDisabled = computed(() => definitionModel.value !== null && definitionModel.value.runtime === null)

  const runtimeDefinitionDirty = computed(() => {
    if (!template.value) return true
    const current = definitionModel.value
      ? normalizeDefinitionJson(form.mode, serializeDefinition(form.mode, definitionModel.value))
      : null
    const persisted = normalizeDefinitionJson(template.value.mode ?? form.mode, template.value.definitionJson ?? '{}')
    return current === null || persisted === null || current !== persisted
  })

  const patchVerificationEnabled = computed(() =>
    platformConfiguration.value?.experimentalFeatures?.ctfPatchVerificationEnabled === true,
  )

  const showInteractionKind = computed(() => form.mode === 'Ctf'
    && (patchVerificationEnabled.value
      || definitionModel.value?.interactionKind === CtfInteraction.PatchVerification),
  )

  function setInteractionKind(value: unknown): void {
    if (!definitionModel.value || isDeleted.value || typeof value !== 'string') return
    const interactionKind = value === 'PatchVerification'
      ? CtfInteraction.PatchVerification
      : CtfInteraction.FlagSubmission
    if (interactionKind === CtfInteraction.PatchVerification
      && !patchVerificationEnabled.value) return
    applyCtfInteraction(definitionModel.value, interactionKind)
  }

  function changeMode(value: unknown): void {
    if (value !== 'Ctf' && value !== 'Awd' && value !== 'Awdp' && value !== 'Koh') return
    form.definitionJson = defaultDefinitionJson(value)
    form.mode = value
  }

  function resetDefinitionToCurrentMode(): void {
    form.definitionJson = defaultDefinitionJson(form.mode)
    toast.info(translate("ui.loadedTheCurrentModeSDefaultChallengeDefinitionSaveYour"))
  }

  function syncForm(value: Template): void {
    form.title = value.title ?? ''
    form.visibility = value.visibility ?? 'Private'
    form.direction = directionLabel(value.direction)
    form.description = value.description ?? ''
    // Keep definitionJson ahead of mode while loading existing templates.
    // useDefinitionModel watches mode and serializes the current parsed model;
    // setting mode first can briefly serialize the empty initial CTF model as the
    // loaded mode and hide persisted runtime/checker settings in the editor.
    form.definitionJson = value.definitionJson ?? '{}'
    form.mode = value.mode ?? 'Ctf'
  }

  async function loadTemplate(): Promise<void> {
    loading.value = true
    loadError.value = null
    const { data, error } = await adminChallengeBankGetTemplate({
      path: { challengeId },
      query: { includeDeleted: true },
    })
    loading.value = false
    if (error || !data) {
      loadError.value = parseApiError(error, translate("ui.theTemplateDoesNotExistOrFailedToLoad")).message
      return
    }
    template.value = data
    syncForm(data)
    syncPermissions(data)
  }

  async function save(): Promise<void> {
    saveAttempted.value = true
    saveErrors.value = []
    if (!template.value) {
      saveErrors.value = [translate("ui.theTemplateHasNotFinishedLoadingAndCannotBeSaved")]
      toast.error(saveErrors.value[0] ?? translate("ui.unableToSaveTheChallengeTemplate"))
      return
    }
    // Read directly from the structured editor model. Its JSON bridge is watched,
    // so relying only on form.definitionJson can miss the latest edit when Save is
    // clicked in the same interaction cycle.
    const currentDefinition = definitionModel.value
      ? serializeDefinition(form.mode, definitionModel.value)
      : form.definitionJson
    const normalizedDefinition = normalizeDefinitionJson(form.mode, currentDefinition)
    if (!normalizedDefinition) {
      saveErrors.value = [translate("ui.theChallengeDefinitionCannotBeParsedResetOrCorrectIt")]
      toast.error(saveErrors.value[0] ?? translate("ui.unableToSaveTheChallengeTemplate"))
      return
    }
    const validationErrors = validateChallengeTemplateDraft({
      mode: form.mode,
      title: form.title,
      direction: form.direction,
      definitionJson: normalizedDefinition,
    })
    if (validationErrors.length > 0) {
      saveErrors.value = validationErrors
      toast.error(validationErrors[0] ?? translate("ui.unableToSaveTheChallengeTemplate"))
      return
    }
    saving.value = true
    const { data, error } = await adminChallengeBankPatchTemplate({
      path: { challengeId },
      body: {
        content: {
          title: form.title.trim(),
          mode: form.mode,
          visibility: form.visibility,
          direction: directionLabel(form.direction),
          description: form.description.trim() || null,
          definitionJson: normalizedDefinition,
        },
      },
    })
    saving.value = false
    if (error) {
      saveErrors.value = challengeTemplateWriteErrorMessages(error)
      toast.error(saveErrors.value[0] ?? translate("ui.unableToSaveTheChallengeTemplate"))
      return
    }
    if (data) {
      template.value = data
      syncForm(data)
    }
    saveErrors.value = []
    toast.success(translate("ui.saved"))
  }

  async function removeTemplate(): Promise<void> {
    deleting.value = true
    const { error } = await adminChallengeBankDeleteTemplate({ path: { challengeId } })
    deleting.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    toast.success(translate("ui.templateDeleted"))
    await navigateTo('/admin/challenges')
  }

  async function restoreTemplate(): Promise<void> {
    restoring.value = true
    const { error } = await adminChallengeBankRestoreTemplate({ path: { challengeId } })
    restoring.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    toast.success(translate("ui.templateRestored"))
    await loadTemplate()
  }

  const attachments = ref<Attachment[]>([])

  const attachmentsLoading = ref(false)

  const attachmentsIncludeDeleted = ref(false)

  const attachmentDeliveryPolicy = ref<NoCtfapiEndpointsAdministrationChallengeBankAttachmentDeliveryPolicyProtocol>('All')

  const uploading = ref(false)

  const uploadInput = ref<HTMLInputElement | null>(null)

  const randomBatchOpen = ref(false)

  const randomUploading = ref(false)

  const randomDownloadFileName = ref('challenge.zip')

  const randomFiles = ref<File[]>([])

  const randomUploadInput = ref<HTMLInputElement | null>(null)

  const deletingAttachment = ref<Attachment | null>(null)

  const attachmentActionPending = ref(false)

  function requestAttachmentDeliveryPolicy(value: unknown): void {
    if (value !== 'All' && value !== 'RandomOnePerTeam') return
    if (value === attachmentDeliveryPolicy.value) return
    if (value === 'RandomOnePerTeam') {
      randomBatchOpen.value = true
      return
    }
    toast.info(translate("ui.deleteAllActiveRandomAttachmentVariantsFirstTheDeliveryMode"))
  }

  async function loadAttachments(): Promise<void> {
    attachmentsLoading.value = true
    const { data, error } = await adminChallengeBankListAttachments({
      path: { challengeId },
      query: { includeDeleted: attachmentsIncludeDeleted.value },
    })
    attachmentsLoading.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    attachmentDeliveryPolicy.value = data?.deliveryPolicy ?? 'All'
    attachments.value = data?.items ?? []
  }

  watch(attachmentsIncludeDeleted, () => {
    void loadAttachments()
  })

  async function uploadAttachment(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement
    const files = [...(input.files ?? [])]
    input.value = ''
    if (files.length === 0) return
    uploading.value = true
    const { error: failed } = await adminChallengeBankUploadAttachments({
      path: { challengeId },
      body: { deliveryPolicy: 'All', files },
    })
    uploading.value = false
    if (failed) {
      toast.error(parseApiError(failed).message)
      await loadAttachments()
      return
    }
    toast.success(translate("ui.uploadedAttachments", { count: files.length }))
    await loadAttachments()
  }

  function selectRandomFiles(event: Event): void {
    const input = event.target as HTMLInputElement
    randomFiles.value = [...(input.files ?? [])]
    input.value = ''
  }

  async function uploadRandomBatch(): Promise<void> {
    if (!randomDownloadFileName.value.trim() || randomFiles.value.length === 0) return
    randomUploading.value = true
    const { error } = await adminChallengeBankUploadAttachments({
      path: { challengeId },
      body: {
        deliveryPolicy: 'RandomOnePerTeam',
        downloadFileName: randomDownloadFileName.value,
        files: randomFiles.value,
      },
    })
    randomUploading.value = false
    if (error) {
      toast.error(randomAttachmentErrorMessage(error))
      return
    }
    toast.success(translate("ui.createdRandomAttachmentVariants", { count: randomFiles.value.length }))
    randomFiles.value = []
    randomBatchOpen.value = false
    await Promise.all([loadAttachments(), loadFlags()])
  }

  function randomAttachmentErrorMessage(error: unknown): string {
    const failure = error as NoCtfapiEndpointsAdministrationChallengeBankAttachmentBatchFailureResponse
    switch (failure.code) {
      case 'UploadTooLarge': return translate("ui.anAttachmentVariantExceedsTheUploadSizeLimit")
      case 'InvalidFileName': return translate("ui.theSharedDownloadFilenameIsInvalid")
      case 'InvalidVariantFileName': return translate("ui.anOriginalFilenameIsNotAValidExactFlag")
      case 'EmptyBatch': return translate("ui.selectAtLeastOneAttachmentVariant")
      case 'DuplicateFlag': return translate("ui.attachmentVariantFlagsMustBeUnique")
      case 'DeliveryModeConflict': return translate("ui.standardAttachmentsOrStaticFlagsCannotBeMixedWithPer")
      case 'BatchStorageFailed': return translate("ui.attachmentStorageFailedTheEntireBatchWasRolledBack")
      case 'BatchPersistenceFailed': return translate("ui.theAttachmentBatchCouldNotBeSavedTheEntireBatch")
      case 'ResourceIdConflict': return translate("ui.theAttachmentResourceIdentifierConflictsUploadTheBatchAgain")
      default: return parseApiError(error).message
    }
  }

  function challengeFlagErrorMessage(error: unknown): string {
    const failure = error as NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagFailureResponse
    switch (failure.code) {
      case 'InvalidFlag': return translate("ui.theFlagValueIsInvalid")
      case 'InvalidRegularExpression': return translate("ui.theFlagRegularExpressionIsInvalid")
      case 'RegularExpressionNotSupported': return translate("ui.thisChallengeDoesNotSupportRegularExpressionFlags")
      case 'ManualFlagNotSupported': return translate("ui.dynamicRuntimeFlagsAreGeneratedAndInjectedByThePlatform")
      case 'SystemManagedFlag': return translate("ui.systemGeneratedDynamicFlagsAreReadOnlyAndCannotBe")
      case 'DeliveryModeConflict': return translate("ui.standardAttachmentsOrStaticFlagsCannotBeMixedWithPer")
      case 'ResourceIdConflict': return translate("ui.theFlagResourceIdentifierConflictsAddItAgain")
      default: return parseApiError(error).message
    }
  }

  async function confirmDeleteAttachment(): Promise<void> {
    const attachment = deletingAttachment.value
    if (!attachment?.id) return
    attachmentActionPending.value = true
    const { error } = await adminChallengeBankDeleteAttachment({
      path: { challengeId, attachmentId: attachment.id },
    })
    attachmentActionPending.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    deletingAttachment.value = null
    toast.success(translate("ui.attachmentDeleted"))
    await Promise.all([loadAttachments(), loadFlags()])
  }

  async function restoreAttachment(attachment: Attachment): Promise<void> {
    if (!attachment.id) return
    attachmentActionPending.value = true
    const { error } = await adminChallengeBankRestoreAttachment({
      path: { challengeId, attachmentId: attachment.id },
    })
    attachmentActionPending.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    toast.success(translate("ui.attachmentRestored"))
    await Promise.all([loadAttachments(), loadFlags()])
  }

  function formatBytes(value?: number): string {
    if (value === undefined || value === null) return '—'
    if (value < 1024) return `${value} B`
    if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`
    return `${(value / 1024 / 1024).toFixed(1)} MB`
  }

  const flags = ref<Flag[]>([])

  const supportsRegularExpression = ref(false)

  const flagsLoading = ref(false)

  const flagsIncludeDeleted = ref(false)

  const flagCreateOpen = ref(false)

  const flagCreating = ref(false)

  const flagForm = reactive({
    flag: '',
    matchKind: 'Exact' as NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagMatchKindProtocol,
  })

  const deletingFlag = ref<Flag | null>(null)

  const flagActionPending = ref(false)

  const staticFlags = computed(() => flags.value.filter(flag => !flag.systemManaged))

  const systemFlags = computed(() => flags.value.filter(flag => flag.systemManaged))

  async function loadFlags(): Promise<void> {
    flagsLoading.value = true
    const { data, error } = await adminChallengeBankListFlags({
      path: { challengeId },
      query: { includeDeleted: flagsIncludeDeleted.value },
    })
    flagsLoading.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    flags.value = data?.items ?? []
    supportsRegularExpression.value = data?.supportsRegularExpression ?? false
    if (!supportsRegularExpression.value && flagForm.matchKind === 'RegularExpression')
      flagForm.matchKind = 'Exact'
  }

  watch(flagsIncludeDeleted, () => {
    void loadFlags()
  })

  function openFlagCreate(): void {
    flagForm.flag = ''
    flagForm.matchKind = 'Exact'
    flagCreateOpen.value = true
  }

  async function createFlag(): Promise<void> {
    if (!flagForm.flag.trim()) {
      toast.error(translate("ui.pleaseFillInTheFlagContent"))
      return
    }
    flagCreating.value = true
    const { error } = await adminChallengeBankCreateFlag({
      path: { challengeId },
      body: {
        flag: flagForm.flag,
        matchKind: flagForm.matchKind,
      },
    })
    flagCreating.value = false
    if (error) {
      toast.error(challengeFlagErrorMessage(error))
      return
    }
    flagCreateOpen.value = false
    toast.success(translate("ui.flagAdded"))
    await loadFlags()
  }

  async function confirmDeleteFlag(): Promise<void> {
    const flag = deletingFlag.value
    if (!flag?.id) return
    flagActionPending.value = true
    const { error } = await adminChallengeBankDeleteFlag({
      path: { challengeId, flagId: flag.id },
    })
    flagActionPending.value = false
    if (error) {
      toast.error(challengeFlagErrorMessage(error))
      return
    }
    deletingFlag.value = null
    toast.success(translate("ui.flagHasBeenDeleted"))
    await loadFlags()
  }

  async function restoreFlag(flag: Flag): Promise<void> {
    if (!flag.id) return
    flagActionPending.value = true
    const { error } = await adminChallengeBankRestoreFlag({
      path: { challengeId, flagId: flag.id },
    })
    flagActionPending.value = false
    if (error) {
      toast.error(challengeFlagErrorMessage(error))
      return
    }
    toast.success(translate("ui.flagHasBeenRestored"))
    await loadFlags()
  }

  const managersText = ref('')

  const permissionsSaving = ref(false)

  const newOwnerId = ref('')

  const transferOpen = ref(false)

  const transferring = ref(false)

  function syncPermissions(value: Template): void {
    managersText.value = (value.managerIds ?? []).join('\n')
  }

  function parseUserIds(text: string): string[] {
    return [...new Set(text.split(/[\s,;]+/).map(id => id.trim()).filter(Boolean))]
  }

  function conflictMessage(error: unknown): string {
    const code = parseApiError(error).code
    switch (code) {
      case 'OwnerIncludedInManagerSet':
        return translate("ui.thePersonInChargeCannotAppearInTheAdministratorCollection")
      case 'UserNotFound':
        return translate("ui.someUsersDoNotExistPleaseCheckTheUserId")
      case 'RoleNotEligible':
        return translate("ui.someUserRolesDoNotMeetTheRequirementsNeedTo")
      case 'ActiveCompetitionModeConflict':
        return translate("ui.thereIsAnOngoingCompetitionReferenceAndTheModeCannot")
      default:
        return parseApiError(error).message
    }
  }

  async function savePermissions(): Promise<void> {
    if (!template.value) return
    permissionsSaving.value = true
    const { data, error } = await adminChallengeBankPatchTemplate({
      path: { challengeId },
      body: {
        permissions: {
          ownerId: template.value.ownerId!,
          managerIds: parseUserIds(managersText.value),
        },
      },
    })
    permissionsSaving.value = false
    if (error) {
      toast.error(conflictMessage(error))
      return
    }
    if (data) template.value = data
    toast.success(translate("ui.permissionsUpdated"))
  }

  async function transferOwner(): Promise<void> {
    if (!template.value || !newOwnerId.value.trim()) return
    transferring.value = true
    const managerIds = new Set(parseUserIds(managersText.value))
    managerIds.add(template.value.ownerId!)
    const { data, error } = await adminChallengeBankPatchTemplate({
      path: { challengeId },
      body: {
        permissions: {
          ownerId: newOwnerId.value.trim(),
          managerIds: [...managerIds],
        },
      },
    })
    transferring.value = false
    if (error) {
      toast.error(conflictMessage(error))
      return
    }
    transferOpen.value = false
    newOwnerId.value = ''
    if (data) {
      template.value = data
      syncPermissions(data)
    }
    toast.success(translate("ui.thePersonInChargeHasBeenTransferred"))
  }

  onMounted(() => {
    if (!canOrganize.value) return
    void (async () => {
      await loadTemplate()
      await Promise.all([loadAttachments(), loadFlags()])
    })()
  })

  const AdminDateTime = markRaw(AdminDateTimeComponent)

  const AdminGameModeBadge = markRaw(AdminGameModeBadgeComponent)

  const ChallengeTestRuntimePanel = markRaw(ChallengeTestRuntimePanelComponent)

  const DefinitionCheckerSection = markRaw(DefinitionCheckerSectionComponent)

  const DefinitionFlagInjectionSection = markRaw(DefinitionFlagInjectionSectionComponent)

  const DefinitionFlagTemplateSection = markRaw(DefinitionFlagTemplateSectionComponent)

  const DefinitionPatchSection = markRaw(DefinitionPatchSectionComponent)

  const DefinitionRuntimeSection = markRaw(DefinitionRuntimeSectionComponent)

  function setUploadInputRef(element: Element | ComponentPublicInstance | null) { uploadInput.value = (element instanceof Element ? element : element?.$el ?? null) as typeof uploadInput.value }

  function setRandomUploadInputRef(element: Element | ComponentPublicInstance | null) { randomUploadInput.value = (element instanceof Element ? element : element?.$el ?? null) as typeof randomUploadInput.value }

  const viewBindings = {
      Paperclip,
      RotateCcw,
      Trash2,
      Upload,
      challengeId,
      canOrganize,
      template,
      loading,
      loadError,
      form,
      saving,
      saveErrors,
      deleting,
      restoring,
      isDeleted,
      titleInvalid,
      directionInvalid,
      definitionModel,
      definitionParseFailed,
      hasModeDefinition,
      usesRuntimeFlagInjection,
      runtimeDisabled,
      runtimeDefinitionDirty,
      CtfInteraction,
      patchVerificationEnabled,
      showInteractionKind,
      setInteractionKind,
      changeMode,
      resetDefinitionToCurrentMode,
      save,
      removeTemplate,
      restoreTemplate,
      attachments,
      attachmentsLoading,
      attachmentsIncludeDeleted,
      attachmentDeliveryPolicy,
      uploading,
      uploadInput,
      randomBatchOpen,
      randomUploading,
      randomDownloadFileName,
      randomFiles,
      randomUploadInput,
      deletingAttachment,
      attachmentActionPending,
      requestAttachmentDeliveryPolicy,
      uploadAttachment,
      selectRandomFiles,
      uploadRandomBatch,
      confirmDeleteAttachment,
      restoreAttachment,
      formatBytes,
      flags,
      supportsRegularExpression,
      flagsLoading,
      flagsIncludeDeleted,
      flagCreateOpen,
      flagCreating,
      flagForm,
      deletingFlag,
      flagActionPending,
      staticFlags,
      systemFlags,
      openFlagCreate,
      createFlag,
      confirmDeleteFlag,
      restoreFlag,
      managersText,
      permissionsSaving,
      newOwnerId,
      transferOpen,
      transferring,
      savePermissions,
      transferOwner,
      AdminDateTime,
      AdminGameModeBadge,
      ChallengeTestRuntimePanel,
      DefinitionCheckerSection,
      DefinitionFlagInjectionSection,
      DefinitionFlagTemplateSection,
      DefinitionPatchSection,
      DefinitionRuntimeSection,
      setUploadInputRef,
      setRandomUploadInputRef
    }
  const viewState = proxyRefs(viewBindings)

  function onBlurFormDirection() {
    viewState.form!.direction = directionLabel(viewState.form.direction)
  }

  function onClickRandomBatchOpen(value: typeof viewState.randomBatchOpen) {
    viewState.randomBatchOpen = value
  }

  function onClickDeletingAttachment(value: typeof viewState.deletingAttachment) {
    viewState.deletingAttachment = value
  }

  function onClickDeletingFlag(value: typeof viewState.deletingFlag) {
    viewState.deletingFlag = value
  }

  function onClickTransferOpen(value: typeof viewState.transferOpen) {
    viewState.transferOpen = value
  }

  function onUpdateOpenDeletingAttachment(open: boolean) {
     if (!open) viewState.deletingAttachment = null
  }

  function onClickRandomBatchOpen2(value: typeof viewState.randomBatchOpen) {
    viewState.randomBatchOpen = value
  }

  function onClickFlagCreateOpen(value: typeof viewState.flagCreateOpen) {
    viewState.flagCreateOpen = value
  }

  function onUpdateOpenDeletingFlag(open: boolean) {
     if (!open) viewState.deletingFlag = null
  }

  return { ...viewBindings, onBlurFormDirection, onClickRandomBatchOpen, onClickDeletingAttachment, onClickDeletingFlag, onClickTransferOpen, onUpdateOpenDeletingAttachment, onClickRandomBatchOpen2, onClickFlagCreateOpen, onUpdateOpenDeletingFlag }
}

export type AdminChallengesByIdPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminChallengesByIdPage>>>
