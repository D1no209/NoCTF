import { markRaw } from 'vue'

import { toast } from 'vue-sonner'
import { adminGetCompetition, adminPatchCompetition } from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionConfigurationResponse } from '../../../../../api'

import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import CompetitionModeConfigEditorComponent from '../../../../admin/CompetitionModeConfigEditor.vue'

const maximumWriteUpDeadlineHours = 24 * 365

/** Owns state, effects and commands for AdminCompetitionsByIdConfigurationPage. */
export function useAdminCompetitionsByIdConfigurationPage() {
  const { competitionId, competition, canWrite, refresh } = useCompetitionAdmin()

  const title = ref('')

  const description = ref('')

  const startTime = ref('')

  const endTime = ref('')

  const teamRegistrationAutoApprove = ref(false)

  const allowTeamRegistrationWhileRunning = ref(false)

  const maxTeamMembers = ref(1)

  const maxConcurrentRuntimeInstancesPerTeam = ref(1)

  const maxActiveQuestionsPerTeam = ref(5)

  const maxParticipantMessagesBeforeHandlerReply = ref(3)

  const allowChallengeOwnersToHandleQuestions = ref(true)

  const practiceModeEnabled = ref(false)

  const writeUpSubmissionRequired = ref(false)

  const writeUpSubmissionDeadlineHours = ref(0)

  const staffOnly = ref(false)

  const savingMeta = ref(false)

  const metaError = ref<string | null>(null)

  watch(competition, (c) => {
    if (!c) return
    title.value = c.title ?? ''
    description.value = c.description ?? ''
    startTime.value = isoToLocalInput(c.startTime)
    endTime.value = isoToLocalInput(c.endTime)
    teamRegistrationAutoApprove.value = c.teamRegistrationAutoApprove ?? false
    allowTeamRegistrationWhileRunning.value = c.allowTeamRegistrationWhileRunning ?? false
    maxTeamMembers.value = c.maxTeamMembers ?? 1
    maxConcurrentRuntimeInstancesPerTeam.value = c.maxConcurrentRuntimeInstancesPerTeam ?? 1
    maxActiveQuestionsPerTeam.value = c.maxActiveQuestionsPerTeam ?? 5
    maxParticipantMessagesBeforeHandlerReply.value = c.maxParticipantMessagesBeforeHandlerReply ?? 3
    allowChallengeOwnersToHandleQuestions.value = c.allowChallengeOwnersToHandleQuestions ?? true
    practiceModeEnabled.value = c.practiceModeEnabled ?? false
    writeUpSubmissionRequired.value = c.writeUpSubmissionRequired ?? false
    writeUpSubmissionDeadlineHours.value = c.writeUpSubmissionDeadlineHours ?? 0
    staffOnly.value = c.accessMode === 'StaffOnly'
  }, { immediate: true })

  async function saveMeta() {
    metaError.value = null
    const start = localInputToIso(startTime.value)
    const end = localInputToIso(endTime.value)
    if (!title.value.trim() || !start || !end) {
      metaError.value = translate("ui.pleaseFillInTheTitleAndTimeCompletely")
      return
    }
    if (!Number.isInteger(writeUpSubmissionDeadlineHours.value)
      || writeUpSubmissionDeadlineHours.value < 0
      || writeUpSubmissionDeadlineHours.value > maximumWriteUpDeadlineHours) {
      metaError.value = translate('writeUp.invalidDeadlineHours', {
        maximum: maximumWriteUpDeadlineHours,
      })
      return
    }
    savingMeta.value = true
    try {
      const { error } = await adminPatchCompetition({
        path: { competitionId },
        body: {
          metadata: {
            title: title.value.trim(),
            description: description.value.trim() || null,
            startTime: start,
            endTime: end,
            teamRegistrationAutoApprove: teamRegistrationAutoApprove.value,
            allowTeamRegistrationWhileRunning: allowTeamRegistrationWhileRunning.value,
            maxTeamMembers: maxTeamMembers.value,
            maxConcurrentRuntimeInstancesPerTeam: maxConcurrentRuntimeInstancesPerTeam.value,
            maxActiveQuestionsPerTeam: maxActiveQuestionsPerTeam.value,
            maxParticipantMessagesBeforeHandlerReply: maxParticipantMessagesBeforeHandlerReply.value,
            allowChallengeOwnersToHandleQuestions: allowChallengeOwnersToHandleQuestions.value,
            practiceModeEnabled: practiceModeEnabled.value,
            writeUpSubmissionRequired: writeUpSubmissionRequired.value,
            writeUpSubmissionDeadlineHours: writeUpSubmissionDeadlineHours.value,
            accessMode: staffOnly.value ? 'StaffOnly' : 'Public',
          },
        },
      })
      if (error) throw error
      toast.success(translate("ui.basicInformationHasBeenSaved"))
      await refresh()
    }
    catch (e) {
      toastWriteError(e)
    }
    finally {
      savingMeta.value = false
    }
  }

  const config = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionConfigurationResponse | null>(null)

  const configLoading = ref(true)

  const savingConfig = ref(false)

  async function loadConfig() {
    configLoading.value = true
    const { data, error } = await adminGetCompetition({ path: { competitionId } })
    if (!error && data) config.value = data.modeConfiguration ?? null
    configLoading.value = false
  }

  async function saveConfig(json: string) {
    if (!config.value) return
    savingConfig.value = true
    try {
      const { data, error } = await adminPatchCompetition({
        path: { competitionId },
        body: { modeConfiguration: { json } },
      })
      if (error) throw error
      config.value = data?.modeConfiguration ?? config.value
      toast.success(translate("ui.modeConfigurationSaved"))
    }
    catch (e) {
      toastWriteError(e)
    }
    finally {
      savingConfig.value = false
    }
  }

  onMounted(loadConfig)

  const CompetitionModeConfigEditor = markRaw(CompetitionModeConfigEditorComponent)

  return {
      competition,
      canWrite,
      title,
      description,
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
      writeUpSubmissionRequired,
      writeUpSubmissionDeadlineHours,
      maximumWriteUpDeadlineHours,
      staffOnly,
      savingMeta,
      metaError,
      saveMeta,
      config,
      configLoading,
      savingConfig,
      saveConfig,
      CompetitionModeConfigEditor
    }
}

export type AdminCompetitionsByIdConfigurationPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdConfigurationPage>>>
