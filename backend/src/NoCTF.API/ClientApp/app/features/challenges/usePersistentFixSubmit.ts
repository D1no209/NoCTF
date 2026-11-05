import { computed, ref, toRefs, watch, onScopeDispose } from 'vue'
import { getGameplayFactStatusEndpoint, uploadPatchEndpoint } from '~/api'
import type { NoCtfapiEndpointsChallengesAvailableChallengeActionResponse as Action,
  NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse as Status } from '~/api'
import { createCommandAttempt } from '~/utils/command-attempt'
import { parseApiError } from '~/utils/api-error'
import { gameplayFactFailureCodeLabel, isGameplayFactPending } from '~/utils/labels'
import { isMessageKey, message, type UiMessage } from '~/utils/i18n'
import { toast } from '~/utils/message-toast'
import { useHumanVerification } from '~/features/security/useHumanVerification'

export function usePersistentFixSubmit(props: Readonly<{ competitionId: string; competitionChallengeId: string; action: Action }>,
  emit: { (event: 'changed'): void }) {
  const file = ref<File | null>(null), status = ref<Status | null>(null), factId = ref<string | null>(null)
  const pending = ref(false), error = ref<UiMessage | null>(null), uploadKey = ref(0)
  const attempt = createCommandAttempt(), verification = useHumanVerification()
  let epoch = 0, scope = ''
  const busy = computed(() => pending.value || !!factId.value && (!status.value || isGameplayFactPending(status.value.state)))
  const canUpload = computed(() => props.action.enabled === true && !!props.action.runtimeInstanceId && !!file.value && !busy.value)
  const phaseLabel = computed(() => {
    if (!status.value) return 'challenges.fix.phase.queued' as const
    switch (status.value.fixExecution?.phase) {
      case 'Applying': return 'challenges.fix.phase.applying'
      case 'Checking': return 'challenges.fix.phase.checking'
      case 'Finalizing': return 'challenges.fix.phase.finalizing'
      case 'Finished': return 'challenges.fix.phase.finished'
      default: return 'challenges.fix.phase.queued'
    }
  })
  const unavailable = computed<UiMessage | null>(() => {
    const key = props.action.admissionMessageKey
    if (props.action.availability === 'AdmissionRejected' && key && isMessageKey(key)) return message(key)
    switch (props.action.availability) {
      case 'TargetBusy': return message('challenges.fix.targetBusy')
      case 'RecoveryRequired': return message('challenges.fix.recoveryRequired')
      case 'TargetNotRunning': case 'TargetRequired': return message('challenges.fix.runtimeRequired')
      case 'AdmissionRejected': return message('challenges.fix.unavailable')
      default: return null
    }
  })
  async function refresh() {
    const id = factId.value, current = epoch
    if (!id) return true
    const response = await getGameplayFactStatusEndpoint({ path: { competitionId: props.competitionId, gameplayFactId: id } })
    if (current !== epoch || id !== factId.value) return true
    if (response.error || !response.data) {
      error.value = parseApiError(response.error, message('challenges.fix.statusFailed')).displayMessage
      throw response.error
    }
    status.value = response.data; error.value = null
    const finished = !isGameplayFactPending(response.data.state)
    if (finished) emit('changed')
    return finished
  }
  const poll = usePolling(refresh, { timeout: 4_500_000, maxInterval: 5000 })
  function selectFile(event: Event) { if (!busy.value) file.value = (event.target as HTMLInputElement).files?.[0] ?? null }
  async function submit() {
    const target = props.action.runtimeInstanceId, selected = file.value, current = epoch
    if (!canUpload.value || !target || !selected) return
    pending.value = true; error.value = null
    try {
      const verified = await verification.request('evaluation')
      if (verified === null || current !== epoch) return
      const response = await uploadPatchEndpoint({ path: { competitionId: props.competitionId,
        competitionChallengeId: props.competitionChallengeId, runtimeInstanceId: target }, body: { file: selected },
        headers: { ...attempt.headers({ target, name: selected.name, size: selected.size, modified: selected.lastModified }), ...verified },
        signal: AbortSignal.timeout(360_000) })
      if (current !== epoch) return
      if (response.error || !response.data?.gameplayFactId) {
        error.value = parseApiError(response.error, message('challenges.error.fixUploadFailed')).displayMessage
        return
      }
      attempt.completed(); file.value = null; uploadKey.value++
      factId.value = response.data.gameplayFactId; status.value = null
      poll.start(); emit('changed'); toast.success(message('challenges.fix.accepted'))
    }
    catch (cause) { if (current === epoch) error.value = parseApiError(cause, message('challenges.error.fixUploadFailed')).displayMessage }
    finally { if (current === epoch) pending.value = false }
  }
  watch(() => [props.competitionId, props.competitionChallengeId, props.action.runtimeInstanceId, props.action.gameplayFactId] as const, () => {
    const next = `${props.competitionId}:${props.competitionChallengeId}:${props.action.runtimeInstanceId ?? ''}`
    if (next !== scope) { scope = next; epoch++; poll.stop(); factId.value = null; status.value = null; file.value = null; pending.value = false; error.value = null; uploadKey.value++ }
    if (props.action.gameplayFactId && props.action.gameplayFactId !== factId.value) {
      factId.value = props.action.gameplayFactId; status.value = null; poll.start()
    }
  }, { immediate: true })
  onScopeDispose(() => { epoch++; poll.stop() })
  return { ...toRefs(props), file, status, pending, error, busy, canUpload, phaseLabel, unavailable, uploadKey,
    selectFile, submit, refresh, polling: poll.polling, timedOut: poll.timedOut, gameplayFactFailureCodeLabel }
}
export type PersistentFixSubmitViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof usePersistentFixSubmit>>
