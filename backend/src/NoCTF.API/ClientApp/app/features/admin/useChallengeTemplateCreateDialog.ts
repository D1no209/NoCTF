import type { UiMessage } from '../../utils/i18n'
import { message as describeMessage } from '../../utils/i18n'
import { markRaw, proxyRefs, toRefs } from 'vue'
import { toast } from '../../utils/message-toast'
import { adminChallengeBankCreateTemplate, adminChallengeBankListTemplates, adminChallengeBankGetTemplate, copyLiveSoloTemplate } from '../../api'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract, NoCtfapiEndpointsAdministrationChallengeBankChallengeVisibilityProtocol, NoCtfapiEndpointsCompetitionsGameModeProtocol } from '../../api'
import { challengeTemplateWriteErrorMessages } from '../../lib/challenge-template-error'
import { validateChallengeTemplateDraft } from '../../lib/challenge-template-validation'
import { challengeDirectionOptions, directionLabel } from '../../utils/directions'
import { defaultDefinition, definitionContractToModel } from '../../utils/game-config'
import { gameModeOptions, isGameMode } from '../../utils/game-modes'
import DefinitionEditorComponent from './DefinitionEditor.vue'

type Events = {
  (event: 'update:open', value: boolean): void
  (event: 'created', templateId: string): void
}

