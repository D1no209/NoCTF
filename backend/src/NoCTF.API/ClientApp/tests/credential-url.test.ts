import { expect, test } from 'bun:test'
import { isCredentialSafeUrl } from '../app/lib/credential-url'

const origin = 'https://noctf.test'

test('credentials may only travel to this origin over http(s)', () => {
  expect(isCredentialSafeUrl('/api/v1/attachments/1', origin)).toBe(true)
  expect(isCredentialSafeUrl('https://noctf.test/api/v1/attachments/1', origin)).toBe(true)
  expect(isCredentialSafeUrl('https://noctf.test/api/v1/attachments/1?staff=true', origin)).toBe(true)
})

test('cross-origin, downgraded, non-http and credential-bearing targets are rejected', () => {
  expect(isCredentialSafeUrl('https://evil.test/api/v1/attachments/1', origin)).toBe(false)
  expect(isCredentialSafeUrl('//evil.test/api/v1/attachments/1', origin)).toBe(false)
  expect(isCredentialSafeUrl('ftp://noctf.test/api/v1/attachments/1', origin)).toBe(false)
  expect(isCredentialSafeUrl('javascript:alert(1)', origin)).toBe(false)
  expect(isCredentialSafeUrl('blob:https://noctf.test/9d0b', origin)).toBe(false)
  expect(isCredentialSafeUrl('https://user:pass@noctf.test/api', origin)).toBe(false)
  expect(isCredentialSafeUrl(null, origin)).toBe(false)
  expect(isCredentialSafeUrl(undefined, origin)).toBe(false)
  expect(isCredentialSafeUrl('', origin)).toBe(false)
})

test('without a browser origin nothing is treated as safe', () => {
  expect(isCredentialSafeUrl('/api/v1/attachments/1', undefined)).toBe(false)
})
