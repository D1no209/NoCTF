import { computed, ref, toRefs, watch } from 'vue'
import type { DefinitionModel } from '../../utils/game-config'
import type { NoCtfapiEndpointsAdministrationChallengeBankQuestionnaireQuestionContract as Question } from '../../api'
import { translate } from '../../utils/i18n'
import { message, type UiMessage } from '../../utils/i18n'
import { adminChallengeBankUploadQuestionnaireImage } from '../../api'
import { parseApiError } from '../../utils/api-error'
import { useQuestionnaireAssets } from '../questionnaires/useQuestionnaireAssets'

export function useQuestionnaireCapabilityEditor(props: Readonly<{ model: DefinitionModel; disabled: boolean; challengeId?: string; savedQuestionIds?: string[] }>) {
  const questions = computed(() => props.model.questionnaire?.questions ?? [])
  const files = useQuestionnaireAssets()
  const uploading = ref<string | null>(null), error = ref<UiMessage | null>(null)
  const difficulties = [
    { value: 'Basic', label: 'theory.basic' },
    { value: 'Intermediate', label: 'theory.intermediate' },
    { value: 'Advanced', label: 'theory.advanced' },
  ] as const
  const imageSources = computed(() => questions.value.flatMap(question => [question.body ?? '', question.explanation ?? '',
    ...(question.options ?? []).map(option => option.content ?? '')]))
  watch(() => [props.challengeId, imageSources.value] as const, () => {
    if (props.challengeId) void files.load(imageSources.value, { challengeId: props.challengeId })
    else files.clear()
  }, { immediate: true })
  const kinds = [
    { value: 'SingleChoice', label: 'administration.capability.singleChoice' },
    { value: 'MultipleChoice', label: 'administration.capability.multipleChoice' },
    { value: 'TrueFalse', label: 'administration.capability.trueFalse' },
  ] as const
  function addQuestion() {
    const questionnaire = props.model.questionnaire
    if (props.disabled || !questionnaire) return
    questionnaire.questions ??= []
    questionnaire.questions.push({ id: crypto.randomUUID(), sourceId: crypto.randomUUID(), kind: 'SingleChoice', difficulty: 'Basic',
      title: '', body: '', explanation: '', classifications: [], fileIds: [],
      options: [{ id: crypto.randomUUID(), content: '', isCorrect: true }, { id: crypto.randomUUID(), content: '', isCorrect: false }] })
  }
  function removeQuestion(id: string | undefined) {
    if (!props.disabled && props.model.questionnaire) props.model.questionnaire.questions = questions.value.filter(question => question.id !== id)
  }
  function setKind(question: Question, value: unknown) {
    const selected = kinds.find(kind => kind.value === value)
    if (props.disabled || !selected) return
    question.kind = selected.value
    if (selected.value === 'TrueFalse') question.options = [
      { id: crypto.randomUUID(), content: translate('administration.capability.answerTrue'), truthValue: true, isCorrect: true },
      { id: crypto.randomUUID(), content: translate('administration.capability.answerFalse'), truthValue: false, isCorrect: false },
    ]
    else for (const [index, option] of (question.options ?? []).entries()) {
      option.truthValue = null
      if (selected.value === 'SingleChoice') option.isCorrect = index === 0
    }
  }
  function addOption(question: Question) {
    if (props.disabled || question.kind === 'TrueFalse') return
    question.options ??= []
    question.options.push({ id: crypto.randomUUID(), content: '', isCorrect: false })
  }
  function removeOption(question: Question, id: string | undefined) {
    if (!props.disabled && question.kind !== 'TrueFalse') question.options = question.options?.filter(option => option.id !== id)
  }
  function setCorrect(question: Question, id: string | undefined, enabled: boolean) {
    if (props.disabled) return
    for (const option of question.options ?? []) {
      if (option.id === id) option.isCorrect = enabled
      else if (enabled && question.kind !== 'MultipleChoice') option.isCorrect = false
    }
  }
  function setExplanation(question: Question, value: string | number) { if (!props.disabled) question.explanation = String(value) }
  function setDifficulty(question: Question, value: unknown) {
    const selected = difficulties.find(item => item.value === value)
    if (!props.disabled && selected) question.difficulty = selected.value
  }
  function classifications(question: Question, kind: 'Knowledge' | 'Tag') {
    return (question.classifications ?? []).filter(item => item.kind === kind).map(item => item.name).join(', ')
  }
  function setClassifications(question: Question, kind: 'Knowledge' | 'Tag', value: string | number) {
    if (props.disabled) return
    const names = [...new Set(String(value).split(/[,，]/).map(name => name.trim()).filter(Boolean))]
    question.classifications = [...(question.classifications ?? []).filter(item => item.kind !== kind), ...names.map(name => ({ kind, name }))]
  }
  function canUpload(question: Question) { return !!props.challengeId && !!question.id && props.savedQuestionIds?.includes(question.id) === true }
  async function uploadImage(question: Question, event: Event) {
    const input = event.target as HTMLInputElement
    const file = input.files?.[0]
    if (!file || props.disabled || uploading.value || !canUpload(question)) return
    uploading.value = question.id!; error.value = null
    try {
      const response = await adminChallengeBankUploadQuestionnaireImage({ path: { challengeId: props.challengeId!, questionId: question.id! }, body: { file } })
      if (response.error || !response.data?.fileId || !response.data.revisionId) throw response.error
      question.fileIds = [...new Set([...(question.fileIds ?? []), response.data.fileId])]
      question.body = `${question.body ?? ''}\n\n![](questionnaire-file:${response.data.fileId})`
      if (props.model.questionnaire) props.model.questionnaire.revisionId = response.data.revisionId
    }
    catch (cause) { error.value = parseApiError(cause, message('theory.imageFailed')).displayMessage }
    finally { uploading.value = null; input.value = '' }
  }
  return { ...toRefs(props), questions, kinds, difficulties, addQuestion, removeQuestion, setKind, addOption, removeOption,
    setCorrect, setExplanation, setDifficulty, classifications, setClassifications, canUpload, uploadImage, uploading, error, assets: files.assets }
}
export type QuestionnaireCapabilityEditorViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useQuestionnaireCapabilityEditor>>
