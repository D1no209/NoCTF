
import { api } from '../lib/api'
import type { UiMessage } from '../utils/i18n'

import { computed, ref } from 'vue'

export type CheatIncidentResolutionAction = 'confirm' | 'dismiss' | 'correct'

export interface CheatIncidentResolutionTarget {
  gameplayFactId: string
  sourceTeamId?: string | null
  sourceTeamName?: string | null
}

export interface CheatIncidentResolutionRequest {
  action: CheatIncidentResolutionAction
  gameplayFactId: string
  reason: string
}

interface CheatIncidentResolutionOptions {
  competitionId: string
  readError: (error: unknown) => UiMessage
  onSuccess?: (request: CheatIncidentResolutionRequest) => void
  clients?: CheatIncidentResolutionClients
}

interface CheatIncidentResolutionInput {
  path: { competitionId: string; gameplayFactId: string }
  body: { status: 'Confirmed' | 'Dismissed' | 'Corrected'; reason: string }
}

interface CheatIncidentResolutionClients {
  confirm: (input: CheatIncidentResolutionInput) => Promise<unknown>
  correct: (input: CheatIncidentResolutionInput) => Promise<unknown>
  dismiss: (input: CheatIncidentResolutionInput) => Promise<unknown>
}

const updateResolution = (input: CheatIncidentResolutionInput) => api.api.v1.admin.competitions
  .byCompetitionId(input.path.competitionId).cheatIncidents.byGameplayFactId(input.path.gameplayFactId)
  .status.put(input.body)

const clients: CheatIncidentResolutionClients = {
  confirm: updateResolution, correct: updateResolution, dismiss: updateResolution,
}

export const cheatIncidentResolutionMinimumReasonLength = 8

async function executeResolution(
  sdk: CheatIncidentResolutionClients,
  competitionId: string,
  request: CheatIncidentResolutionRequest,
) {
  const status: 'Confirmed' | 'Dismissed' | 'Corrected' = request.action === 'confirm'
    ? 'Confirmed'
    : request.action === 'dismiss' ? 'Dismissed' : 'Corrected'
  const options = {
    path: {
      competitionId,
      gameplayFactId: request.gameplayFactId,
    },
    body: {
      status,
      reason: request.reason,
    },
  }

  await (request.action === 'confirm'
    ? sdk.confirm(options)
    : request.action === 'dismiss'
      ? sdk.dismiss(options)
      : sdk.correct(options))

}

export function useCheatIncidentResolution(options: CheatIncidentResolutionOptions) {
  const sdk = options.clients ?? clients
  const isOpen = ref(false)
  const isSubmitting = ref(false)
  const action = ref<CheatIncidentResolutionAction | null>(null)
  const target = ref<CheatIncidentResolutionTarget | null>(null)
  const reason = ref('')
  const error = ref<UiMessage>('')

  const normalizedReason = computed(() => reason.value.trim())
  const remainingCharacters = computed(() => Math.max(
    0,
    cheatIncidentResolutionMinimumReasonLength - normalizedReason.value.length,
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
      gameplayFactId: currentTarget.gameplayFactId,
      reason: normalizedReason.value,
    }

    isSubmitting.value = true
    error.value = ''
    try {
      await executeResolution(sdk, options.competitionId, request)
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
