import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { markRaw } from 'vue'

import { toast } from '../../../../../utils/message-toast'
import { adminPatchCompetition } from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionConfigurationResponse } from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract } from '../../../../../api'
import type { NoCtfapiEndpointsCompetitionsRuntimeAccessModeProtocol } from '../../../../../api'

import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '../../../../../api'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import { shouldHydrateMetaField } from '../../../../../lib/pristine-meta'
import { normalizeConfigValue } from '~/features/admin/useConfigDraft'
import SingleWriteUpSettingsComponent from '~/features/writeups/SingleWriteUpSettings.vue'
import CompetitionModeConfigEditorComponent from '../../../../admin/CompetitionModeConfigEditor.vue'

const maximumWriteUpDeadlineHours = 24 * 365

/** Owns state, effects and commands for AdminCompetitionsByIdConfigurationPage. */
export function useAdminCompetitionsByIdConfigurationPage() {
  const { competitionId, competition, canWrite, refresh, accept, data: useData, read: readAdminCompetition } = useCompetitionAdmin()

  const title = ref('')

  const description = ref('')

  const startTime = ref('')

  const endTime = ref('')

  const teamRegistrationAutoApprove = ref(false)

  const allowTeamRegistrationWhileRunning = ref(false)

  const maxTeamMembers = ref(1)

  const maxConcurrentRuntimeInstancesPerTeam = ref(1)

  const runtimeAccessMode = ref<NoCtfapiEndpointsCompetitionsRuntimeAccessModeProtocol>('Direct')

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

  /** 最近一次与服务器对齐过的元数据,用来区分“用户没动过的字段”和“正在编辑的字段”。 */
  const hydratedMeta = ref<ReturnType<typeof currentMeta> | null>(null)

  function currentMeta() {
    return {
      title: title.value,
      description: description.value,
      startTime: startTime.value,
      endTime: endTime.value,
      teamRegistrationAutoApprove: teamRegistrationAutoApprove.value,
      allowTeamRegistrationWhileRunning: allowTeamRegistrationWhileRunning.value,
      maxTeamMembers: maxTeamMembers.value,
      maxConcurrentRuntimeInstancesPerTeam: maxConcurrentRuntimeInstancesPerTeam.value,
      runtimeAccessMode: runtimeAccessMode.value,
      trafficCaptureEnabled: trafficCaptureEnabled.value,
      trafficCaptureLimitMiB: trafficCaptureLimitMiB.value,
      maxActiveQuestionsPerTeam: maxActiveQuestionsPerTeam.value,
      maxParticipantMessagesBeforeHandlerReply: maxParticipantMessagesBeforeHandlerReply.value,
      allowChallengeOwnersToHandleQuestions: allowChallengeOwnersToHandleQuestions.value,
      practiceModeEnabled: practiceModeEnabled.value,
      writeUpSubmissionRequired: writeUpSubmissionRequired.value,
      writeUpSubmissionDeadlineHours: writeUpSubmissionDeadlineHours.value,
      staffOnly: staffOnly.value,
    }
  }

  /**
   * 实时刷新与保存回执都会重建 competition 对象。只回填用户没改动的字段,
   * 否则正在输入的描述会被服务器旧值覆盖(表现为字符丢失与光标跳动)。
   */
  function applyCompetitionMeta(c: NoCtfapiEndpointsCompetitionsCompetitionResponse, force = false): void {
    const next = {
      title: c.title ?? '',
      description: c.description ?? '',
      startTime: isoToLocalInput(c.startTime),
      endTime: isoToLocalInput(c.endTime),
      teamRegistrationAutoApprove: c.teamRegistrationAutoApprove ?? false,
      allowTeamRegistrationWhileRunning: c.allowTeamRegistrationWhileRunning ?? false,
      maxTeamMembers: c.maxTeamMembers ?? 1,
      maxConcurrentRuntimeInstancesPerTeam: c.maxConcurrentRuntimeInstancesPerTeam ?? 1,
      runtimeAccessMode: c.runtimeAccessMode ?? 'Direct',
      trafficCaptureEnabled: c.trafficCaptureEnabled ?? false,
      trafficCaptureLimitMiB: c.trafficCaptureLimitBytes == null
        ? null
        : c.trafficCaptureLimitBytes / 1_048_576,
      maxActiveQuestionsPerTeam: c.maxActiveQuestionsPerTeam ?? 5,
      maxParticipantMessagesBeforeHandlerReply: c.maxParticipantMessagesBeforeHandlerReply ?? 3,
      allowChallengeOwnersToHandleQuestions: c.allowChallengeOwnersToHandleQuestions ?? true,
      practiceModeEnabled: c.practiceModeEnabled ?? false,
      writeUpSubmissionRequired: c.writeUpSubmissionRequired ?? false,
      writeUpSubmissionDeadlineHours: c.writeUpSubmissionDeadlineHours ?? 0,
      staffOnly: c.accessMode === 'StaffOnly',
    }
    const previous = hydratedMeta.value
    const current = currentMeta()
    const write = (key: keyof typeof next) => shouldHydrateMetaField(previous, current, key, force)
    if (write('title')) title.value = next.title
    if (write('description')) description.value = next.description
    if (write('startTime')) startTime.value = next.startTime
    if (write('endTime')) endTime.value = next.endTime
    if (write('teamRegistrationAutoApprove')) teamRegistrationAutoApprove.value = next.teamRegistrationAutoApprove
    if (write('allowTeamRegistrationWhileRunning')) allowTeamRegistrationWhileRunning.value = next.allowTeamRegistrationWhileRunning
    if (write('maxTeamMembers')) maxTeamMembers.value = next.maxTeamMembers
    if (write('maxConcurrentRuntimeInstancesPerTeam')) maxConcurrentRuntimeInstancesPerTeam.value = next.maxConcurrentRuntimeInstancesPerTeam
    if (write('runtimeAccessMode')) runtimeAccessMode.value = next.runtimeAccessMode
    if (write('trafficCaptureEnabled')) trafficCaptureEnabled.value = next.trafficCaptureEnabled
    if (write('trafficCaptureLimitMiB')) trafficCaptureLimitMiB.value = next.trafficCaptureLimitMiB
    if (write('maxActiveQuestionsPerTeam')) maxActiveQuestionsPerTeam.value = next.maxActiveQuestionsPerTeam
    if (write('maxParticipantMessagesBeforeHandlerReply')) maxParticipantMessagesBeforeHandlerReply.value = next.maxParticipantMessagesBeforeHandlerReply
    if (write('allowChallengeOwnersToHandleQuestions')) allowChallengeOwnersToHandleQuestions.value = next.allowChallengeOwnersToHandleQuestions
    if (write('practiceModeEnabled')) practiceModeEnabled.value = next.practiceModeEnabled
    if (write('writeUpSubmissionRequired')) writeUpSubmissionRequired.value = next.writeUpSubmissionRequired
    if (write('writeUpSubmissionDeadlineHours')) writeUpSubmissionDeadlineHours.value = next.writeUpSubmissionDeadlineHours
    if (write('staffOnly')) staffOnly.value = next.staffOnly
    hydratedMeta.value = next
  }

  /** 当前表单基线所属的竞赛;切换竞赛时必须重新建立基线。 */
  const hydratedCompetitionId = ref('')

  watch(competition, (c) => {
    if (!c) return
    if (c.id !== hydratedCompetitionId.value) {
      hydratedCompetitionId.value = c.id ?? ''
      hydratedMeta.value = null
    }
    applyCompetitionMeta(c)
  }, { immediate: true })

  async function saveMeta() {
    if (!canWrite.value) return
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
    const submitted = currentMeta()
    try {
      const { data, error } = await adminPatchCompetition({
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
        },
      })
      if (error) throw error
      toast.success(describeMessage("administration.competitionsBy.label.basicInformationSaved"))
      if (data) accept(data)
      else await refresh()
      // 已提交的值成为新基线:未改动的字段继续跟随服务器,保存期间继续输入的字段保持不变。
      hydratedMeta.value = submitted
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
    try {
      const { data } = await readAdminCompetition({ path: { competitionId } })
      const next = data?.modeConfiguration ?? null
      // 只在内容变化时替换对象,避免刷新重置模式配置编辑器的未保存草稿。
      if (normalizeConfigValue(next) !== normalizeConfigValue(config.value)) config.value = next
    }
    finally { configLoading.value = false }
  }

  async function saveConfig(configuration: NoCtfapiEndpointsAdministrationCompetitionsCompetitionModeConfigurationContract, reason?: string) {
    if (!canWrite.value || !config.value) return
    savingConfig.value = true
    try {
      const { data, error } = await adminPatchCompetition({
        path: { competitionId },
        body: { modeConfiguration: { configuration, reason } },
      })
      if (error) throw error
      if (data) accept(data)
      toast.success(describeMessage("administration.label.modeConfigurationSaved"))
    }
    catch (e) {
      toastWriteError(e)
    }
    finally {
      savingConfig.value = false
    }
  }

  watch(() => useData.value?.modeConfiguration, value => {
    const next = value ?? null
    // 内容没变就不要替换对象:否则实时刷新会重置模式配置编辑器的未保存草稿。
    if (normalizeConfigValue(next) === normalizeConfigValue(config.value)) return
    config.value = next
  }, { immediate: true })
  onMounted(loadConfig)

  const CompetitionModeConfigEditor = markRaw(CompetitionModeConfigEditorComponent)

  return {
      competitionId, SingleWriteUpSettings: markRaw(SingleWriteUpSettingsComponent), refreshCompetition: refresh,
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
