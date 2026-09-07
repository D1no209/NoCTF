import { expect, test } from 'bun:test'
import { prepareCommandRequest, observeCommandResponse, createCommandAttempt } from '../app/utils/command-attempt'

test('network retries share a nonce but completed identical submissions are new intents', async () => {
  const url = 'https://example.invalid/commands'
  const request = () => new Request(url, { method: 'POST' })
  const first = request()
  await prepareCommandRequest(first, { flag: 'test-value' })
  observeCommandResponse(first, undefined, true)
  const retry = request()
  await prepareCommandRequest(retry, { flag: 'test-value' })
  expect(retry.headers.get('Idempotency-Key')).toBe(first.headers.get('Idempotency-Key'))
  observeCommandResponse(retry, 202)
  const deliberate = request()
  await prepareCommandRequest(deliberate, { flag: 'test-value' })
  expect(deliberate.headers.get('Idempotency-Key')).not.toBe(first.headers.get('Idempotency-Key'))
})

test('changed content and explicit command completion create new intent identifiers', () => {
  const attempt = createCommandAttempt()
  const first = attempt.headers({ delta: 10 })['Idempotency-Key']
  expect(attempt.headers({ delta: 10 })['Idempotency-Key']).toBe(first)
  expect(attempt.headers({ delta: 20 })['Idempotency-Key']).not.toBe(first)
  const current = attempt.headers({ delta: 20 })['Idempotency-Key']
  attempt.completed()
  expect(attempt.headers({ delta: 20 })['Idempotency-Key']).not.toBe(current)
})
