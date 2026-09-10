import { toRefs } from 'vue'
import { toast } from 'vue-sonner'
import { adminCreateCompetition } from '../../api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse, NoCtfapiEndpointsCompetitionsGameModeProtocol } from '../../api'

type Events = {
  'update:open': [value: boolean]
  created: [competition: NoCtfapiEndpointsCompetitionsCompetitionResponse]
}

/** Owns the create-competition draft and commands used by the competition browser dialog. */
export function useCreateCompetitionDialog(
  props: Readonly<{ open: boolean }>,
  emit: { <K extends keyof Events>(event: K, ...args: Events[K]): void },
) {
  const { canOrganize } = useAuth()
  const title = ref('')
  const description = ref('')
  const mode = ref<NoCtfapiEndpointsCompetitionsGameModeProtocol>('Ctf')
  const startTime = ref('')
  const endTime = ref('')
  const teamRegistrationAutoApprove = ref(false)
  const allowTeamRegistrationWhileRunning = ref(false)
  const maxTeamMembers = ref(5)
  const maxConcurrentRuntimeInstancesPerTeam = ref(1)
  const maxActiveQuestionsPerTeam = ref(5)
  const maxParticipantMessagesBeforeHandlerReply = ref(3)
  const allowChallengeOwnersToHandleQuestions = ref(true)
  const practiceModeEnabled = ref(false)
  const error = ref<string | null>(null)
  const pending = ref(false)
  let resetBeforeNextOpen = false

  function reset() {
    title.value = ''
    description.value = ''
    mode.value = 'Ctf'
    startTime.value = ''
    endTime.value = ''
    teamRegistrationAutoApprove.value = false
    allowTeamRegistrationWhileRunning.value = false
    maxTeamMembers.value = 5
    maxConcurrentRuntimeInstancesPerTeam.value = 1
    maxActiveQuestionsPerTeam.value = 5
    maxParticipantMessagesBeforeHandlerReply.value = 3
    allowChallengeOwnersToHandleQuestions.value = true
    practiceModeEnabled.value = false
    error.value = null
  }

  function setOpen(value: boolean) {
    if (!value && pending.value) return
    if (value && resetBeforeNextOpen) {
      reset()
      resetBeforeNextOpen = false
    }
    if (!value) resetBeforeNextOpen = true
    emit('update:open', value)
  }

  async function submit() {
    error.value = null
    if (!title.value.trim()) {
      error.value = translate('ui.pleaseEnterAContestTitle')
      return
    }
    const start = localInputToIso(startTime.value)
    const end = localInputToIso(endTime.value)
    if (!start || !end) {
      error.value = translate('ui.pleaseSelectStartAndEndTime')
      return
    }
    if (new Date(start) >= new Date(end)) {
      error.value = translate('ui.startTimeMustBeEarlierThanEndTime')
      return
    }
    pending.value = true
    try {
      const { data, error: requestError } = await adminCreateCompetition({
        body: {
          title: title.value.trim(),
          description: description.value.trim() || null,
          mode: mode.value,
          startTime: start,
          endTime: end,
          teamRegistrationAutoApprove: teamRegistrationAutoApprove.value,
          allowTeamRegistrationWhileRunning: allowTeamRegistrationWhileRunning.value,
          maxTeamMembers: maxTeamMembers.value,
          maxConcurrentRuntimeInstancesPerTeam: maxConcurrentRuntimeInstancesPerTeam.value,
          maxActiveQuestionsPerTeam: maxActiveQuestionsPerTeam.value,
          maxParticipantMessagesBeforeHandlerReply: maxParticipantMessagesBeforeHandlerReply.value,
          allowChallengeOwnersToHandleQuestions: allowChallengeOwnersToHandleQuestions.value,
          practiceModeEnabled: mode.value === 'Ctf' && practiceModeEnabled.value,
        },
      })
      if (requestError || !data) throw requestError
      toast.success(translate('ui.contestCreated'))
      emit('created', data)
      pending.value = false
      setOpen(false)
    }
    catch (requestError) {
      error.value = parseApiError(requestError).message
    }
    finally {
      pending.value = false
    }
  }

  return {
    ...toRefs(props),
    canOrganize,
    title,
    description,
    mode,
    startTime,
    endTime,
    teamRegistrationAutoApprove,
    allowTeamRegistrationWhileRunning,
    maxTeamMembers,
    maxConcurrentRuntimeInstancesPerTeam,
    maxActiveQuestionsPerTeam,
    maxParticipantMessagesBeforeHandlerReply,
    allowChallengeOwnersToHandleQuestions,
    practiceModeEnabled,
    error,
    pending,
    setOpen,
    submit,
  }
}

export type CreateCompetitionDialogViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useCreateCompetitionDialog>>
