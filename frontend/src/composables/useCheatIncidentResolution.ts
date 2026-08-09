import { computed, ref } from 'vue'
import { queryKeys } from '@/api/queryKeys'

export type CheatIncidentResolutionAction = 'dismiss' | 'confirm' | 'correct'

export interface CheatIncidentResolutionTarget {
  scoringEventId: string
  sourceTeamId?: string | null
  sourceTeamName?: string | null
}

export interface CheatIncidentResolutionRequest {
  action: CheatIncidentResolutionAction
  scoringEventId: string
  reason: string
}

interface CheatIncidentResolutionOptions {
  execute: (request: CheatIncidentResolutionRequest) => Promise<void>
  readError: (error: unknown) => string
  onSuccess?: (request: CheatIncidentResolutionRequest) => void
}

const minimumReasonLength = 8

interface QueryInvalidator {
  invalidateQueries: (options: { queryKey: readonly unknown[] }) => Promise<unknown>
}

export async function invalidateCheatIncidentResolutionQueries(
  queryClient: QueryInvalidator,
  competitionId: string,
  scoringEventId: string,
) {
  await Promise.all([
    queryClient.invalidateQueries({
      queryKey: queryKeys.adminCompetitionCheatIncidents(competitionId),
    }),
    queryClient.invalidateQueries({
      queryKey: queryKeys.adminCompetitionCheatIncident(competitionId, scoringEventId),
    }),
    queryClient.invalidateQueries({
      queryKey: queryKeys.adminCompetitionTeams(competitionId),
    }),
  ])
}

export function useCheatIncidentResolution(options: CheatIncidentResolutionOptions) {
  const isOpen = ref(false)
  const isSubmitting = ref(false)
  const action = ref<CheatIncidentResolutionAction | null>(null)
  const target = ref<CheatIncidentResolutionTarget | null>(null)
  const reason = ref('')
  const error = ref('')

  const normalizedReason = computed(() => reason.value.trim())
  const remainingCharacters = computed(() => Math.max(
    0,
    minimumReasonLength - normalizedReason.value.length,
  ))
  const canSubmit = computed(() => Boolean(
    action.value
    && target.value
    && remainingCharacters.value === 0
    && !isSubmitting.value,
  ))

  function clear() {
    isOpen.value = false
    action.value = null
    target.value = null
    reason.value = ''
    error.value = ''
  }

  function begin(
    nextAction: CheatIncidentResolutionAction,
    nextTarget: CheatIncidentResolutionTarget,
  ) {
    if (isSubmitting.value)
      return

    action.value = nextAction
    target.value = { ...nextTarget }
    reason.value = ''
    error.value = ''
    isOpen.value = true
  }

  function cancel() {
    if (!isSubmitting.value)
      clear()
  }

  function setOpen(value: boolean) {
    if (value) {
      isOpen.value = true
      return
    }
    cancel()
  }

  async function submit() {
    if (!canSubmit.value)
      return false

    const currentAction = action.value
    const currentTarget = target.value
    if (!currentAction || !currentTarget)
      return false

    const request: CheatIncidentResolutionRequest = {
      action: currentAction,
      scoringEventId: currentTarget.scoringEventId,
      reason: normalizedReason.value,
    }

    isSubmitting.value = true
    error.value = ''
    try {
      await options.execute(request)
      clear()
      options.onSuccess?.(request)
      return true
    }
    catch (submissionError) {
      error.value = options.readError(submissionError)
      return false
    }
    finally {
      isSubmitting.value = false
    }
  }

  return {
    action,
    begin,
    canSubmit,
    cancel,
    error,
    isOpen,
    isSubmitting,
    reason,
    remainingCharacters,
    setOpen,
    submit,
    target,
  }
}
