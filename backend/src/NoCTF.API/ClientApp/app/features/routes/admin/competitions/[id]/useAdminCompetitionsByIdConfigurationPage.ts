import { dateObject } from '../../../../../utils/date-value'

import { api } from '../../../../../lib/api'
import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { markRaw } from 'vue'

import { toast } from '../../../../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationCompetitionsCompetitionConfigurationResponse } from '../../../../../api/models'
import type { NoCTFAPIEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract } from '../../../../../api/models'
import type { NoCTFAPIEndpointsCompetitionsRuntimeAccessModeProtocol } from '../../../../../api/models'

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

  const runtimeAccessMode = ref<NoCTFAPIEndpointsCompetitionsRuntimeAccessModeProtocol>('Direct')

  const trafficCaptureEnabled = ref(false)

  const trafficCaptureLimitMiB = ref<number | null>(null)

  const trafficCaptureHasDirectBypass = computed(() =>
    trafficCaptureEnabled.value && runtimeAccessMode.value === 'DirectAndWsrx')

  watch(runtimeAccessMode, (mode) => {
    if (mode === 'Direct') trafficCaptureEnabled.value = false
  })

  const maxActiveQuestionsPerTeam = ref(5)

  const maxParticipantMessagesBeforeHandlerReply = ref(3)

  const allowChallengeOwnersToHandleQuestions = ref(true)

  const practiceModeEnabled = ref(false)

  const writeUpSubmissionRequired = ref(false)

  const writeUpSubmissionDeadlineHours = ref(0)

  const staffOnly = ref(false)

  const savingMeta = ref(false)

  const metaError = ref<UiMessage | null>(null)

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
    runtimeAccessMode.value = c.runtimeAccessMode ?? 'Direct'
    trafficCaptureEnabled.value = c.trafficCaptureEnabled ?? false
    trafficCaptureLimitMiB.value = c.trafficCaptureLimitBytes == null
      ? null
      : c.trafficCaptureLimitBytes / 1_048_576
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
      metaError.value = describeMessage("administration.competitionsBy.description.fillTitleTimeCompletely")
      return
    }
    if (!Number.isInteger(writeUpSubmissionDeadlineHours.value)
      || writeUpSubmissionDeadlineHours.value < 0
      || writeUpSubmissionDeadlineHours.value > maximumWriteUpDeadlineHours) {
      metaError.value = describeMessage('writeUp.invalidDeadlineHours', {
        maximum: maximumWriteUpDeadlineHours,
      })
      return
    }
    if (trafficCaptureEnabled.value && runtimeAccessMode.value === 'Direct') {
      metaError.value = describeMessage('runtime.captureRequiresWsrx')
      return
    }
    if (trafficCaptureLimitMiB.value !== null
      && (!Number.isInteger(trafficCaptureLimitMiB.value)
        || trafficCaptureLimitMiB.value < 1
        || trafficCaptureLimitMiB.value > 4096)) {
      metaError.value = describeMessage('runtime.captureLimitInvalid')
      return
    }
    savingMeta.value = true
    try {
      await api.api.v1.admin.competitions.byCompetitionId(competitionId).patch({
          metadata: {
            title: title.value.trim(),
            description: description.value.trim() || null,
            startTime: dateObject(start ?? undefined),
            endTime: dateObject(end ?? undefined),
            teamRegistrationAutoApprove: teamRegistrationAutoApprove.value,
            allowTeamRegistrationWhileRunning: allowTeamRegistrationWhileRunning.value,
            maxTeamMembers: maxTeamMembers.value,
            maxConcurrentRuntimeInstancesPerTeam: maxConcurrentRuntimeInstancesPerTeam.value,
            runtimeAccessMode: runtimeAccessMode.value,
            trafficCaptureEnabled: trafficCaptureEnabled.value,
            trafficCaptureLimitBytes: trafficCaptureLimitMiB.value === null
              ? null
              : trafficCaptureLimitMiB.value * 1_048_576,
            maxActiveQuestionsPerTeam: maxActiveQuestionsPerTeam.value,
            maxParticipantMessagesBeforeHandlerReply: maxParticipantMessagesBeforeHandlerReply.value,
            allowChallengeOwnersToHandleQuestions: allowChallengeOwnersToHandleQuestions.value,
            practiceModeEnabled: practiceModeEnabled.value,
            writeUpSubmissionRequired: writeUpSubmissionRequired.value,
            writeUpSubmissionDeadlineHours: writeUpSubmissionDeadlineHours.value,
            accessMode: staffOnly.value ? 'StaffOnly' : 'Public',
          },
        });
      toast.success(describeMessage("administration.competitionsBy.label.basicInformationSaved"))
      await refresh()
    }
    catch (e) {
      toastWriteError(e)
    }
    finally {
      savingMeta.value = false
    }
  }

  const config = ref<NoCTFAPIEndpointsAdministrationCompetitionsCompetitionConfigurationResponse | null>(null)

  const configLoading = ref(true)

  const savingConfig = ref(false)

  async function loadConfig() {
    configLoading.value = true
    let error: unknown;
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).get().catch(cause => { error = cause; return undefined });
    if (!error && data) config.value = data.modeConfiguration ?? null
    configLoading.value = false
  }

  async function saveConfig(configuration: NoCTFAPIEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract) {
    if (!config.value) return
    savingConfig.value = true
    try {

      const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).patch({ modeConfiguration: { configuration } });
      config.value = data?.modeConfiguration ?? config.value
      toast.success(describeMessage("administration.label.modeConfigurationSaved"))
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
      runtimeAccessMode,
      trafficCaptureEnabled,
      trafficCaptureLimitMiB,
      trafficCaptureHasDirectBypass,
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
