import type {
  AcceptedFlagSubmission,
  PublicSubmissionStatus,
} from '@/api/submissionPresentation'
import { useQueryClient } from '@tanstack/vue-query'
import { onScopeDispose, ref, shallowRef } from 'vue'
import { submissionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { isSubmissionTerminal } from '@/api/submissionPresentation'

function waitForNextPoll(signal: AbortSignal) {
  return new Promise<void>((resolve, reject) => {
    if (signal.aborted) {
      reject(signal.reason)
      return
    }

    let timer: ReturnType<typeof setTimeout>
    const onAbort = () => {
      clearTimeout(timer)
      reject(signal.reason)
    }
    timer = setTimeout(() => {
      signal.removeEventListener('abort', onAbort)
      resolve()
    }, 1_000)
    signal.addEventListener('abort', onAbort, { once: true })
  })
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === 'AbortError'
}

export function useFlagSubmission() {
  const queryClient = useQueryClient()
  const accepted = shallowRef<AcceptedFlagSubmission | null>(null)
  const status = shallowRef<PublicSubmissionStatus | null>(null)
  const isSubmitting = ref(false)
  let controller: AbortController | null = null
  let operation = 0

  function cancel() {
    operation += 1
    controller?.abort()
    controller = null
    isSubmitting.value = false
    accepted.value = null
    status.value = null
  }

  async function submit(
    competitionId: string,
    competitionChallengeId: string,
    flag: string,
  ): Promise<PublicSubmissionStatus | null> {
    cancel()
    const currentOperation = operation
    const currentController = new AbortController()
    controller = currentController
    isSubmitting.value = true

    try {
      const acceptedResponse = await submissionApi.submitFlag(
        competitionId,
        competitionChallengeId,
        flag,
        currentController.signal,
      )
      accepted.value = acceptedResponse

      while (!currentController.signal.aborted) {
        await waitForNextPoll(currentController.signal)
        const nextStatus = await submissionApi.getStatus(
          competitionId,
          acceptedResponse.submissionId,
          currentController.signal,
        )
        queryClient.setQueryData(
          queryKeys.submission(competitionId, nextStatus.submissionId),
          nextStatus,
        )
        status.value = nextStatus

        if (isSubmissionTerminal(nextStatus))
          return nextStatus
      }
    }
    catch (error) {
      if (currentController.signal.aborted || isAbortError(error))
        return null
      throw error
    }
    finally {
      if (operation === currentOperation) {
        controller = null
        isSubmitting.value = false
      }
    }

    return null
  }

  onScopeDispose(cancel)

  return {
    accepted,
    status,
    isSubmitting,
    submit,
    cancel,
  }
}
