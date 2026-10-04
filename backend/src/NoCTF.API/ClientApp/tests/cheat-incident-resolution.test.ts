import { sourceFile } from './support/feature-source'
import type {
  CheatIncidentResolutionAction,
  CheatIncidentResolutionRequest,
  CheatIncidentResolutionTarget,
} from '../app/composables/useCheatIncidentResolution'
import { describe, expect, test } from 'bun:test'
import { useCheatIncidentResolution } from '../app/composables/useCheatIncidentResolution'

const competitionId = 'competition-1'
const target: CheatIncidentResolutionTarget = {
  gameplayFactId: 'incident-1',
  sourceTeamId: 'team-1',
  sourceTeamName: 'Source Team',
}

interface SdkCall {
  action: CheatIncidentResolutionAction
  options: unknown
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

function createClients(calls: SdkCall[], wait?: Promise<void>) {
  const client = (action: CheatIncidentResolutionAction) => async (options: unknown) => {
    calls.push({ action, options })
    if (wait)
      await wait
    return { error: undefined }
  }

  return {
    confirm: client('confirm'),
    correct: client('correct'),
    dismiss: client('dismiss'),
  }
}

function createState(calls: SdkCall[] = [], wait?: Promise<void>) {
  return useCheatIncidentResolution({
    clients: createClients(calls, wait) as never,
    competitionId,
    readError: error => error instanceof Error ? error.message : '请求失败',
  })
}

describe('useCheatIncidentResolution', () => {
  test('opens matching dismiss and confirm dialogs with the affected team', () => {
    const state = createState()

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

  test('reports the remaining characters and does not call the SDK for a short reason', async () => {
    const calls: SdkCall[] = []
    const state = createState(calls)

    state.begin('dismiss', target)
    state.reason.value = 'short'

    expect(state.remainingCharacters.value).toBe(3)
    expect(state.canSubmit.value).toBe(false)
    expect(await state.submit()).toBe(false)
    expect(calls).toHaveLength(0)
  })

  test('dismiss calls only the dismiss SDK once with the trimmed reason', async () => {
    const calls: SdkCall[] = []
    const state = createState(calls)

    state.begin('dismiss', target)
    state.reason.value = '  reviewed evidence  '

    expect(await state.submit()).toBe(true)
    expect(calls).toEqual([{
      action: 'dismiss',
      options: {
        path: { competitionId, gameplayFactId: 'incident-1' },
        body: { status: 'Dismissed', reason: 'reviewed evidence' },
      },
    }])
  })

  test('confirm calls only the confirm SDK once with the affected incident', async () => {
    const calls: SdkCall[] = []
    const state = createState(calls)

    state.begin('confirm', target)
    state.reason.value = 'confirmed cross-team flag'

    expect(await state.submit()).toBe(true)
    expect(calls).toEqual([{
      action: 'confirm',
      options: {
        path: { competitionId, gameplayFactId: 'incident-1' },
        body: { status: 'Confirmed', reason: 'confirmed cross-team flag' },
      },
    }])
  })

  test('prevents duplicate submissions and closing while a request is pending', async () => {
    const pending = deferred<void>()
    const calls: SdkCall[] = []
    const state = createState(calls, pending.promise)

    state.begin('confirm', target)
    state.reason.value = 'confirmed cross-team flag'
    const first = state.submit()
    const duplicate = state.submit()
    state.setOpen(false)

    expect(state.isSubmitting.value).toBe(true)
    expect(state.isOpen.value).toBe(true)
    expect(calls).toHaveLength(1)
    expect(await duplicate).toBe(false)

    pending.resolve()
    expect(await first).toBe(true)
    expect(calls).toHaveLength(1)
  })

  test('success closes and clears state while retaining the completed action for the callback', async () => {
    const calls: SdkCall[] = []
    const completed: CheatIncidentResolutionRequest[] = []
    const state = useCheatIncidentResolution({
      clients: createClients(calls) as never,
      competitionId,
      onSuccess: request => completed.push(request),
      readError: () => '请求失败',
    })

    state.begin('dismiss', target)
    state.reason.value = 'reviewed evidence'

    expect(await state.submit()).toBe(true)
    expect(completed).toEqual([{
      action: 'dismiss',
      gameplayFactId: 'incident-1',
      reason: 'reviewed evidence',
    }])
    expect(state.isOpen.value).toBe(false)
    expect(state.action.value).toBeNull()
    expect(state.reason.value).toBe('')
  })

  test('failure retains the dialog and reason and exposes a readable error', async () => {
    const state = useCheatIncidentResolution({
      clients: {
        confirm: (async () => ({ error: undefined })) as never,
        correct: (async () => ({ error: undefined })) as never,
        dismiss: (async () => { throw new Error('事件已由其他管理员处置') }) as never,
      },
      competitionId,
      readError: error => error instanceof Error ? error.message : '请求失败',
    })

    state.begin('dismiss', target)
    state.reason.value = 'reviewed evidence'

    expect(await state.submit()).toBe(false)
    expect(state.isOpen.value).toBe(true)
    expect(state.action.value).toBe('dismiss')
    expect(state.reason.value).toBe('reviewed evidence')
    expect(state.error.value).toBe('事件已由其他管理员处置')
  })

  test('cancel and close reset state before reopening another action', () => {
    const state = createState()

    state.begin('dismiss', target)
    state.reason.value = 'draft reason'
    state.cancel()
    expect(state.isOpen.value).toBe(false)
    expect(state.action.value).toBeNull()
    expect(state.reason.value).toBe('')

    state.begin('confirm', target)
    expect(state.action.value).toBe('confirm')
    expect(state.reason.value).toBe('')
    state.setOpen(false)
    expect(state.target.value).toBeNull()
  })
})

describe('cheat incident page wiring', () => {
  test('uses a non-closing submit button and refreshes detail and list after success', async () => {
    const page = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/cheats.vue', import.meta.url),
    ).text()

    expect(page).toContain("@click=\"openAction('dismiss')\"")
    expect(page).toContain("@click=\"openAction('confirm')\"")
    expect(page).toContain('@click="handleResolutionSubmit"')
    expect(page).not.toMatch(/<AlertDialogAction[\s\S]*?@click="handleResolutionSubmit"/)
    expect(page).toContain("administration.competitionsBy.validation.reasonLeastRequired")
    expect(page).toContain('await openDetail(request.gameplayFactId)')
    expect(page).toContain('await refreshLatest()')
    expect(page).toContain('watchCompetition(competitionId')
    expect(page).toContain('competitionEventChanged: () => void refreshLatest()')
    expect(page).toContain('onReconnected: () => void refreshLatest()')
    expect(page).toContain('unwatchCompetition?.()')
    expect(page).toContain('v-if="detail.canConfirm || detail.canDismiss || detail.canCorrect"')
    expect(page).not.toContain('v-if="canWrite && (detail.canConfirm || detail.canDismiss || detail.canCorrect)"')
  })
})
