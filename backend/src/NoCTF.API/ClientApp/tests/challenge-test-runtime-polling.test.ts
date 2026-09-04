import { describe, expect, test } from 'bun:test'
import { evaluateChallengeTestRuntimePolling } from '../app/utils/challenge-test-runtime-polling'

describe('challenge test runtime polling', () => {
  test('keeps polling when the newly accepted runtime is briefly missing', () => {
    expect(evaluateChallengeTestRuntimePolling('missing', null, {
      kind: 'start',
      runtimeInstanceId: 'runtime-new',
    })).toEqual({
      continuePolling: true,
      mutationObserved: false,
    })
  })

  test('waits for the accepted runtime id and then follows its lifecycle', () => {
    const pending = {
      kind: 'reset' as const,
      runtimeInstanceId: 'runtime-new',
      previousRuntimeInstanceId: 'runtime-old',
    }
    expect(evaluateChallengeTestRuntimePolling('available', {
      id: 'runtime-old',
      state: 'Running',
      flagState: 'Succeeded',
    }, pending).continuePolling).toBeTrue()
    expect(evaluateChallengeTestRuntimePolling('available', {
      id: 'runtime-new',
      state: 'Queued',
      flagState: 'Pending',
    }, pending)).toEqual({
      continuePolling: true,
      mutationObserved: true,
    })
  })

  test('does not stop polling on a transient status request failure', () => {
    expect(evaluateChallengeTestRuntimePolling('failed', null, null)).toEqual({
      continuePolling: true,
      mutationObserved: false,
    })
  })

  test('stops only after stop and extend mutations are observable', () => {
    expect(evaluateChallengeTestRuntimePolling('available', {
      id: 'runtime-1',
      state: 'Running',
      flagState: 'Succeeded',
      expiresAt: '2026-09-04T12:10:00Z',
    }, {
      kind: 'stop',
      runtimeInstanceId: 'runtime-1',
    }).continuePolling).toBeTrue()
    expect(evaluateChallengeTestRuntimePolling('available', {
      id: 'runtime-1',
      state: 'Stopped',
      flagState: 'Canceled',
    }, {
      kind: 'stop',
      runtimeInstanceId: 'runtime-1',
    })).toEqual({
      continuePolling: false,
      mutationObserved: true,
    })
    expect(evaluateChallengeTestRuntimePolling('available', {
      id: 'runtime-1',
      state: 'Running',
      flagState: 'Succeeded',
      expiresAt: '2026-09-04T12:40:00Z',
    }, {
      kind: 'extend',
      runtimeInstanceId: 'runtime-1',
      previousExpiresAt: '2026-09-04T12:10:00Z',
    })).toEqual({
      continuePolling: false,
      mutationObserved: true,
    })
  })
})
