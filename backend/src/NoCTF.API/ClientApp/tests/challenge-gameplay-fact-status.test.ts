import { describe, expect, test } from 'bun:test'
import type { NoCTFAPIEndpointsGameplayFactsGameplayFactStatusResponse } from '../app/api/models'
import { createChallengeGameplayFactStatusReader } from '../app/features/challenges/challenge-gameplay-fact-status'

describe('challenge gameplay fact status reads', () => {
  test('shares one pending request for the same competition and fact', async () => {
    let calls = 0
    let complete!: (status: NoCTFAPIEndpointsGameplayFactsGameplayFactStatusResponse) => void
    const reader = createChallengeGameplayFactStatusReader(undefined, () => {
      calls++
      if (calls > 1) return Promise.resolve({ state: 'Completed', result: 'Correct' })
      return new Promise(resolve => { complete = resolve })
    })

    const first = reader('COMPETITION', 'AABB-CCDD')
    const second = reader('competition', 'aabbccdd')
    expect(first).toBe(second)
    expect(calls).toBe(1)

    complete({ gameplayFactId: 'aabbccdd', state: 'Completed', result: 'Correct' })
    expect((await first).result).toBe('Correct')
    expect((await second).result).toBe('Correct')
    await reader('competition', 'aabbccdd')
    expect(calls).toBe(2)
  })

  test('clears failed requests so polling can retry', async () => {
    let calls = 0
    const reader = createChallengeGameplayFactStatusReader(undefined, async () => {
      calls++
      if (calls === 1) throw new Error('temporary failure')
      return { state: 'Completed', result: 'Wrong' }
    })

    await expect(reader('competition', 'fact')).rejects.toThrow('temporary failure')
    expect((await reader('competition', 'fact')).result).toBe('Wrong')
    expect(calls).toBe(2)
  })
})
