import type {
  CheatIncidentResolutionRequest,
  CheatIncidentResolutionTarget,
} from '../src/composables/useCheatIncidentResolution'
import { describe, expect, test } from 'bun:test'
import { readCheatIncidentResolutionError } from '../src/api/cheatIncidentApi'
import { ApiError } from '../src/api/noctf'
import {
  invalidateCheatIncidentResolutionQueries,
  useCheatIncidentResolution,
} from '../src/composables/useCheatIncidentResolution'

const target: CheatIncidentResolutionTarget = {
  scoringEventId: 'incident-1',
  sourceTeamId: 'team-1',
  sourceTeamName: 'Source Team',
}

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, reject, resolve }
}

describe('useCheatIncidentResolution', () => {
  test('prefers readable backend validation details over a generic fallback', () => {
    expect(readCheatIncidentResolutionError(
      new ApiError('request failed', 400, {
        title: 'Validation failed',
        errors: { Reason: ['The reason must contain at least 8 characters.'] },
      }),
      'fallback',
    )).toBe('The reason must contain at least 8 characters.')

    expect(readCheatIncidentResolutionError(
      new ApiError('request failed', 409, { detail: 'The incident was already resolved.' }),
      'fallback',
    )).toBe('The incident was already resolved.')
    expect(readCheatIncidentResolutionError(new Error('network failed'), 'fallback'))
      .toBe('network failed')
  })

  test('opens the matching dismiss and confirm dialogs', () => {
    const state = useCheatIncidentResolution({
      execute: async () => {},
      readError: () => 'failed',
    })

    state.begin('dismiss', target)
    expect(state.isOpen.value).toBe(true)
    expect(state.action.value).toBe('dismiss')
    expect(state.target.value).toEqual(target)

    state.cancel()
    state.begin('confirm', target)
    expect(state.isOpen.value).toBe(true)
    expect(state.action.value).toBe('confirm')
    expect(state.target.value).toEqual(target)
  })

  test('reports short reasons and does not send a request', async () => {
    const requests: CheatIncidentResolutionRequest[] = []
    const state = useCheatIncidentResolution({
      execute: async (request) => { requests.push(request) },
      readError: () => 'failed',
    })

    state.begin('dismiss', target)
    state.reason.value = 'short'

    expect(state.remainingCharacters.value).toBe(3)
    expect(state.canSubmit.value).toBe(false)
    expect(await state.submit()).toBe(false)
    expect(requests).toHaveLength(0)
  })

  test('dismiss sends exactly one trimmed request and clears successful state', async () => {
    const requests: CheatIncidentResolutionRequest[] = []
    const completed: CheatIncidentResolutionRequest[] = []
    const state = useCheatIncidentResolution({
      execute: async (request) => { requests.push(request) },
      onSuccess: (request) => { completed.push(request) },
      readError: () => 'failed',
    })

    state.begin('dismiss', target)
    state.reason.value = '  reviewed evidence  '

    expect(await state.submit()).toBe(true)
    expect(requests).toEqual([{
      action: 'dismiss',
      reason: 'reviewed evidence',
      scoringEventId: 'incident-1',
    }])
    expect(completed).toEqual(requests)
    expect(state.isOpen.value).toBe(false)
    expect(state.action.value).toBeNull()
    expect(state.target.value).toBeNull()
    expect(state.reason.value).toBe('')
  })

  test('successful resolution invalidates the list, detail, and team queries', async () => {
    const invalidated: unknown[] = []
    const queryClient = {
      invalidateQueries: async ({ queryKey }: { queryKey: unknown }) => {
        invalidated.push(queryKey)
      },
    }

    await invalidateCheatIncidentResolutionQueries(
      queryClient,
      'competition-1',
      'incident-1',
    )

    expect(invalidated).toEqual([
      ['admin-competition-cheat-incidents', 'competition-1'],
      ['admin-competition-cheat-incident', 'competition-1', 'incident-1'],
      ['admin-competition-teams', 'competition-1'],
    ])
  })

  test('confirm sends exactly one request with the incident id and reason', async () => {
    const requests: CheatIncidentResolutionRequest[] = []
    const state = useCheatIncidentResolution({
      execute: async (request) => { requests.push(request) },
      readError: () => 'failed',
    })

    state.begin('confirm', target)
    state.reason.value = 'confirmed cross-team flag'

    expect(await state.submit()).toBe(true)
    expect(requests).toEqual([{
      action: 'confirm',
      reason: 'confirmed cross-team flag',
      scoringEventId: 'incident-1',
    }])
  })

  test('ignores duplicate submission and closing while a request is pending', async () => {
    const pending = deferred<void>()
    const requests: CheatIncidentResolutionRequest[] = []
    const state = useCheatIncidentResolution({
      execute: async (request) => {
        requests.push(request)
        await pending.promise
      },
      readError: () => 'failed',
    })

    state.begin('confirm', target)
    state.reason.value = 'confirmed cross-team flag'
    const first = state.submit()
    const duplicate = state.submit()
    state.setOpen(false)

    expect(state.isSubmitting.value).toBe(true)
    expect(state.isOpen.value).toBe(true)
    expect(requests).toHaveLength(1)
    expect(await duplicate).toBe(false)

    pending.resolve()
    expect(await first).toBe(true)
    expect(requests).toHaveLength(1)
  })

  test('retains the dialog and input while exposing a readable request error', async () => {
    const state = useCheatIncidentResolution({
      execute: async () => { throw new Error('backend conflict') },
      readError: error => error instanceof Error ? error.message : 'failed',
    })

    state.begin('dismiss', target)
    state.reason.value = 'reviewed evidence'

    expect(await state.submit()).toBe(false)
    expect(state.isOpen.value).toBe(true)
    expect(state.action.value).toBe('dismiss')
    expect(state.reason.value).toBe('reviewed evidence')
    expect(state.error.value).toBe('backend conflict')
    expect(state.isSubmitting.value).toBe(false)
  })

  test('cancel and close reset state before the next action', () => {
    const state = useCheatIncidentResolution({
      execute: async () => {},
      readError: () => 'failed',
    })

    state.begin('dismiss', target)
    state.reason.value = 'draft reason'
    state.cancel()

    expect(state.isOpen.value).toBe(false)
    expect(state.action.value).toBeNull()
    expect(state.reason.value).toBe('')

    state.begin('confirm', target)
    expect(state.action.value).toBe('confirm')
    expect(state.reason.value).toBe('')
    expect(state.error.value).toBe('')

    state.setOpen(false)
    expect(state.isOpen.value).toBe(false)
    expect(state.target.value).toBeNull()
  })
})
