import { proxyRefs } from 'vue'
import { markRaw } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { Paperclip, RotateCcw, Trash2, Upload } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminChallengeBankCreateFlag, adminChallengeBankDeleteAttachment, adminChallengeBankDeleteFlag, adminChallengeBankDeleteTemplate, adminChallengeBankGetTemplate, adminChallengeBankListAttachments, adminChallengeBankListFlags, adminChallengeBankPatchTemplate, adminChallengeBankRestoreAttachment, adminChallengeBankRestoreFlag, adminChallengeBankRestoreTemplate, adminChallengeBankUploadAttachments } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeAttachmentResponse, NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagResponse, NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagMatchKindProtocol, NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateContentPatchRequest, NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse, NoCtfapiEndpointsAdministrationChallengeBankChallengeVisibilityProtocol, NoCtfapiEndpointsCompetitionsGameModeProtocol, NoCtfapiEndpointsAdministrationChallengeBankAttachmentDeliveryPolicyProtocol, NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagFailureResponse, NoCtfapiEndpointsAdministrationChallengeBankAttachmentBatchFailureResponse } from '../../../../api'
import { challengeTemplateWriteErrorMessages } from '../../../../lib/challenge-template-error'
import { validateChallengeTemplateDraft } from '../../../../lib/challenge-template-validation'
import type { DefinitionModel } from '../../../../utils/game-config'
import { applyCtfInteraction, CtfInteraction, defaultDefinitionJson, FlagSource, normalizeDefinitionJson, parseDefinition } from '../../../../utils/game-config'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'
import AdminGameModeBadgeComponent from '../../../admin/AdminGameModeBadge.vue'
import ChallengeTestRuntimePanelComponent from '../../../admin/ChallengeTestRuntimePanel.vue'
import DefinitionCheckerSectionComponent from '../../../admin/DefinitionCheckerSection.vue'
import DefinitionFlagInjectionSectionComponent from '../../../admin/DefinitionFlagInjectionSection.vue'
import DefinitionFlagTemplateSectionComponent from '../../../admin/DefinitionFlagTemplateSection.vue'
import DefinitionPatchSectionComponent from '../../../admin/DefinitionPatchSection.vue'
import DefinitionRuntimeSectionComponent from '../../../admin/DefinitionRuntimeSection.vue'
import { challengeRuntimeDefinitionsEqual, mergeChallengeModeDefinition, mergeChallengeRuntimeDefinition } from '../../../admin/challenge-definition-sections'

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

  type SaveSection = 'basic' | 'runtime' | 'definition'

  const savingSection = ref<SaveSection | null>(null)

  const saving = computed(() => savingSection.value !== null)

  const basicSaveErrors = ref<string[]>([])

  const runtimeSaveErrors = ref<string[]>([])

  const definitionSaveErrors = ref<string[]>([])

  const basicSaveAttempted = ref(false)

  const deleting = ref(false)

  const restoring = ref(false)

  const isDeleted = computed(() => !!template.value?.deletedAt)

  const titleInvalid = computed(() => basicSaveAttempted.value
    && (!form.title.trim() || form.title.trim().length > 160))

  const directionInvalid = computed(() => basicSaveAttempted.value
    && (!form.direction.trim() || form.direction.trim().length > 96))

  const { model: definitionModel, parseFailed: definitionParseFailed } = useDefinitionModel(
    () => form.definitionJson,
    () => form.mode,
    (json) => { form.definitionJson = json },
  )

  const runtimeDefinitionModel = ref<DefinitionModel | null>(null)

  const runtimeDefinitionParseFailed = ref(false)

  const hasModeDefinition = computed(() => {
    if (form.mode === 'Awd' || form.mode === 'Awdp') return true
    if (form.mode === 'Ctf') return definitionModel.value?.interactionKind === CtfInteraction.PatchVerification
      || definitionModel.value?.runtime?.flagSource === FlagSource.PerTeam
    return false
  })

  const persistedDefinitionModel = computed(() => {
    const value = template.value
    return value?.mode
      ? parseDefinition(value.definitionJson ?? '{}', value.mode)
      : null
  })

  const usesRuntimeFlagInjection = computed(() => {
    const value = template.value
    return (value?.mode === 'Ctf' || value?.mode === 'Awdp')
      && persistedDefinitionModel.value?.runtime?.flagSource === FlagSource.PerTeam
  })

  const runtimeDisabled = computed(() =>
    persistedDefinitionModel.value !== null
    && persistedDefinitionModel.value.runtime === null,
  )

  const runtimeDefinitionDirty = computed(() => {
    if (!template.value) return true
    const mode = template.value.mode ?? 'Ctf'
    const current = runtimeDefinitionModel.value
    const persisted = persistedDefinitionModel.value
    if (!current || !persisted) return true
    return !challengeRuntimeDefinitionsEqual(mode, current, persisted)
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

  function resetRuntimeDefinition(): void {
    const mode = template.value?.mode
    if (!mode) return
    const defaults = parseDefinition(defaultDefinitionJson(mode), mode)
    if (!defaults) return
    runtimeDefinitionModel.value = defaults
    runtimeDefinitionParseFailed.value = false
    toast.info(translate("ui.loadedTheCurrentModeSDefaultChallengeDefinitionSaveYour"))
  }

  function resetModeDefinition(): void {
    form.definitionJson = defaultDefinitionJson(form.mode)
    toast.info(translate("ui.loadedTheCurrentModeSDefaultChallengeDefinitionSaveYour"))
  }

  function syncBasicForm(value: Template): void {
    form.title = value.title ?? ''
    form.visibility = value.visibility ?? 'Private'
    form.direction = directionLabel(value.direction)
    form.description = value.description ?? ''
  }

  function syncModeDefinition(value: Template): void {
    // Keep definitionJson ahead of mode while loading existing templates.
    // useDefinitionModel watches mode and serializes the current parsed model;
    // setting mode first can briefly serialize the empty initial CTF model as the
    // loaded mode and hide persisted runtime/checker settings in the editor.
    form.definitionJson = value.definitionJson ?? '{}'
    form.mode = value.mode ?? 'Ctf'
  }

  function syncRuntimeDefinition(value: Template): void {
    const mode = value.mode ?? 'Ctf'
    const parsed = parseDefinition(value.definitionJson ?? '{}', mode)
    runtimeDefinitionModel.value = parsed
    runtimeDefinitionParseFailed.value = parsed === null
  }

  function syncForm(value: Template): void {
    syncBasicForm(value)
    syncModeDefinition(value)
    syncRuntimeDefinition(value)
  }

  function contentFromTemplate(
    value: Template,
    overrides: Partial<NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateContentPatchRequest> = {},
  ): NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateContentPatchRequest {
    return {
      mode: value.mode ?? 'Ctf',
      visibility: value.visibility ?? 'Private',
      title: value.title ?? '',
      description: value.description ?? null,
      direction: directionLabel(value.direction),
      definitionJson: value.definitionJson ?? defaultDefinitionJson(value.mode ?? 'Ctf'),
      ...overrides,
    }
  }

  async function updateContent(
    section: SaveSection,
    content: NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateContentPatchRequest,
    errors: { value: string[] },
  ): Promise<Template | null> {
    if (savingSection.value !== null) return null
    savingSection.value = section
    const { data, error } = await adminChallengeBankPatchTemplate({
      path: { challengeId },
      body: { content },
    })
    savingSection.value = null
    if (error || !data) {
      errors.value = challengeTemplateWriteErrorMessages(error)
      toast.error(errors.value[0] ?? translate("ui.unableToSaveTheChallengeTemplate"))
      return null
    }
    template.value = data
    errors.value = []
    toast.success(translate("ui.saved"))
    return data
  }

  function validateDefinitionForSave(
    mode: NoCtfapiEndpointsCompetitionsGameModeProtocol,
    definitionJson: string,
    errors: { value: string[] },
  ): string | null {
    const value = template.value
    const normalized = normalizeDefinitionJson(mode, definitionJson)
    if (!normalized) {
      errors.value = [translate("ui.theChallengeDefinitionCannotBeParsedResetOrCorrectIt")]
      toast.error(errors.value[0] ?? translate("ui.unableToSaveTheChallengeTemplate"))
      return null
    }
    const validationErrors = validateChallengeTemplateDraft({
      mode,
      title: value?.title ?? '',
      direction: value?.direction ?? '',
      definitionJson: normalized,
    })
    if (validationErrors.length > 0) {
      errors.value = validationErrors
      toast.error(validationErrors[0] ?? translate("ui.unableToSaveTheChallengeTemplate"))
      return null
    }
    return normalized
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

  async function saveBasic(): Promise<void> {
    basicSaveAttempted.value = true
    basicSaveErrors.value = []
    const value = template.value
    if (!value) {
      basicSaveErrors.value = [translate("ui.theTemplateHasNotFinishedLoadingAndCannotBeSaved")]
      toast.error(basicSaveErrors.value[0] ?? translate("ui.unableToSaveTheChallengeTemplate"))
      return
    }
    const validationErrors: string[] = []
    if (!form.title.trim()) validationErrors.push(translate("ui.titleIsRequired"))
    else if (form.title.trim().length > 160)
      validationErrors.push(translate("ui.titleCannotExceed160Characters"))
    if (!form.direction.trim()) validationErrors.push(translate("ui.directionIsRequired"))
    else if (form.direction.trim().length > 96)
      validationErrors.push(translate("ui.directionCannotExceed96Characters"))
    if (validationErrors.length > 0) {
      basicSaveErrors.value = validationErrors
      toast.error(validationErrors[0] ?? translate("ui.unableToSaveTheChallengeTemplate"))
      return
    }
    const updated = await updateContent('basic', contentFromTemplate(value, {
      title: form.title.trim(),
      visibility: form.visibility,
      direction: directionLabel(form.direction),
      description: form.description.trim() || null,
    }), basicSaveErrors)
    if (updated) syncBasicForm(updated)
  }

  async function saveRuntimeDefinition(): Promise<void> {
    runtimeSaveErrors.value = []
    const value = template.value
    if (!value) {
      runtimeSaveErrors.value = [translate("ui.theTemplateHasNotFinishedLoadingAndCannotBeSaved")]
      toast.error(runtimeSaveErrors.value[0] ?? translate("ui.unableToSaveTheChallengeTemplate"))
      return
    }
    const mode = value.mode ?? 'Ctf'
    const merged = runtimeDefinitionModel.value
      ? mergeChallengeRuntimeDefinition(
          mode,
          value.definitionJson ?? '{}',
          runtimeDefinitionModel.value,
        )
      : null
    const normalized = merged
      ? validateDefinitionForSave(mode, merged, runtimeSaveErrors)
      : null
    if (!normalized) return
    const updated = await updateContent('runtime', contentFromTemplate(value, {
      definitionJson: normalized,
    }), runtimeSaveErrors)
    if (updated) syncRuntimeDefinition(updated)
  }

  async function saveModeDefinition(): Promise<void> {
    definitionSaveErrors.value = []
    const value = template.value
    if (!value) {
      definitionSaveErrors.value = [translate("ui.theTemplateHasNotFinishedLoadingAndCannotBeSaved")]
      toast.error(definitionSaveErrors.value[0] ?? translate("ui.unableToSaveTheChallengeTemplate"))
      return
    }
    const previousMode = value.mode ?? 'Ctf'
    const runtimeWasDirty = runtimeDefinitionDirty.value
    const merged = definitionModel.value
      ? mergeChallengeModeDefinition(
          previousMode,
          value.definitionJson ?? '{}',
          form.mode,
          definitionModel.value,
        )
      : null
    const normalized = merged
      ? validateDefinitionForSave(form.mode, merged, definitionSaveErrors)
      : null
    if (!normalized) return
    const updated = await updateContent('definition', contentFromTemplate(value, {
      mode: form.mode,
      definitionJson: normalized,
    }), definitionSaveErrors)
    if (!updated) return
    syncModeDefinition(updated)
    if (previousMode !== updated.mode || !runtimeWasDirty) {
      syncRuntimeDefinition(updated)
    }
    else if (updated.mode === 'Ctf' && runtimeDefinitionModel.value) {
      applyCtfInteraction(runtimeDefinitionModel.value, definitionModel.value?.interactionKind
        ?? CtfInteraction.FlagSubmission)
    }
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
      basicSaveErrors,
      runtimeSaveErrors,
      definitionSaveErrors,
      deleting,
      restoring,
      isDeleted,
      titleInvalid,
      directionInvalid,
      definitionModel,
      definitionParseFailed,
      runtimeDefinitionModel,
      runtimeDefinitionParseFailed,
      hasModeDefinition,
      usesRuntimeFlagInjection,
      runtimeDisabled,
      runtimeDefinitionDirty,
      CtfInteraction,
      patchVerificationEnabled,
      showInteractionKind,
      setInteractionKind,
      changeMode,
      resetRuntimeDefinition,
      resetModeDefinition,
      saveBasic,
      saveRuntimeDefinition,
      saveModeDefinition,
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
