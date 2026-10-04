
import { api } from '../../lib/api'
import type { UiMessage } from '../../utils/i18n'
import { message as describeMessage } from '../../utils/i18n'
import { markRaw, proxyRefs, toRefs } from 'vue'
import { toast } from '../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationChallengeBankChallengeDefinitionContract, NoCTFAPIEndpointsAdministrationChallengeBankChallengeVisibilityProtocol, NoCTFAPIEndpointsCompetitionsGameModeProtocol } from '../../api/models'
import { challengeTemplateWriteErrorMessages } from '../../lib/challenge-template-error'
import { validateChallengeTemplateDraft } from '../../lib/challenge-template-validation'
import { challengeDirectionOptions, directionLabel } from '../../utils/directions'
import { defaultDefinition, definitionContractToModel } from '../../utils/game-config'
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
  const title = ref('')
  const mode = ref<NoCTFAPIEndpointsCompetitionsGameModeProtocol>('Ctf')
  const visibility = ref<NoCTFAPIEndpointsAdministrationChallengeBankChallengeVisibilityProtocol>('Private')
  const direction = ref<(typeof challengeDirectionOptions)[number]>('Misc')
  const description = ref('')
  const definition = ref<NoCTFAPIEndpointsAdministrationChallengeBankChallengeDefinitionContract>(
    defaultDefinition(mode.value),
  )
  const saveErrors = ref<UiMessage[]>([])
  const saveAttempted = ref(false)
  const pending = ref(false)

  const titleInvalid = computed(() => saveAttempted.value
    && (!title.value.trim() || title.value.trim().length > 160))
  const directionInvalid = computed(() => saveAttempted.value && !direction.value)

  function reset() {
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
    if (value !== 'Ctf' && value !== 'Awd' && value !== 'Awdp' && value !== 'Koh') return
    definition.value = defaultDefinition(value)
    mode.value = value
  }

  async function submit(): Promise<void> {
    if (pending.value) return
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
      let error: unknown;
      const data = await api.api.v1.admin.challenges.post({
          title: title.value.trim(),
          mode: mode.value,
          visibility: visibility.value,
          direction: directionLabel(direction.value),
          description: description.value.trim() || null,
          definition: definition.value,
        }).catch(cause => { error = cause; return undefined });
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
    title,
    mode,
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
