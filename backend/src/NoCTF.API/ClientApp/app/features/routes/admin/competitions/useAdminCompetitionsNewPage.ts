

import { toast } from 'vue-sonner'
import { adminCreateCompetition } from '../../../../api'
import type { NoCtfapiEndpointsCompetitionsGameModeProtocol } from '../../../../api'

/** Owns state, effects and commands for AdminCompetitionsNewPage. */
export function useAdminCompetitionsNewPage() {
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

  async function submit() {
    error.value = null
    if (!title.value.trim()) {
      error.value = translate("ui.pleaseEnterAContestTitle")
      return
    }
    const start = localInputToIso(startTime.value)
    const end = localInputToIso(endTime.value)
    if (!start || !end) {
      error.value = translate("ui.pleaseSelectStartAndEndTime")
      return
    }
    if (new Date(start) >= new Date(end)) {
      error.value = translate("ui.startTimeMustBeEarlierThanEndTime")
      return
    }
    pending.value = true
    try {
      const { data, error: e } = await adminCreateCompetition({
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
      if (e || !data) throw e
      toast.success(translate("ui.contestCreated"))
      await navigateTo(`/admin/competitions/${data.id}`)
    }
    catch (e) {
      error.value = parseApiError(e).message
    }
    finally {
      pending.value = false
    }
  }

  return {
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
      submit
    }
}

export type AdminCompetitionsNewPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsNewPage>>>
