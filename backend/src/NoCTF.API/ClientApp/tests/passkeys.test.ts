import { describe, expect, test } from 'bun:test'
import { passkeyBrowserSupported, passkeyCancelled, passkeyCredentialJson } from '../app/features/authentication/passkeys/webauthn'
import { shouldRefreshSession } from '../app/lib/auth-refresh'
import { ensureLocaleDomains, setLocale, localizeMessage, message } from '../app/utils/i18n'
import { parseApiError } from '../app/utils/api-error'

describe('Passkey browser protocol', () => {
  test('unsupported or insecure contexts do not advertise passkey access', () => {
    expect(passkeyBrowserSupported()).toBeFalse()
  })
  test('cancellation is distinct from verifier failures', () => {
    expect(passkeyCancelled(new DOMException('cancel', 'NotAllowedError'))).toBeTrue()
    expect(passkeyCancelled(new DOMException('cancel', 'AbortError'))).toBeTrue()
    expect(passkeyCancelled(new DOMException('invalid', 'SecurityError'))).toBeFalse()
  })
  test('serializes credential bytes explicitly without invoking an overridden toJSON', () => {
    const original = Object.getOwnPropertyDescriptor(globalThis, 'AuthenticatorAttestationResponse')
    class Attestation {}
    Object.defineProperty(globalThis, 'AuthenticatorAttestationResponse', { value: Attestation, configurable: true })
    try {
      const value = Object.assign(new Attestation(), { clientDataJSON: new Uint8Array([1, 2, 255]).buffer,
        attestationObject: new Uint8Array([250, 251]).buffer, getTransports: () => ['internal'] })
      const credential = { id: 'AQ', rawId: new Uint8Array([1]).buffer, type: 'public-key', authenticatorAttachment: 'platform',
        response: value, getClientExtensionResults: () => ({}), toJSON: () => { throw new TypeError('Illegal invocation') } } as unknown as PublicKeyCredential
      const json = JSON.parse(passkeyCredentialJson(credential))
      expect(json.rawId).toBe('AQ'); expect(json.response.clientDataJSON).toBe('AQL_')
      expect(json.response.attestationObject).toBe('-vs'); expect(json.response.transports).toEqual(['internal'])
      expect(json.clientExtensionResults).toEqual({})
    } finally {
      if (original) Object.defineProperty(globalThis, 'AuthenticatorAttestationResponse', original)
      else Reflect.deleteProperty(globalThis, 'AuthenticatorAttestationResponse')
    }
  })
  test('passkey failures do not restart the refresh loop and remain bilingual', async () => {
    expect(shouldRefreshSession(401, new Headers({ Authorization: 'Bearer expired' }), '/api/v1/auth/passkeys/login/complete')).toBeFalse()
    await ensureLocaleDomains(['passkeys'])
    expect(parseApiError({ code: 'PasskeyInvalidCredential' }).displayMessage).toEqual(message('passkeys.error.InvalidCredential'))
    setLocale('en'); expect(localizeMessage(message('passkeys.error.FlowExpired'))).toContain('expired')
    setLocale('zh-CN'); expect(localizeMessage(message('passkeys.error.FlowExpired'))).toContain('已过期')
  })
})
