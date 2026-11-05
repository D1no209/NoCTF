import { computed, onScopeDispose, ref, toRefs, watch } from 'vue'
import { getLiveSoloFixTarget, requestLiveSoloFixTarget, submitLiveSoloFix, getGameplayFactStatusEndpoint } from '~/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloQuestionResponse as Question,
  NoCtfapiEndpointsLiveSoloLiveSoloFixTargetResponse as Target,
  NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse as Status } from '~/api'
import { useHumanVerification } from '~/features/security/useHumanVerification'
import { createCommandAttempt } from '~/utils/command-attempt'
import { gameplayFactFailureCodeLabel, gameplayFactResultLabel, isGameplayFactPending } from '~/utils/labels'
import { message, type UiMessage } from '~/utils/i18n'
import { liveSoloFailureMessage, parseLiveSoloError } from './live-solo-errors'

export function useLiveSoloFixSubmit(props: Readonly<{ competitionId: string; matchId: string; roundId: string; question: Question; canSubmit: boolean }>,
  emit: { (event: 'changed'): void }) {
  const { user } = useAuth()
  const target = ref<Target | null>(null), status = ref<Status | null>(null), file = ref<File | null>(null)
  const pending = ref(false), error = ref<UiMessage | null>(null), uploadKey = ref(0)
  const targetAttempt = createCommandAttempt(), uploadAttempt = createCommandAttempt(), verification = useHumanVerification()
  const path = () => ({ competitionId: props.competitionId, matchId: props.matchId, roundId: props.roundId, questionId: props.question.id! })
  const action = computed(() => props.question.availableActions?.find(value => value.kind === 'SubmitFix'))
  const prepareAction = computed(() => props.question.availableActions?.find(value => value.kind === 'PrepareFixTarget'))
  const ready = computed(() => !!target.value?.runtimeInstanceId && !target.value.gameplayFactId
    && ['Queued', 'Provisioning', 'Running'].includes(target.value.state ?? ''))
  const canPrepare = computed(() => props.canSubmit && prepareAction.value?.enabled === true && !ready.value && !pending.value)
  const canUpload = computed(() => props.canSubmit && action.value?.failure == null && ready.value && !!file.value && !pending.value)
  const unavailable = computed(() => action.value?.failure ? liveSoloFailureMessage(action.value.failure) : null)
  const factPending = computed(() => !!target.value?.gameplayFactId && (!status.value || isGameplayFactPending(status.value.state)))
  const result = computed(() => !status.value ? null : status.value.failureCode ? gameplayFactFailureCodeLabel(status.value.failureCode)
    : gameplayFactResultLabel(status.value.result ?? undefined))
  const phaseLabel = computed(() => {
    switch (status.value?.fixExecution?.phase) {
      case 'Applying': return 'challenges.fix.phase.applying'
      case 'Checking': return 'challenges.fix.phase.checking'
      case 'Finalizing': return 'challenges.fix.phase.finalizing'
      case 'Finished': return 'challenges.fix.phase.finished'
      default: return 'challenges.fix.phase.queued'
    }
  })
  let epoch = 0, reading = false
  async function refresh() {
    if (reading || !props.question.id) return false
    const current = epoch, scope = path(); reading = true
    try {
      const response = await getLiveSoloFixTarget({ path: scope })
      if (current !== epoch) return true
      if (response.error || !response.data) throw parseLiveSoloError(response.error, message('liveSolo.error.load'))
      const previousFact = target.value?.gameplayFactId
      target.value = response.data
      if (previousFact !== response.data.gameplayFactId) status.value = null
      if (response.data.gameplayFactId) {
        const fact = await getGameplayFactStatusEndpoint({ path: { competitionId: scope.competitionId, gameplayFactId: response.data.gameplayFactId } })
        if (current !== epoch || target.value.gameplayFactId !== response.data.gameplayFactId) return true
        if (fact.error || !fact.data) throw parseLiveSoloError(fact.error, message('challenges.fix.statusFailed'))
        status.value = fact.data
      }
      error.value = null
      return !factPending.value && !['Queued', 'Provisioning', 'Stopping'].includes(target.value.state ?? '')
    }
    catch (cause) { if (current === epoch) error.value = parseLiveSoloError(cause, message('liveSolo.error.load')).displayMessage; return false }
    finally { if (current === epoch) reading = false }
  }
  const poll = usePolling(refresh, { timeout: 4_500_000, maxInterval: 5000 })
  async function prepare() {
    if (!canPrepare.value) return
    const current = epoch, scope = path(); pending.value = true; error.value = null
    try {
      const headers = await verification.request('evaluation')
      if (!headers || current !== epoch || !props.canSubmit) return
      const response = await requestLiveSoloFixTarget({ path: scope, headers: { ...targetAttempt.headers(scope), ...headers } })
      if (current !== epoch) return
      if (response.error || !response.data) throw parseLiveSoloError(response.error, message('liveSolo.error.operation'))
      targetAttempt.completed(); target.value = response.data; status.value = null
      file.value = null; uploadKey.value++; poll.start(); emit('changed')
    }
    catch (cause) { if (current === epoch) error.value = parseLiveSoloError(cause, message('liveSolo.error.operation')).displayMessage }
    finally { if (current === epoch) pending.value = false }
  }
  function selectFile(event: Event) { if (!pending.value) file.value = (event.target as HTMLInputElement).files?.[0] ?? null }
  async function submit() {
    const selected = file.value, runtimeInstanceId = target.value?.runtimeInstanceId
    if (!canUpload.value || !selected || !runtimeInstanceId) return
    const current = epoch, scope = path(); pending.value = true; error.value = null
    try {
      const headers = await verification.request('evaluation')
      if (!headers || current !== epoch || !props.canSubmit) return
      const response = await submitLiveSoloFix({ path: { ...scope, runtimeInstanceId }, body: { file: selected },
        headers: { ...uploadAttempt.headers({ ...scope, runtimeInstanceId, name: selected.name, size: selected.size, modified: selected.lastModified }), ...headers },
        signal: AbortSignal.timeout(360_000) })
      if (current !== epoch) return
      if (response.error || !response.data?.gameplayFactId) throw parseLiveSoloError(response.error, message('challenges.error.fixUploadFailed'))
      uploadAttempt.completed(); file.value = null; uploadKey.value++; status.value = null
      target.value = { ...target.value, gameplayFactId: response.data.gameplayFactId, admissionSequence: response.data.admissionSequence }
      poll.start(); emit('changed')
    }
    catch (cause) { if (current === epoch) error.value = parseLiveSoloError(cause, message('challenges.error.fixUploadFailed')).displayMessage }
    finally { if (current === epoch) pending.value = false }
  }
  watch(() => [user.value?.userId, props.competitionId, props.matchId, props.roundId, props.question.id], () => {
    const current = ++epoch
    reading = false; poll.stop(); target.value = null; status.value = null; pending.value = false; file.value = null; error.value = null; uploadKey.value++
    targetAttempt.completed(); uploadAttempt.completed(); void refresh().then(done => { if (current === epoch && !done) poll.start() })
  }, { immediate: true })
  onScopeDispose(() => { epoch++; poll.stop() })
  return { ...toRefs(props), target, status, pending, error, uploadKey, ready, canPrepare, canUpload, unavailable, factPending, result,
    phaseLabel, prepare, selectFile, submit, retry: poll.start, timedOut: poll.timedOut }
}
export type LiveSoloFixSubmitViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloFixSubmit>>
