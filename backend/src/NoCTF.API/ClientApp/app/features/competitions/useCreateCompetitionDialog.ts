import { message as describeMessage } from '../../utils/i18n'
import type { UiMessage } from '../../utils/i18n'
import { toRefs } from 'vue'
import { gameModeOptions } from '../../utils/game-modes'
import { toast } from '../../utils/message-toast'
import { adminCompetitionPosterReplace, adminCreateCompetition } from '../../api'
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
  const staffOnly = ref(false)
  const posterFile = ref<File | null>(null)
  const posterError = ref<UiMessage | null>(null)
  const posterInputKey = ref(0)
  const createdCompetition = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>(null)
  const error = ref<UiMessage | null>(null)
  const pending = ref(false)
  let resetBeforeNextOpen = false

  const submitLabel = computed(() => {
    if (!createdCompetition.value) return translate('competitions.label.createContest')
    return posterFile.value
      ? translate('createCompetition.retryPosterUpload')
      : translate('createCompetition.finishCreation')
  })

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
    staffOnly.value = false
    posterFile.value = null
    posterError.value = null
    posterInputKey.value += 1
    createdCompetition.value = null
    error.value = null
  }

  function completeCreation() {
    const competition = createdCompetition.value
    if (!competition) return
    createdCompetition.value = null
    toast.success(describeMessage('common.label.contestCreated'))
    emit('created', competition)
  }

  function setOpen(value: boolean) {
    if (!value && pending.value) return
    if (value && resetBeforeNextOpen) {
      reset()
      resetBeforeNextOpen = false
    }
    if (!value) {
      completeCreation()
      resetBeforeNextOpen = true
    }
    emit('update:open', value)
  }

  function selectPoster(event: Event) {
    const input = event.target as HTMLInputElement
    const file = input.files?.[0] ?? null
    posterFile.value = null
    posterError.value = null
    if (!file) return
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
      posterError.value = describeMessage('common.createCompetition.validation.posterJpegFormat')
      return
    }
    posterFile.value = file
  }

  function posterUploadError(requestError: unknown): string {
    const parsed = parseApiError(requestError)
    const message = parsed.code === 'UploadTooLarge'
      ? translate('common.error.uploadTooLarge')
      : ['SizeInvalid', 'SourceMetadataMismatch', 'UnsupportedFormat', 'InvalidDimensions', 'PixelLimitExceeded', 'MultipleFrames', 'MalformedImage'].includes(parsed.code ?? '')
        ? translate('common.createCompetition.validation.posterJpegFormat')
        : parsed.displayMessage
    return translate('createCompetition.posterUploadFailed', { message })
  }

  async function submit() {
    if (pending.value || posterError.value) return
    error.value = null
    pending.value = true
    try {
      if (!createdCompetition.value) {
        if (!title.value.trim()) {
          error.value = describeMessage('competitions.createCompetition.label.enterContestTitle')
          return
        }
        const start = localInputToIso(startTime.value)
        const end = localInputToIso(endTime.value)
        if (!start || !end) {
          error.value = describeMessage('competitions.createCompetition.description.selectStartEndTime')
          return
        }
        if (new Date(start) >= new Date(end)) {
          error.value = describeMessage('competitions.createCompetition.validation.startTimeFormat')
          return
        }
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
            accessMode: staffOnly.value ? 'StaffOnly' : 'Public',
          },
        })
        if (requestError || !data) throw requestError
        createdCompetition.value = data
      }

      if (posterFile.value && createdCompetition.value.id) {
        const { data: uploadedPoster, error: uploadError } = await adminCompetitionPosterReplace({
          path: { competitionId: createdCompetition.value.id },
          body: { file: posterFile.value },
        })
        if (uploadError || !uploadedPoster?.url) {
          error.value = posterUploadError(uploadError)
          return
        }
        createdCompetition.value = {
          ...createdCompetition.value,
          posterUrl: uploadedPoster.url,
        }
      }

      completeCreation()
      pending.value = false
      setOpen(false)
    }
    catch (requestError) {
      error.value = parseApiError(requestError).displayMessage
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
    modeOptions: gameModeOptions,
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
    staffOnly,
    posterFile,
    posterError,
    posterInputKey,
    submitLabel,
    error,
    pending,
    selectPoster,
    setOpen,
    submit,
  }
}

export type CreateCompetitionDialogViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useCreateCompetitionDialog>>