/** Owns the modal workflow for creating a challenge-bank template. */
export function useChallengeTemplateCreateDialog(
  props: Readonly<{ open: boolean }>,
  emit: Events,
) {
  const variant = ref<'blank' | 'copy'>('blank')
  const sourceId = ref<string | null>(null), sourceKeyword = ref(''), copyAttachments = ref(true), copyFlags = ref(false)
  const source = ref<import('../../api').NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateResponse | null>(null)
  const sources = useOffsetPagination<import('../../api').NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateSummaryResponse>(async ({ offset, limit }) => {
    const result = await adminChallengeBankListTemplates({ query: { includeDeleted: false, onlyMine: false, desc: false, mode: 'Ctf', offset, limit, keyword: sourceKeyword.value || undefined } })
    if (result.error || !result.data) throw result.error
    return result.data
  })
  const sourceOptions = computed(() => sources.items.value.filter(x => x.id && x.interactionKind === 'FlagSubmission').map(x => ({ value: x.id!, label: x.title ?? '—' })))
  const copySupported = computed(() => source.value?.definition?.ctf?.interactionKind !== 'PatchVerification'
    && !source.value?.definition?.checker && (!source.value?.definition?.runtime || source.value.definition.runtime.kind === 'Container'))
  async function searchSources() { sources.reset(); await sources.loadPage() }
  async function setVariant(value: unknown) { if (pending.value || (value !== 'blank' && value !== 'copy')) return; variant.value = value; if (value === 'copy') await searchSources() }
  async function selectSource(value: string) {
    sourceId.value = value; source.value = null
    const result = await adminChallengeBankGetTemplate({ path: { challengeId: value }, query: { includeDeleted: false } })
    if (sourceId.value !== value) return
    if (result.error || !result.data) { toast.error(translate('liveSolo.error.load')); return }
    source.value = result.data
  }
  const title = ref('')
  const mode = ref<NoCtfapiEndpointsCompetitionsGameModeProtocol>('Ctf')
  const visibility = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeVisibilityProtocol>('Private')
  const direction = ref<(typeof challengeDirectionOptions)[number]>('Misc')
  const description = ref('')
  const definition = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract>(
    defaultDefinition(mode.value),
  )
  const saveErrors = ref<UiMessage[]>([])
  const saveAttempted = ref(false)
  const pending = ref(false)

  const titleInvalid = computed(() => saveAttempted.value
    && (!title.value.trim() || title.value.trim().length > 160))
  const directionInvalid = computed(() => saveAttempted.value && !direction.value)

  function reset() {
    variant.value = 'blank'; sourceId.value = null; source.value = null; sourceKeyword.value = ''; copyAttachments.value = true; copyFlags.value = false
    title.value = ''
    mode.value = 'Ctf'
    visibility.value = 'Private'
    direction.value = 'Misc'
    description.value = ''
    definition.value = defaultDefinition('Ctf')
    saveErrors.value = []
    saveAttempted.value = false
  }

  function setOpen(value: boolean) {
    if (pending.value) return
    if (!value) reset()
    emit('update:open', value)
  }

  function changeMode(value: unknown): void {
    if (!isGameMode(value)) return
    definition.value = defaultDefinition(value)
    mode.value = value
    variant.value = 'blank'
  }

  async function submit(): Promise<void> {
    if (pending.value) return
    if (mode.value === 'LiveSolo' && variant.value === 'copy') {
      if (!source.value?.id || !source.value.concurrencyStamp || !copySupported.value) return
      pending.value = true
      try {
        const result = await copyLiveSoloTemplate({ body: { sourceChallengeId: source.value.id, expectedSourceStamp: source.value.concurrencyStamp, copyAttachments: copyAttachments.value, copyFlags: copyFlags.value } })
        if (result.error || !result.data?.challengeId) { saveErrors.value = challengeTemplateWriteErrorMessages(result.error); toast.error(saveErrors.value[0] ?? translate('liveSolo.error.operation')); return }
        const id = result.data.challengeId; reset(); emit('created', id)
      } catch (cause) { saveErrors.value = challengeTemplateWriteErrorMessages(cause); toast.error(saveErrors.value[0] ?? translate('liveSolo.error.operation')) } finally { pending.value = false }
      return
    }
    saveAttempted.value = true
    saveErrors.value = []
    const validationErrors = validateChallengeTemplateDraft({
      mode: mode.value,
      title: title.value,
      direction: direction.value,
      definition: definitionContractToModel(definition.value, mode.value),
    })
    if (validationErrors.length > 0) {
      saveErrors.value = validationErrors
      toast.error(validationErrors[0] ?? translate('administration.challengesBy.description.unableSaveChallengeTemplate'))
      return
    }

    pending.value = true
    try {
      const { data, error } = await adminChallengeBankCreateTemplate({
        body: {
          title: title.value.trim(),
          mode: mode.value,
          visibility: visibility.value,
          direction: directionLabel(direction.value),
          description: description.value.trim() || null,
          definition: definition.value,
        },
      })
      if (error || !data?.id) {
        saveErrors.value = challengeTemplateWriteErrorMessages(error)
        toast.error(saveErrors.value[0] ?? translate('administration.challengesBy.description.unableSaveChallengeTemplate'))
        return
      }
      const templateId = data.id
      toast.success(describeMessage('administration.label.templateCreated'))
      reset()
      emit('created', templateId)
    }
    catch (error) {
      saveErrors.value = challengeTemplateWriteErrorMessages(error)
      toast.error(saveErrors.value[0] ?? translate('administration.challengesBy.description.unableSaveChallengeTemplate'))
    }
    finally {
      pending.value = false
    }
  }

  const DefinitionEditor = markRaw(DefinitionEditorComponent)
  const viewBindings = {
    ...toRefs(props),
    variant, setVariant, sourceId, sourceKeyword, sourceOptions, source, copySupported, copyAttachments, copyFlags, selectSource, searchSources,
    sourcePage: sources.page, sourcePageCount: sources.pageCount, sourceTotal: sources.total, sourceLimit: sources.limit, sourceLoading: sources.loading, sourcePageChange: sources.loadPage, sourceLimitChange: sources.setPageSize,
    title,
    mode,
    modeOptions: gameModeOptions,
    visibility,
    direction,
    description,
    definition,
    saveErrors,
    pending,
    titleInvalid,
    directionInvalid,
    directionOptions: challengeDirectionOptions,
    changeMode,
    setOpen,
    submit,
    DefinitionEditor,
  }
  return proxyRefs(viewBindings)
}

export type ChallengeTemplateCreateDialogViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useChallengeTemplateCreateDialog>>
