import { proxyRefs } from 'vue'
import { markRaw } from 'vue'

import { toast } from 'vue-sonner'
import { adminChallengeBankCreateTemplate } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeVisibilityProtocol, NoCtfapiEndpointsCompetitionsGameModeProtocol } from '../../../../api'
import { challengeTemplateWriteErrorMessages } from '../../../../lib/challenge-template-error'
import { validateChallengeTemplateDraft } from '../../../../lib/challenge-template-validation'
import { defaultDefinitionJson, normalizeDefinitionJson } from '../../../../utils/game-config'
import DefinitionEditorComponent from '../../../admin/DefinitionEditor.vue'

/** Owns state, effects and commands for AdminChallengesNewPage. */
export function useAdminChallengesNewPage() {
  const { canOrganize } = useAuth()

  const title = ref('')

  const mode = ref<NoCtfapiEndpointsCompetitionsGameModeProtocol>('Ctf')

  const visibility = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeVisibilityProtocol>('Private')

  const direction = ref('')

  const description = ref('')

  const definitionJson = ref(defaultDefinitionJson(mode.value))

  const saveErrors = ref<string[]>([])

  const saveAttempted = ref(false)

  const pending = ref(false)

  const titleInvalid = computed(() => saveAttempted.value
    && (!title.value.trim() || title.value.trim().length > 160))

  const directionInvalid = computed(() => saveAttempted.value
    && (!direction.value.trim() || direction.value.trim().length > 96))

  function changeMode(value: unknown): void {
    if (value !== 'Ctf' && value !== 'Awd' && value !== 'Awdp' && value !== 'Koh') return
    definitionJson.value = defaultDefinitionJson(value)
    mode.value = value
  }

  async function submit(): Promise<void> {
    saveAttempted.value = true
    saveErrors.value = []
    const normalizedDefinition = normalizeDefinitionJson(mode.value, definitionJson.value)
    if (!normalizedDefinition) {
      saveErrors.value = [translate("ui.theChallengeDefinitionCannotBeParsedResetOrCorrectIt")]
      toast.error(saveErrors.value[0] ?? translate("ui.unableToSaveTheChallengeTemplate"))
      return
    }
    const validationErrors = validateChallengeTemplateDraft({
      mode: mode.value,
      title: title.value,
      direction: direction.value,
      definitionJson: normalizedDefinition,
    })
    if (validationErrors.length > 0) {
      saveErrors.value = validationErrors
      toast.error(validationErrors[0] ?? translate("ui.unableToSaveTheChallengeTemplate"))
      return
    }
    pending.value = true
    const { data, error: apiError } = await adminChallengeBankCreateTemplate({
      body: {
        title: title.value.trim(),
        mode: mode.value,
        visibility: visibility.value,
        direction: directionLabel(direction.value),
        description: description.value.trim() || null,
        definitionJson: normalizedDefinition,
      },
    })
    pending.value = false
    if (apiError || !data) {
      saveErrors.value = challengeTemplateWriteErrorMessages(apiError)
      toast.error(saveErrors.value[0] ?? translate("ui.unableToSaveTheChallengeTemplate"))
      return
    }
    toast.success(translate("ui.templateCreated"))
    await navigateTo(`/admin/challenges/${data.id}`)
  }

  const DefinitionEditor = markRaw(DefinitionEditorComponent)

  const viewBindings = {
      canOrganize,
      title,
      mode,
      visibility,
      direction,
      description,
      definitionJson,
      saveErrors,
      pending,
      titleInvalid,
      directionInvalid,
      changeMode,
      submit,
      DefinitionEditor
    }
  const viewState = proxyRefs(viewBindings)

  function onBlurDirection() {
    viewState.direction = directionLabel(viewState.direction)
  }

  return { ...viewBindings, onBlurDirection }
}

export type AdminChallengesNewPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminChallengesNewPage>>>
