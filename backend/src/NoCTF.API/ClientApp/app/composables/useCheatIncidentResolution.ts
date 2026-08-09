import {
  adminConfirmCheatIncident,
  adminCorrectCheatIncident,
  adminDismissCheatIncident,
} from '../api'
import { computed, ref } from 'vue'

export type CheatIncidentResolutionAction = 'confirm' | 'dismiss' | 'correct'

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
  competitionId: string
  readError: (error: unknown) => string
  onSuccess?: (request: CheatIncidentResolutionRequest) => void
  clients?: CheatIncidentResolutionClients
}

interface CheatIncidentResolutionSdkResult {
  data?: unknown
  error?: unknown
  request?: Request
  response?: Response
}

interface CheatIncidentResolutionClients {
  confirm: (options: Parameters<typeof adminConfirmCheatIncident>[0]) => Promise<CheatIncidentResolutionSdkResult>
  correct: (options: Parameters<typeof adminCorrectCheatIncident>[0]) => Promise<CheatIncidentResolutionSdkResult>
  dismiss: (options: Parameters<typeof adminDismissCheatIncident>[0]) => Promise<CheatIncidentResolutionSdkResult>
}

const clients: CheatIncidentResolutionClients = {
  confirm: async options => adminConfirmCheatIncident(options),
  correct: async options => adminCorrectCheatIncident(options),
  dismiss: async options => adminDismissCheatIncident(options),
}

export const cheatIncidentResolutionMinimumReasonLength = 8

async function executeResolution(
  sdk: CheatIncidentResolutionClients,
  competitionId: string,
  request: CheatIncidentResolutionRequest,
) {
  const options = {
    path: {
      competitionId,
      scoringEventId: request.scoringEventId,
    },
    body: { reason: request.reason },
  }

  const { error } = request.action === 'confirm'
    ? await sdk.confirm(options)
    : request.action === 'dismiss'
      ? await sdk.dismiss(options)
      : await sdk.correct(options)

  if (error)
    throw error
}

export function useCheatIncidentResolution(options: CheatIncidentResolutionOptions) {
  const sdk = options.clients ?? clients
  const isOpen = ref(false)
  const isSubmitting = ref(false)
  const action = ref<CheatIncidentResolutionAction | null>(null)
  const target = ref<CheatIncidentResolutionTarget | null>(null)
  const reason = ref('')
  const error = ref('')

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
      scoringEventId: currentTarget.scoringEventId,
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
