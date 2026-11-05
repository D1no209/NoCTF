import { computed, onMounted, reactive, ref, watch } from 'vue'
import {
  adminChallengeBankGetCapabilityTest, adminChallengeBankGetTestRuntime, adminChallengeBankTestBreak,
  adminChallengeBankTestCheck, adminChallengeBankTestControl, adminChallengeBankTestFix,
  adminChallengeBankTestFixAndCheck, adminChallengeBankTestQuestionnaire,
} from '../../api'
import type {
  AdminChallengeBankGetCapabilityTestResponse, AdminChallengeBankGetTestRuntimeResponse,
  AdminChallengeBankTestBreakResponse, AdminChallengeBankTestControlResponse, AdminChallengeBankTestQuestionnaireResponse,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract,
} from '../../api'
import { parseApiError } from '../../utils/api-error'
import { message, type UiMessage } from '../../utils/i18n'
import { useQuestionnaireAssets } from '../questionnaires/useQuestionnaireAssets'
import { selectTemplateTestOption, templateCapabilityTestFinished } from './template-capability-tests'

export function useChallengeCapabilityTests(props: Readonly<{
  challengeId: string
  definition: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract
  definitionDirty: boolean
}>) {
  const route = useRoute(), router = useRouter()
  const runtime = ref<AdminChallengeBankGetTestRuntimeResponse | null>(null)
  const execution = ref<AdminChallengeBankGetCapabilityTestResponse | null>(null)
  const breakResult = ref<AdminChallengeBankTestBreakResponse | null>(null)
  const controlResult = ref<AdminChallengeBankTestControlResponse | null>(null)
  const questionnaireResult = ref<AdminChallengeBankTestQuestionnaireResponse | null>(null)
  const error = ref<UiMessage | null>(null), acting = ref(false)
  const flag = ref(''), useRuntimeFlag = ref(false), archive = ref<File | null>(null)
  const answers = reactive<Record<string, string[]>>({})
  const requestIds = new Map<string, string>()
  const files = useQuestionnaireAssets()
  const capabilities = computed(() => props.definition.capabilities?.flatMap(value => value.kind ? [value.kind] : []) ?? [])
  const questions = computed(() => props.definition.capabilities?.find(value => value.kind === 'Questionnaire')?.questionnaire?.questions ?? [])
  const has = (kind: string) => capabilities.value.some(value => value === kind)
  const executionId = computed(() => typeof route.query.capabilityTest === 'string' ? route.query.capabilityTest : null)
  const busy = computed(() => acting.value || !!execution.value && !templateCapabilityTestFinished(execution.value.state))
  const disabled = computed(() => props.definitionDirty || busy.value)
  const runtimeReady = computed(() => runtime.value?.state === 'Running' && !!runtime.value.id)
  const path = computed(() => ({ challengeId: props.challengeId }))

  async function loadRuntime() {
    const response = await adminChallengeBankGetTestRuntime({ path: path.value })
    if (response.response?.status === 404) { runtime.value = null; return }
    if (response.error) throw response.error
    runtime.value = response.data ?? null
  }
  async function loadExecution(): Promise<boolean> {
    if (!executionId.value) return true
    const expected = executionId.value
    const response = await adminChallengeBankGetCapabilityTest({ path: { ...path.value, executionId: expected } })
    if (expected !== executionId.value) return true
    if (response.error || !response.data) throw response.error
    execution.value = response.data
    return templateCapabilityTestFinished(response.data.state)
  }
  const polling = usePolling(loadExecution, { interval: 1500, timeout: 11_400_000 })
  async function run(action: () => Promise<void>) {
    if (disabled.value) return
    acting.value = true; error.value = null
    try { await action() }
    catch (cause) { error.value = parseApiError(cause, message('administration.capabilityTests.failed')).displayMessage }
    finally { acting.value = false }
  }
  function requestId(kind: string) {
    if (!requestIds.has(kind)) requestIds.set(kind, crypto.randomUUID())
    return requestIds.get(kind)!
  }
  async function accepted(id: string | undefined, kind: string) {
    if (!id) throw new Error('The capability test response has no execution identity.')
    requestIds.delete(kind)
    await router.replace({ query: { ...route.query, capabilityTest: id } })
    if (!await loadExecution()) polling.start()
  }
  const testBreak = () => run(async () => {
    const response = await adminChallengeBankTestBreak({ path: path.value, body: {
      flag: flag.value, runtimeInstanceId: useRuntimeFlag.value ? runtime.value?.id : null,
    } })
    if (response.error) throw response.error
    breakResult.value = response.data ?? null
  })
  const testControl = () => run(async () => {
    if (!runtime.value?.id) return
    const response = await adminChallengeBankTestControl({ path: path.value, body: { runtimeInstanceId: runtime.value.id } })
    if (response.error) throw response.error
    controlResult.value = response.data ?? null
  })
  const testQuestionnaire = () => run(async () => {
    const response = await adminChallengeBankTestQuestionnaire({ path: path.value, body: {
      answers: questions.value.flatMap(question => question.id ? [{ questionId: question.id, selectedOptionIds: answers[question.id] ?? [] }] : []),
    } })
    if (response.error) throw response.error
    questionnaireResult.value = response.data ?? null
  })
  const testCheck = () => run(async () => {
    if (!runtime.value?.id) return
    const response = await adminChallengeBankTestCheck({ path: path.value,
      body: { runtimeInstanceId: runtime.value.id, requestId: requestId('Check') } })
    if (response.error) throw response.error
    await accepted(response.data?.executionId, 'Check')
  })
  const testFix = (withCheck: boolean) => run(async () => {
    if (!archive.value) return
    const kind = withCheck ? 'FixAndCheck' : 'Fix'
    const operation = withCheck ? adminChallengeBankTestFixAndCheck : adminChallengeBankTestFix
    const response = await operation({ path: path.value, body: { file: archive.value, requestId: requestId(kind) } })
    if (response.error) throw response.error
    await accepted(response.data?.executionId, kind)
  })
  function selectFile(event: Event) {
    archive.value = (event.target as HTMLInputElement).files?.[0] ?? null
    requestIds.delete('Fix'); requestIds.delete('FixAndCheck')
  }
  function selectOption(questionId: string | undefined, optionId: string | undefined, multiple: boolean, checked: boolean | 'indeterminate') {
    if (disabled.value || !questionId || !optionId) return
    answers[questionId] = selectTemplateTestOption(answers[questionId] ?? [], optionId, multiple, checked === true)
  }
  const questionnaireQuestions = computed(() => questions.value.map(question => {
    const result = questionnaireResult.value?.questions?.find(value => value.questionId === question.id)?.result
    return { ...question, resultMessage: result ? message(`administration.capabilityTests.result.${result}`) : null }
  }))
  async function refresh() {
    try { await loadRuntime(); if (executionId.value && !await loadExecution()) polling.start() }
    catch (cause) { error.value = parseApiError(cause, message('administration.capabilityTests.failed')).displayMessage }
  }
  watch(questions, values => {
    void files.load(values.flatMap(question => [question.body ?? '', ...(question.options ?? []).map(option => option.content ?? '')]), { challengeId: props.challengeId })
    for (const id of Object.keys(answers)) delete answers[id]
    questionnaireResult.value = null
  }, { immediate: true })
  onMounted(refresh)
  return { capabilities, questionnaireQuestions, has, runtime, runtimeReady, execution, breakResult, controlResult, questionnaireResult,
    error, busy, disabled, flag, useRuntimeFlag, archive, answers, assets: files.assets, timedOut: polling.timedOut,
    testBreak, testControl, testQuestionnaire, testCheck, testFix, selectFile, selectOption, refresh }
}

export type ChallengeCapabilityTestsViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useChallengeCapabilityTests>>
